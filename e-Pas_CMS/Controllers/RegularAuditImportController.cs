using System.Globalization;
using System.Text;
using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace e_Pas_CMS.Controllers;

[Authorize]
public class RegularAuditImportController : Controller
{
    private readonly EpasDbContext _context;
    private readonly ILogger<RegularAuditImportController> _logger;

    public RegularAuditImportController(
        EpasDbContext context,
        ILogger<RegularAuditImportController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Upload(IFormFile file) =>
        UploadInternal(file, "Regular Audit", "AuditReport");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UploadBasicOperational(IFormFile file) =>
        UploadInternal(file, "Basic Operational", "BasicOperationalReport");

    private async Task<IActionResult> UploadInternal(
        IFormFile file,
        string defaultAuditType,
        string redirectController)
    {
        var importLabel = defaultAuditType;
        var isBasicOperational =
            defaultAuditType.Equals("Basic Operational", StringComparison.OrdinalIgnoreCase);
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "File CSV belum dipilih.";
            return RedirectToAction("Index", redirectController);
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = $"Template {importLabel} harus berupa file CSV.";
            return RedirectToAction("Index", redirectController);
        }

        var currentUser = User.Identity?.Name ?? "SYSTEM";
        var nowWithoutTimeZone = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        var nowUtc = DateTime.UtcNow;

        _context.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));

        try
        {
            List<RegularAuditImportRow> rows;

            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true))
            {
                rows = ReadCsv(reader);
            }

            if (rows.Count == 0)
                throw new InvalidOperationException("CSV tidak memiliki data.");

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.AuditType))
                    row.AuditType = defaultAuditType;

                // Basic Operational historical CSV uses "result" instead of good_status.
                if (isBasicOperational && string.IsNullOrWhiteSpace(row.GoodStatus))
                    row.GoodStatus = row.Result;
            }

            var validRows = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.SpbuNo) && x.AuditDate.HasValue)
                .ToList();

            var skipped = rows.Count - validRows.Count;

            if (validRows.Count == 0)
                throw new InvalidOperationException("Tidak ada row valid. spbu_no dan Audit Date wajib diisi.");

            // =========================================================
            // PRELOAD MASTER / EXISTING DATA - OUTSIDE WRITE TRANSACTION
            // =========================================================

            var spbuNos = validRows
                .Select(x => x.SpbuNo.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Tracked because full CSV is allowed to enrich existing SPBU master fields.
            var existingSpbus = await _context.spbus
                .Where(x => spbuNos.Contains(x.spbu_no))
                .ToListAsync();

            var spbuByNo = existingSpbus
                .GroupBy(x => x.spbu_no, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var allQuestionnaires = await _context.master_questioners
                .AsNoTracking()
                .Where(x => x.category == "CHECKLIST")
                .OrderByDescending(x => x.version)
                .ToListAsync();

            foreach (var row in validRows)
            {
                row.ResolvedMasterQuestionerChecklistId = ResolveMasterQuestionerChecklistId(row, allQuestionnaires);
            }

            var requiredQuestionnaireIds = validRows
                .Select(x => x.ResolvedMasterQuestionerChecklistId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var questionMapByQuestionnaire = await LoadQuestionMapAsync(requiredQuestionnaireIds);

            // Resolve auditor/verifier identities by username first, then by exact name.
            var userTokens = validRows
                .SelectMany(x => new[]
                {
                    x.Auditor1Username, x.Auditor2Username, x.VerifierUsername,
                    x.Auditor1Name, x.Auditor2Name, x.VerifierName
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidateUsers = userTokens.Count == 0
                ? new List<app_user>()
                : await _context.app_users
                    .AsNoTracking()
                    .Where(x => userTokens.Contains(x.username) || userTokens.Contains(x.name))
                    .ToListAsync();

            var userByUsername = candidateUsers
                .Where(x => !string.IsNullOrWhiteSpace(x.username))
                .GroupBy(x => x.username, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var userByName = candidateUsers
                .Where(x => !string.IsNullOrWhiteSpace(x.name))
                .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var minAuditDate = DateTime.SpecifyKind(
                validRows.Min(x => x.AuditDate!.Value.Date),
                DateTimeKind.Unspecified);

            var maxAuditDateExclusive = DateTime.SpecifyKind(
                validRows.Max(x => x.AuditDate!.Value.Date).AddDays(1),
                DateTimeKind.Unspecified);

            var existingSpbuIds = existingSpbus
                .Select(x => x.id)
                .Distinct()
                .ToList();

            var existingAudits = new List<trx_audit>();

            if (existingSpbuIds.Count > 0)
            {
                var auditQuery = _context.trx_audits
                    .Where(x =>
                        existingSpbuIds.Contains(x.spbu_id) &&
                        x.audit_execution_time >= minAuditDate &&
                        x.audit_execution_time < maxAuditDateExclusive);

                auditQuery = isBasicOperational
                    ? auditQuery.Where(x => x.audit_type == "Basic Operational")
                    : auditQuery.Where(x => x.audit_type != "Basic Operational");

                existingAudits = await auditQuery.ToListAsync();
            }

            static string AuditKey(string spbuId, DateTime date, string level) =>
                $"{spbuId}|{date:yyyyMMdd}|{level.Trim().ToUpperInvariant()}";

            var auditByKey = existingAudits
                .Where(x => x.audit_execution_time.HasValue)
                .GroupBy(x => AuditKey(
                    x.spbu_id,
                    x.audit_execution_time!.Value.Date,
                    string.IsNullOrWhiteSpace(x.audit_level) ? "-" : x.audit_level))
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var existingAuditIds = existingAudits
                .Select(x => x.id)
                .Distinct()
                .ToList();

            var existingSummaries = existingAuditIds.Count == 0
                ? new List<trx_audit_import_summary>()
                : await _context.trx_audit_import_summaries
                    .Where(x => existingAuditIds.Contains(x.trx_audit_id))
                    .ToListAsync();

            var summaryByAuditId = existingSummaries
                .GroupBy(x => x.trx_audit_id)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var invoiceAuditIds = existingAuditIds.Count == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    await _context.TrxInvoiceDetails
                        .AsNoTracking()
                        .Where(x => existingAuditIds.Contains(x.TrxAuditId))
                        .Select(x => x.TrxAuditId)
                        .ToListAsync(),
                    StringComparer.OrdinalIgnoreCase);

            await using var dbTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await _context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '8s';");
                await _context.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '120s';");

                // =====================================================
                // STAGE 1 - SPBU MASTER
                // =====================================================

                foreach (var row in validRows)
                {
                    var spbuNo = row.SpbuNo.Trim();
                    var auditDate = AsUnspecified(row.AuditDate!.Value.Date);

                    if (!spbuByNo.TryGetValue(spbuNo, out var spbu))
                    {
                        spbu = new spbu
                        {
                            id = Guid.NewGuid().ToString(),
                            spbu_no = spbuNo,
                            region = RequiredFallback(row.Region),
                            province_name = RequiredFallback(row.ProvinceName),
                            city_name = RequiredFallback(row.CityName),
                            status = "ACTIVE",
                            created_by = currentUser,
                            created_date = nowWithoutTimeZone,
                            updated_by = currentUser,
                            updated_date = nowWithoutTimeZone,
                            wtms = row.Wtms ?? 0m,
                            qq = row.Qq ?? 0m,
                            wmef = row.Wmef ?? 0m,
                            format_fisik = row.FormatFisik ?? 0m,
                            cpo = row.Cpo ?? 0m
                        };

                        _context.spbus.Add(spbu);
                        spbuByNo[spbuNo] = spbu;
                    }

                    // Only overwrite master text if the full CSV actually supplies a value.
                    SetIfNotBlank(row.Region, v => spbu.region = v);
                    SetIfNotBlank(row.ProvinceName, v => spbu.province_name = v);
                    SetIfNotBlank(row.CityName, v => spbu.city_name = v);
                    SetIfNotBlank(row.Address, v => spbu.address = v);
                    SetIfNotBlank(row.TipeSpbu, v => spbu.owner_type = v);
                    SetIfNotBlank(row.Rayon, v => spbu.sbm = v);
                    SetIfNotBlank(row.Sam, v => spbu.sam = v);
                    SetIfNotBlank(row.OwnerName, v => spbu.owner_name = v);
                    SetIfNotBlank(row.ManagerName, v => spbu.manager_name = v);
                    SetIfNotBlank(row.Mor, v => spbu.mor = v);
                    SetIfNotBlank(row.SalesArea, v => spbu.sales_area = v);
                    SetIfNotBlank(row.PhoneNumber1, v => spbu.phone_number_1 = v);
                    SetIfNotBlank(row.KelasSpbu, v => spbu.level = v);
                    SetIfNotBlank(row.AuditNext, v => spbu.audit_next = v);
                    SetIfNotBlank(row.AuditLevel, v => spbu.audit_current = v);
                    SetIfNotBlank(row.GoodStatus, v => spbu.status_good = v);
                    SetIfNotBlank(row.ExcellentStatus, v => spbu.status_excellent = v);

                    if (row.Year.HasValue) spbu.year = row.Year;
                    if (row.Quarter.HasValue) spbu.quater = row.Quarter;
                    if (row.Wtms.HasValue) spbu.wtms = row.Wtms.Value;
                    if (row.Qq.HasValue) spbu.qq = row.Qq.Value;
                    if (row.Wmef.HasValue) spbu.wmef = row.Wmef.Value;
                    if (row.FormatFisik.HasValue) spbu.format_fisik = row.FormatFisik.Value;
                    if (row.Cpo.HasValue) spbu.cpo = row.Cpo.Value;

                    spbu.audit_current_score = row.TotalScore;
                    spbu.audit_current_time = auditDate;
                    spbu.updated_by = currentUser;
                    spbu.updated_date = nowWithoutTimeZone;
                }

                await SaveStageAsync("SPBU");

                // =====================================================
                // STAGE 2 - AUDIT HEADER + SUMMARY
                // =====================================================

                var created = 0;
                var updated = 0;
                var auditRows = new List<AuditImportContext>();

                foreach (var row in validRows)
                {
                    var spbuNo = row.SpbuNo.Trim();
                    var spbu = spbuByNo[spbuNo];
                    var auditDate = AsUnspecified(row.AuditDate!.Value.Date);
                    var sendDate = AsUnspecified((row.SendDate ?? row.AuditDate.Value).Date);
                    var normalizedAuditLevel = string.IsNullOrWhiteSpace(row.AuditLevel)
                        ? "-"
                        : row.AuditLevel.Trim();

                    var auditKey = AuditKey(spbu.id, auditDate, normalizedAuditLevel);

                    if (!auditByKey.TryGetValue(auditKey, out var audit))
                    {
                        audit = new trx_audit
                        {
                            id = Guid.NewGuid().ToString(),
                            spbu_id = spbu.id,
                            audit_level = normalizedAuditLevel,
                            audit_type = string.IsNullOrWhiteSpace(row.AuditType)
                                ? defaultAuditType
                                : row.AuditType.Trim(),
                            status = "VERIFIED",
                            form_type_auditor1 = "FULL",
                            form_status_auditor1 = "COMPLETED",
                            km_range = row.KmRange ?? 0m,
                            created_by = currentUser,
                            created_date = row.AuditCreatedDate.HasValue
                                ? AsUnspecified(row.AuditCreatedDate.Value)
                                : sendDate,
                            updated_by = currentUser,
                            updated_date = nowWithoutTimeZone
                        };

                        _context.trx_audits.Add(audit);
                        auditByKey[auditKey] = audit;
                        created++;
                    }
                    else
                    {
                        updated++;
                    }

                    var auditor1 = ResolveUser(row.Auditor1Username, row.Auditor1Name, userByUsername, userByName);
                    var auditor2 = ResolveUser(row.Auditor2Username, row.Auditor2Name, userByUsername, userByName);
                    var verifier = ResolveUser(row.VerifierUsername, row.VerifierName, userByUsername, userByName);

                    audit.report_prefix = NullIfEmpty(row.ReportPrefix) ?? audit.report_prefix ?? "IMP";
                    audit.report_no = NullIfEmpty(row.ReportNo) ?? audit.report_no ?? GenerateReportNo(row);

                    if (row.HasAuditor1Column)
                        audit.app_user_id = auditor1?.id;

                    if (row.HasAuditor2Column)
                        audit.app_user_id_auditor2 = auditor2?.id;

                    if (!string.IsNullOrWhiteSpace(row.ResolvedMasterQuestionerChecklistId))
                        audit.master_questioner_checklist_id = row.ResolvedMasterQuestionerChecklistId;
                    audit.audit_level = normalizedAuditLevel;
                    audit.audit_type = string.IsNullOrWhiteSpace(row.AuditType)
                        ? defaultAuditType
                        : row.AuditType.Trim();
                    audit.score = row.TotalScore;
                    audit.is_imported = true;
                    audit.audit_schedule_date = row.AuditScheduleDate.HasValue
                        ? DateOnly.FromDateTime(row.AuditScheduleDate.Value)
                        : DateOnly.FromDateTime(auditDate);
                    audit.audit_execution_time = auditDate;
                    if (row.HasAuditMomIntroColumn)
                        audit.audit_mom_intro = NullIfEmpty(row.AuditMomIntro);

                    if (row.HasAuditMomFinalColumn)
                        audit.audit_mom_final = NullIfEmpty(row.AuditMomFinal);

                    audit.status = "VERIFIED";
                    audit.form_type_auditor1 = "FULL";
                    audit.form_status_auditor1 = "COMPLETED";
                    audit.good_status = NullIfEmpty(row.GoodStatus);
                    audit.excellent_status = NullIfEmpty(row.ExcellentStatus);
                    if (row.HasVerifierColumn)
                    {
                        audit.approval_by = verifier?.username
                            ?? NullIfEmpty(row.VerifierUsername)
                            ?? audit.approval_by;
                    }
                    else if (string.IsNullOrWhiteSpace(audit.approval_by))
                    {
                        audit.approval_by = currentUser;
                    }
                    audit.approval_date = row.ApprovalDate.HasValue
                        ? AsUnspecified(row.ApprovalDate.Value)
                        : sendDate;
                    audit.km_range = row.KmRange ?? audit.km_range;
                    audit.updated_by = currentUser;
                    audit.updated_date = nowWithoutTimeZone;

                    if (!summaryByAuditId.TryGetValue(audit.id, out var summary))
                    {
                        summary = new trx_audit_import_summary
                        {
                            id = Guid.NewGuid().ToString(),
                            trx_audit_id = audit.id,
                            created_by = currentUser,
                            created_date = nowWithoutTimeZone
                        };

                        _context.trx_audit_import_summaries.Add(summary);
                        summaryByAuditId[audit.id] = summary;
                    }

                    summary.send_date = sendDate;
                    summary.audit_date = auditDate;
                    summary.total_score = row.TotalScore;
                    summary.sss = row.Sss;
                    summary.eqnq = row.Eqnq;
                    summary.rfs = row.Rfs;
                    summary.vfc = row.Vfc;
                    summary.epo = row.Epo;
                    summary.wtms = row.Wtms;
                    summary.qq = row.Qq;
                    summary.wmef = row.Wmef;
                    summary.format_fisik = row.FormatFisik;
                    summary.cpo = row.Cpo;
                    summary.kelas_spbu = NullIfEmpty(row.KelasSpbu);
                    summary.audit_next = NullIfEmpty(row.AuditNext);
                    summary.penalty_good_alerts = NullIfEmpty(row.PenaltyGoodAlerts);
                    summary.penalty_excellent_alerts = NullIfEmpty(row.PenaltyExcellentAlerts);
                    summary.source_file = Path.GetFileName(file.FileName);

                    auditRows.Add(new AuditImportContext
                    {
                        Row = row,
                        Audit = audit,
                        SpbuNo = spbuNo
                    });
                }

                await SaveStageAsync("AUDIT/REPORT");

                // =====================================================
                // STAGE 3 - CHECKLIST + QQ DETAIL (NO MEDIA)
                // =====================================================

                var detailAuditIds = auditRows
                    .Where(x => x.Row.HasChecklistPayload || x.Row.HasQqWideColumns)
                    .Select(x => x.Audit.id)
                    .Distinct()
                    .ToList();

                if (detailAuditIds.Count > 0)
                {
                    var existingChecklist = await _context.trx_audit_checklists
                        .Where(x => detailAuditIds.Contains(x.trx_audit_id))
                        .ToListAsync();

                    var qqAuditIds = auditRows
                        .Where(x => x.Row.HasQqWideColumns)
                        .Select(x => x.Audit.id)
                        .Distinct()
                        .ToList();

                    var existingQq = qqAuditIds.Count == 0
                        ? new List<trx_audit_qq>()
                        : await _context.trx_audit_qqs
                            .Where(x => qqAuditIds.Contains(x.trx_audit_id))
                            .ToListAsync();

                    // Wide checklist columns are authoritative when present.
                    var checklistReplaceIds = auditRows
                        .Where(x => x.Row.HasChecklistPayload)
                        .Select(x => x.Audit.id)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    _context.trx_audit_checklists.RemoveRange(
                        existingChecklist.Where(x => checklistReplaceIds.Contains(x.trx_audit_id)));

                    // Wide QQ columns are authoritative when present. If all QQ cells
                    // are blank, existing QQ rows are intentionally cleared.
                    _context.trx_audit_qqs.RemoveRange(existingQq);

                    await SaveStageAsync("DETAIL-CLEAR");
                }

                foreach (var ctx in auditRows)
                {
                    var row = ctx.Row;
                    var audit = ctx.Audit;

                    if (row.HasChecklistPayload)
                    {
                        if (string.IsNullOrWhiteSpace(audit.master_questioner_checklist_id))
                        {
                            throw new InvalidOperationException(
                                $"Checklist audit {row.SpbuNo} {row.AuditDate:yyyy-MM-dd} tidak dapat diimport karena master_questioner_checklist_id tidak ditemukan.");
                        }

                        if (!questionMapByQuestionnaire.TryGetValue(
                                audit.master_questioner_checklist_id,
                                out var questionMap))
                        {
                            throw new InvalidOperationException(
                                $"Master checklist {audit.master_questioner_checklist_id} tidak memiliki QUESTION yang dapat dipetakan.");
                        }

                        var checklistPayload = BuildChecklistPayload(row, questionMap);

                        foreach (var item in checklistPayload)
                        {
                            _context.trx_audit_checklists.Add(new trx_audit_checklist
                            {
                                id = Guid.NewGuid().ToString(),
                                trx_audit_id = audit.id,
                                master_questioner_detail_id = item.Question.Id,
                                score_input = NullIfEmpty(item.ScoreInput)?.ToUpperInvariant(),
                                score_af = item.ScoreAf ?? CalculateScoreAf(item.ScoreInput, item.Question.IsRelaksasi),
                                score_x = item.ScoreX,
                                comment = NullIfEmpty(item.Comment),
                                status = "ACTIVE",
                                created_by = currentUser,
                                created_date = nowWithoutTimeZone,
                                updated_by = currentUser,
                                updated_date = nowWithoutTimeZone
                            });
                        }
                    }

                    if (row.HasQqWideColumns)
                    {
                        var qqItems = ParseQqWide(row);

                        foreach (var qq in qqItems)
                        {
                            // At minimum a QQ row needs a nozzle number because the
                            // existing entity/database requires it.
                            if (string.IsNullOrWhiteSpace(qq.NozzleNumber))
                                continue;

                            _context.trx_audit_qqs.Add(new trx_audit_qq
                            {
                                id = Guid.NewGuid().ToString(),
                                trx_audit_id = audit.id,
                                nozzle_number = qq.NozzleNumber.Trim(),
                                du_make = NullIfEmpty(qq.DuMake),
                                du_serial_no = NullIfEmpty(qq.DuSerialNo),
                                product = NullIfEmpty(qq.Product),
                                mode = NullIfEmpty(qq.Mode),
                                quantity_variation_with_measure = qq.QuantityVariationWithMeasure,
                                quantity_variation_in_percentage = qq.QuantityVariationInPercentage,
                                observed_density = qq.ObservedDensity,
                                observed_temp = qq.ObservedTemp,
                                observed_density_15_degree = qq.ObservedDensity15Degree,
                                reference_density_15_degree = qq.ReferenceDensity15Degree,
                                tank_number = NullIfEmpty(qq.TankNumber),
                                density_variation = qq.DensityVariation,
                                status = "ACTIVE",
                                created_by = currentUser,
                                created_date = nowWithoutTimeZone,
                                updated_by = currentUser,
                                updated_date = nowWithoutTimeZone
                            });
                        }
                    }
                }

                await SaveStageAsync("CHECKLIST/QQ");

                // =====================================================
                // STAGE 4 - FINANCE
                // =====================================================

                foreach (var item in auditRows)
                {
                    if (invoiceAuditIds.Contains(item.Audit.id))
                        continue;

                    AddFinanceInvoice(item.Audit, item.SpbuNo, currentUser, nowUtc);
                    invoiceAuditIds.Add(item.Audit.id);
                }

                await SaveStageAsync("FINANCE");
                await dbTransaction.CommitAsync();

                var fullRows = auditRows.Count(x => x.Row.HasChecklistPayload || x.Row.HasQqWideColumns);

                TempData["Success"] =
                    $"Upload {importLabel} berhasil. New={created}, Updated={updated}, FullDetail={fullRows}, Skipped={skipped}.";

                return RedirectToAction("Index", redirectController);
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{AuditType} CSV import failed for {FileName}", importLabel, file.FileName);
            TempData["Error"] = $"Upload {importLabel} gagal: {GetImportError(ex)}";
            return RedirectToAction("Index", redirectController);
        }
    }

    // =============================================================
    // QUESTIONNAIRE / CHECKLIST HELPERS
    // =============================================================

    private static string? ResolveMasterQuestionerChecklistId(
        RegularAuditImportRow row,
        List<master_questioner> questionnaires)
    {
        if (!string.IsNullOrWhiteSpace(row.MasterQuestionerChecklistId))
        {
            var explicitMatch = questionnaires.FirstOrDefault(x =>
                string.Equals(x.id, row.MasterQuestionerChecklistId.Trim(), StringComparison.OrdinalIgnoreCase));

            if (explicitMatch != null)
                return explicitMatch.id;
        }

        var questionnaireType = string.IsNullOrWhiteSpace(row.AuditType) ||
                                row.AuditType.Equals("Regular Audit", StringComparison.OrdinalIgnoreCase)
            ? "Mystery Audit"
            : row.AuditType.Trim();

        if (row.MasterQuestionerVersion.HasValue)
        {
            var versionMatch = questionnaires
                .Where(x =>
                    x.version == row.MasterQuestionerVersion.Value &&
                    x.category == "CHECKLIST" &&
                    x.type.Equals(questionnaireType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.version)
                .FirstOrDefault();

            if (versionMatch != null)
                return versionMatch.id;
        }

        // Same convention already used by Scheduler for Regular Audit.
        var fallback = questionnaires
            .Where(x =>
                x.category == "CHECKLIST" &&
                x.type.Equals(questionnaireType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.version)
            .FirstOrDefault();

        if (fallback != null)
            return fallback.id;

        // Last safety fallback if a deployment stores Regular Audit literally.
        return questionnaires
            .Where(x =>
                x.category == "CHECKLIST" &&
                x.type.Equals("Regular Audit", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.version)
            .Select(x => x.id)
            .FirstOrDefault();
    }

    private async Task<Dictionary<string, QuestionMap>> LoadQuestionMapAsync(string[] questionnaireIds)
    {
        var result = new Dictionary<string, QuestionMap>(StringComparer.OrdinalIgnoreCase);

        if (questionnaireIds.Length == 0)
            return result;

        var connectionString = _context.Database.GetConnectionString();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var rows = (await conn.QueryAsync<QuestionMapItem>(@"
            SELECT
                id AS ""Id"",
                master_questioner_id AS ""MasterQuestionerId"",
                number AS ""Number"",
                COALESCE(is_relaksasi, false) AS ""IsRelaksasi""
            FROM master_questioner_detail
            WHERE master_questioner_id = ANY(@ids)
              AND type = 'QUESTION'
            ORDER BY master_questioner_id, order_no;",
            new { ids = questionnaireIds })).ToList();

        foreach (var group in rows.GroupBy(x => x.MasterQuestionerId, StringComparer.OrdinalIgnoreCase))
        {
            result[group.Key] = new QuestionMap(group.ToList());
        }

        return result;
    }

    private static List<ResolvedChecklistItem> BuildChecklistPayload(
        RegularAuditImportRow row,
        QuestionMap questionMap)
    {
        var result = new List<ResolvedChecklistItem>();

        // Human-friendly wide format:
        //   1.1.1.a            = A/B/C/D/E/F/X
        //   1.1.1.a_comment    = free text comment
        //   1.1.1.a_score_x    = numeric X score when score_input = X
        // Legacy double-underscore suffixes are accepted too.
        foreach (var question in questionMap.Items)
        {
            if (string.IsNullOrWhiteSpace(question.Number))
                continue;

            var number = question.Number.Trim();
            var score = row.GetRaw(number);

            var comment = FirstNotBlank(
                row.GetRaw($"{number}_comment"),
                row.GetRaw($"{number}__comment"));

            var scoreX = ParseDecimal(FirstNotBlank(
                row.GetRaw($"{number}_score_x"),
                row.GetRaw($"{number}__score_x")));

            if (string.IsNullOrWhiteSpace(score) &&
                string.IsNullOrWhiteSpace(comment) &&
                !scoreX.HasValue)
            {
                continue;
            }

            var normalizedScore = string.IsNullOrWhiteSpace(score)
                ? null
                : score.Trim().ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(normalizedScore) &&
                normalizedScore is not ("A" or "B" or "C" or "D" or "E" or "F" or "X"))
            {
                throw new InvalidOperationException(
                    $"Nilai checklist '{number}' untuk SPBU {row.SpbuNo} tidak valid: '{score}'. Gunakan A/B/C/D/E/F/X atau kosong.");
            }

            result.Add(new ResolvedChecklistItem
            {
                Question = question,
                ScoreInput = normalizedScore,
                ScoreAf = CalculateScoreAf(normalizedScore, question.IsRelaksasi),
                ScoreX = scoreX,
                Comment = comment
            });
        }

        return result;
    }

    private static decimal? CalculateScoreAf(string? scoreInput, bool isRelaksasi)
    {
        var score = scoreInput?.Trim().ToUpperInvariant();

        if (isRelaksasi && score == "F")
            return 1.00m;

        return score switch
        {
            "A" => 1.00m,
            "B" => 0.80m,
            "C" => 0.60m,
            "D" => 0.40m,
            "E" => 0.20m,
            "F" => 0.00m,
            _ => null
        };
    }

    private static List<QqCsvPayloadItem> ParseQqWide(RegularAuditImportRow row)
    {
        var indexes = row.RawValues.Keys
            .Select(header => TryGetQqColumnIndex(header, out var index) ? index : (int?)null)
            .Where(index => index.HasValue)
            .Select(index => index!.Value)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        var result = new List<QqCsvPayloadItem>();

        foreach (var index in indexes)
        {
            var prefix = $"qq_{index}_";

            var item = new QqCsvPayloadItem
            {
                NozzleNumber = row.GetRaw(prefix + "nozzle_number"),
                DuMake = row.GetRaw(prefix + "du_make"),
                DuSerialNo = row.GetRaw(prefix + "du_serial_no"),
                Product = row.GetRaw(prefix + "product"),
                Mode = row.GetRaw(prefix + "mode"),
                QuantityVariationWithMeasure = ParseDecimal(row.GetRaw(prefix + "quantity_variation_with_measure")),
                QuantityVariationInPercentage = ParseDecimal(row.GetRaw(prefix + "quantity_variation_in_percentage")),
                ObservedDensity = ParseDecimal(row.GetRaw(prefix + "observed_density")),
                ObservedTemp = ParseDecimal(row.GetRaw(prefix + "observed_temp")),
                ObservedDensity15Degree = ParseDecimal(row.GetRaw(prefix + "observed_density_15_degree")),
                ReferenceDensity15Degree = ParseDecimal(row.GetRaw(prefix + "reference_density_15_degree")),
                TankNumber = row.GetRaw(prefix + "tank_number"),
                DensityVariation = ParseDecimal(row.GetRaw(prefix + "density_variation"))
            };

            var hasAnyValue =
                !string.IsNullOrWhiteSpace(item.NozzleNumber) ||
                !string.IsNullOrWhiteSpace(item.DuMake) ||
                !string.IsNullOrWhiteSpace(item.DuSerialNo) ||
                !string.IsNullOrWhiteSpace(item.Product) ||
                !string.IsNullOrWhiteSpace(item.Mode) ||
                item.QuantityVariationWithMeasure.HasValue ||
                item.QuantityVariationInPercentage.HasValue ||
                item.ObservedDensity.HasValue ||
                item.ObservedTemp.HasValue ||
                item.ObservedDensity15Degree.HasValue ||
                item.ReferenceDensity15Degree.HasValue ||
                !string.IsNullOrWhiteSpace(item.TankNumber) ||
                item.DensityVariation.HasValue;

            if (!hasAnyValue)
                continue;

            if (string.IsNullOrWhiteSpace(item.NozzleNumber))
            {
                throw new InvalidOperationException(
                    $"QQ baris {index} untuk SPBU {row.SpbuNo} memiliki data tetapi qq_{index}_nozzle_number kosong.");
            }

            result.Add(item);
        }

        return result;
    }

    private static bool TryGetQqColumnIndex(string? header, out int index)
    {
        index = 0;

        if (string.IsNullOrWhiteSpace(header))
            return false;

        var value = header.Trim();
        if (!value.StartsWith("qq_", StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = value.Substring(3);
        var separator = rest.IndexOf('_');
        if (separator <= 0)
            return false;

        return int.TryParse(rest.Substring(0, separator), out index) && index > 0;
    }

    private static bool LooksLikeChecklistColumn(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return false;

        var value = header.Trim();

        if (value.StartsWith("qq_", StringComparison.OrdinalIgnoreCase))
            return false;

        var suffixes = new[]
        {
            "__comment", "__score_x",
            "_comment", "_score_x"
        };

        foreach (var suffix in suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^suffix.Length];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(value) || !char.IsDigit(value[0]))
            return false;

        return value.All(c => char.IsDigit(c) || char.IsLetter(c) || c == '.');
    }

    private static string? FirstNotBlank(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
    }

    private static app_user? ResolveUser(
        string? username,
        string? name,
        Dictionary<string, app_user> byUsername,
        Dictionary<string, app_user> byName)
    {
        if (!string.IsNullOrWhiteSpace(username) && byUsername.TryGetValue(username.Trim(), out var byUser))
            return byUser;

        if (!string.IsNullOrWhiteSpace(name) && byName.TryGetValue(name.Trim(), out var byDisplayName))
            return byDisplayName;

        return null;
    }

    // =============================================================
    // SAVE / ERROR HELPERS
    // =============================================================

    private async Task SaveStageAsync(string stage)
    {
        if (!_context.ChangeTracker.HasChanges())
            return;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[{stage}] {GetDeepestMessage(ex)}", ex);
        }
    }

    private static string GetImportError(Exception ex)
    {
        var current = ex;

        while (current != null)
        {
            if (current is InvalidOperationException && current.Message.StartsWith("[", StringComparison.Ordinal))
                return current.Message;

            current = current.InnerException;
        }

        return GetDeepestMessage(ex);
    }

    private static string GetDeepestMessage(Exception ex)
    {
        var current = ex;
        while (current.InnerException != null)
            current = current.InnerException;
        return current.Message;
    }

    // =============================================================
    // FINANCE
    // =============================================================

    private void AddFinanceInvoice(
        trx_audit audit,
        string spbuNo,
        string currentUser,
        DateTime nowUtc)
    {
        nowUtc = ToUtc(nowUtc);

        var auditDateWithoutTimeZone = audit.audit_execution_time ?? audit.created_date;
        var auditDateUtc = ToUtc(auditDateWithoutTimeZone);

        var invoicePeriodStartUtc = new DateTime(
            auditDateWithoutTimeZone.Year,
            auditDateWithoutTimeZone.Month,
            1,
            0, 0, 0,
            DateTimeKind.Utc);

        var invoicePeriodEndUtc = invoicePeriodStartUtc.AddMonths(1).AddDays(-1);
        var invoiceId = Guid.NewGuid().ToString();
        var invoiceNo = $"IMP-{auditDateWithoutTimeZone:yyyyMM}-{NormalizeForNumber(spbuNo)}-{audit.id[..8]}";

        _context.TrxInvoices.Add(new TrxInvoice
        {
            Id = invoiceId,
            AppUserId = audit.app_user_id,
            InvoicePrefix = "IMP",
            InvoiceNo = invoiceNo,
            InvoicePeriodStart = invoicePeriodStartUtc,
            InvoicePeriodEnd = invoicePeriodEndUtc,
            IssuedDate = auditDateUtc,
            DueDate = auditDateUtc.AddDays(30),
            Status = "IN_PROGRESS",
            CreatedBy = currentUser,
            CreatedDate = nowUtc,
            UpdatedBy = currentUser,
            UpdatedDate = nowUtc
        });

        _context.TrxInvoiceDetails.Add(new TrxInvoiceDetail
        {
            Id = Guid.NewGuid().ToString(),
            TrxInvoiceId = invoiceId,
            TrxAuditId = audit.id,
            AuditFee = 0m,
            LumpsumFee = null,
            Status = "IN_PROGRESS",
            CreatedBy = currentUser,
            CreatedDate = nowUtc,
            UpdatedBy = currentUser,
            UpdatedDate = nowUtc
        });

        _context.TrxClaims.Add(new trx_claim
        {
            id = Guid.NewGuid().ToString(),
            trx_invoice_id = invoiceId,
            app_user_id = audit.app_user_id,
            claim_date = auditDateUtc,
            claim_media_upload = 0,
            claim_media_total = 0,
            status = "UNDER_REVIEW",
            created_by = currentUser,
            created_date = nowUtc,
            updated_by = currentUser,
            updated_date = nowUtc
        });
    }

    // =============================================================
    // CSV PARSER - SUPPORTS QUOTED MULTILINE COMMENTS AND WIDE CHECKLIST/QQ COLUMNS
    // =============================================================

    private static List<RegularAuditImportRow> ReadCsv(TextReader reader)
    {
        var records = ReadCsvRecords(reader);
        if (records.Count == 0)
            return new List<RegularAuditImportRow>();

        var header = records[0]
            .Select(x => (x ?? string.Empty).Trim().TrimStart('\uFEFF'))
            .ToList();

        var result = new List<RegularAuditImportRow>();

        for (var r = 1; r < records.Count; r++)
        {
            var values = records[r];
            if (values.All(string.IsNullOrWhiteSpace))
                continue;

            var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++)
            {
                var key = header[i];
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                raw[key] = i < values.Count ? values[i] : string.Empty;
            }

            string Get(params string[] names)
            {
                foreach (var name in names)
                {
                    var found = raw.FirstOrDefault(x =>
                        NormalizeHeader(x.Key) == NormalizeHeader(name));

                    if (!string.IsNullOrEmpty(found.Key))
                        return found.Value?.Trim() ?? string.Empty;
                }

                return string.Empty;
            }

            bool Has(params string[] names) => names.Any(name =>
                raw.Keys.Any(k => NormalizeHeader(k) == NormalizeHeader(name)));

            var row = new RegularAuditImportRow
            {
                RawValues = raw,

                SendDate = ParseDateTime(Get("send_date")),
                AuditDate = ParseDateTime(Get("Audit Date", "audit_date", "audit_execution_time")),
                AuditScheduleDate = ParseDateTime(Get("audit_schedule_date")),
                AuditCreatedDate = ParseDateTime(Get("audit_created_date")),
                ApprovalDate = ParseDateTime(Get("approval_date")),

                SpbuNo = Get("spbu_no"),
                Region = Get("region"),
                ProvinceName = Get("province_name", "province"),
                Year = ParseInt(Get("year")),
                Address = Get("address"),
                CityName = Get("city_name"),
                TipeSpbu = Get("tipe_spbu", "owner_type"),
                Rayon = Get("rayon", "sbm"),
                Sam = Get("sam"),
                OwnerName = Get("owner_name"),
                ManagerName = Get("manager_name"),
                Quarter = ParseInt(Get("quarter", "quater")),
                Mor = Get("mor"),
                SalesArea = Get("sales_area"),
                PhoneNumber1 = Get("phone_number_1", "phone"),

                AuditType = Get("audit_type"),
                AuditLevel = Get("audit_level"),
                AuditNext = Get("audit_next"),
                GoodStatus = Get("good_status", "result"),
                Result = Get("result"),
                ExcellentStatus = Get("excellent_status"),
                TotalScore = ParseDecimal(Get("Total Score", "total_score")),
                Sss = ParseDecimal(Get("SSS")),
                Eqnq = ParseDecimal(Get("EQnQ")),
                Rfs = ParseDecimal(Get("RFS")),
                Vfc = ParseDecimal(Get("VFC")),
                Epo = ParseDecimal(Get("EPO")),
                Wtms = ParseDecimal(Get("WTMS")),
                Qq = ParseDecimal(Get("QQ")),
                Wmef = ParseDecimal(Get("WMEF")),
                FormatFisik = ParseDecimal(Get("FORMAT FISIK", "format_fisik")),
                Cpo = ParseDecimal(Get("CPO")),
                KelasSpbu = Get("kelas_spbu"),
                PenaltyGoodAlerts = Get("penalty_good_alerts", "penalty_alerts"),
                PenaltyExcellentAlerts = Get("penalty_excellent_alerts"),

                ReportPrefix = Get("report_prefix"),
                ReportNo = Get("report_no"),
                Auditor1Username = Get("auditor1_username"),
                Auditor1Name = Get("auditor1_name"),
                Auditor2Username = Get("auditor2_username"),
                Auditor2Name = Get("auditor2_name"),
                VerifierUsername = Get("verifier_username", "approval_by"),
                VerifierName = Get("verifier_name"),
                AuditMomIntro = Get("audit_mom_intro"),
                AuditMomFinal = Get("audit_mom_final", "berita_acara"),
                KmRange = ParseDecimal(Get("km_range")),

                MasterQuestionerChecklistId = Get("master_questioner_checklist_id"),
                MasterQuestionerVersion = ParseInt(Get("master_questioner_version", "questionnaire_version")),

                HasAuditor1Column = Has("auditor1_username", "auditor1_name"),
                HasAuditor2Column = Has("auditor2_username", "auditor2_name"),
                HasVerifierColumn = Has("verifier_username", "verifier_name", "approval_by"),
                HasAuditMomIntroColumn = Has("audit_mom_intro"),
                HasAuditMomFinalColumn = Has("audit_mom_final", "berita_acara")
            };

            // Checklist scores are ordinary columns such as 1.1.1.a, exactly
            // like the historical Audit_Summary CSV supplied by the business user.
            row.HasDynamicChecklistColumns = raw.Keys.Any(LooksLikeChecklistColumn);
            row.HasQqWideColumns = raw.Keys.Any(k => TryGetQqColumnIndex(k, out _));
            row.FixedNormalizedHeaders = BuildFixedHeaderSet();

            result.Add(row);
        }

        return result;
    }

    private static List<List<string>> ReadCsvRecords(TextReader reader)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        while (true)
        {
            var read = reader.Read();
            if (read < 0)
                break;

            var c = (char)read;

            if (c == '"')
            {
                if (inQuotes && reader.Peek() == '"')
                {
                    reader.Read();
                    field.Append('"');
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                record.Add(field.ToString());
                field.Clear();
                continue;
            }

            if ((c == '\r' || c == '\n') && !inQuotes)
            {
                if (c == '\r' && reader.Peek() == '\n')
                    reader.Read();

                record.Add(field.ToString());
                field.Clear();

                if (record.Any(x => !string.IsNullOrWhiteSpace(x)))
                    records.Add(record);

                record = new List<string>();
                continue;
            }

            field.Append(c);
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            if (record.Any(x => !string.IsNullOrWhiteSpace(x)))
                records.Add(record);
        }

        if (inQuotes)
            throw new InvalidOperationException("CSV tidak valid: ada quoted field yang belum ditutup.");

        return records;
    }

    private static HashSet<string> BuildFixedHeaderSet()
    {
        var headers = new[]
        {
            "send_date","Audit Date","audit_date","audit_execution_time","audit_schedule_date","audit_created_date","approval_date",
            "spbu_no","region","province_name","province","year","address","city_name","tipe_spbu","owner_type","rayon","sbm","sam",
            "owner_name","manager_name","quarter","quater","mor","sales_area","phone_number_1","phone",
            "audit_type","audit_level","audit_next","good_status","result","excellent_status","Total Score","total_score",
            "SSS","EQnQ","RFS","VFC","EPO","WTMS","QQ","WMEF","FORMAT FISIK","format_fisik","CPO","kelas_spbu",
            "penalty_good_alerts","penalty_alerts","penalty_excellent_alerts","report_prefix","report_no",
            "auditor1_username","auditor1_name","auditor2_username","auditor2_name","verifier_username","approval_by","verifier_name",
            "audit_mom_intro","audit_mom_final","berita_acara","km_range",
            "master_questioner_checklist_id","master_questioner_version","questionnaire_version"
        };

        return headers.Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // =============================================================
    // BASIC PARSE / FORMAT HELPERS
    // =============================================================

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim().Replace(" ", string.Empty);

        // CSV historical can contain 94,11 or 94.11. Treat a single separator
        // as the decimal separator, not as a thousands separator.
        if (text.Contains(',') && !text.Contains('.'))
            text = text.Replace(',', '.');
        else if (text.Contains(',') && text.Contains('.'))
        {
            // Whichever separator appears last is considered decimal separator.
            if (text.LastIndexOf(',') > text.LastIndexOf('.'))
                text = text.Replace(".", string.Empty).Replace(',', '.');
            else
                text = text.Replace(",", string.Empty);
        }

        return decimal.TryParse(
            text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var number)
                ? number
                : null;
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "dd/MM/yyyy",
            "dd/MM/yyyy HH:mm:ss",
            "dd-MM-yyyy",
            "yyyy/MM/dd"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(
                value.Trim(),
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
            {
                return date;
            }
        }

        if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        return null;
    }

    private static DateTime AsUnspecified(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var number) ? number : null;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequiredFallback(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string NormalizeHeader(string? value) =>
        (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();

    private static string NormalizeForNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "SPBU";

        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string GenerateReportNo(RegularAuditImportRow row)
    {
        var date = row.AuditDate ?? DateTime.Now;
        return $"IMP-{date:yyyyMMdd}-{NormalizeForNumber(row.SpbuNo)}";
    }

    private static void SetIfNotBlank(string? value, Action<string> setter)
    {
        if (!string.IsNullOrWhiteSpace(value))
            setter(value.Trim());
    }

    // =============================================================
    // PRIVATE TYPES
    // =============================================================

    private sealed class AuditImportContext
    {
        public RegularAuditImportRow Row { get; set; } = null!;
        public trx_audit Audit { get; set; } = null!;
        public string SpbuNo { get; set; } = string.Empty;
    }

    private sealed class QuestionMap
    {
        public List<QuestionMapItem> Items { get; }
        private readonly Dictionary<string, QuestionMapItem> _byId;
        private readonly Dictionary<string, QuestionMapItem> _byNumber;

        public QuestionMap(List<QuestionMapItem> items)
        {
            Items = items;
            _byId = items
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            _byNumber = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Number))
                .GroupBy(x => x.Number!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        }

        public QuestionMapItem? Resolve(string? id, string? number)
        {
            if (!string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id.Trim(), out var byId))
                return byId;

            if (!string.IsNullOrWhiteSpace(number) && _byNumber.TryGetValue(number.Trim(), out var byNumber))
                return byNumber;

            return null;
        }
    }

    private sealed class QuestionMapItem
    {
        public string Id { get; set; } = string.Empty;
        public string MasterQuestionerId { get; set; } = string.Empty;
        public string? Number { get; set; }
        public bool IsRelaksasi { get; set; }
    }

    private sealed class ResolvedChecklistItem
    {
        public QuestionMapItem Question { get; set; } = null!;
        public string? ScoreInput { get; set; }
        public decimal? ScoreAf { get; set; }
        public decimal? ScoreX { get; set; }
        public string? Comment { get; set; }
    }

    private sealed class QqCsvPayloadItem
    {
        public string? NozzleNumber { get; set; }
        public string? DuMake { get; set; }
        public string? DuSerialNo { get; set; }
        public string? Product { get; set; }
        public string? Mode { get; set; }
        public decimal? QuantityVariationWithMeasure { get; set; }
        public decimal? QuantityVariationInPercentage { get; set; }
        public decimal? ObservedDensity { get; set; }
        public decimal? ObservedTemp { get; set; }
        public decimal? ObservedDensity15Degree { get; set; }
        public decimal? ReferenceDensity15Degree { get; set; }
        public string? TankNumber { get; set; }
        public decimal? DensityVariation { get; set; }
    }

    private sealed class RegularAuditImportRow
    {
        public Dictionary<string, string> RawValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FixedNormalizedHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public DateTime? SendDate { get; set; }
        public DateTime? AuditDate { get; set; }
        public DateTime? AuditScheduleDate { get; set; }
        public DateTime? AuditCreatedDate { get; set; }
        public DateTime? ApprovalDate { get; set; }

        public string SpbuNo { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string ProvinceName { get; set; } = string.Empty;
        public int? Year { get; set; }
        public string Address { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;
        public string TipeSpbu { get; set; } = string.Empty;
        public string Rayon { get; set; } = string.Empty;
        public string Sam { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public string ManagerName { get; set; } = string.Empty;
        public int? Quarter { get; set; }
        public string Mor { get; set; } = string.Empty;
        public string SalesArea { get; set; } = string.Empty;
        public string PhoneNumber1 { get; set; } = string.Empty;

        public string AuditType { get; set; } = string.Empty;
        public string AuditLevel { get; set; } = string.Empty;
        public string AuditNext { get; set; } = string.Empty;
        public string GoodStatus { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string ExcellentStatus { get; set; } = string.Empty;

        public decimal? TotalScore { get; set; }
        public decimal? Sss { get; set; }
        public decimal? Eqnq { get; set; }
        public decimal? Rfs { get; set; }
        public decimal? Vfc { get; set; }
        public decimal? Epo { get; set; }
        public decimal? Wtms { get; set; }
        public decimal? Qq { get; set; }
        public decimal? Wmef { get; set; }
        public decimal? FormatFisik { get; set; }
        public decimal? Cpo { get; set; }
        public decimal? KmRange { get; set; }

        public string KelasSpbu { get; set; } = string.Empty;
        public string PenaltyGoodAlerts { get; set; } = string.Empty;
        public string PenaltyExcellentAlerts { get; set; } = string.Empty;

        public string ReportPrefix { get; set; } = string.Empty;
        public string ReportNo { get; set; } = string.Empty;
        public string Auditor1Username { get; set; } = string.Empty;
        public string Auditor1Name { get; set; } = string.Empty;
        public string Auditor2Username { get; set; } = string.Empty;
        public string Auditor2Name { get; set; } = string.Empty;
        public string VerifierUsername { get; set; } = string.Empty;
        public string VerifierName { get; set; } = string.Empty;
        public string AuditMomIntro { get; set; } = string.Empty;
        public string AuditMomFinal { get; set; } = string.Empty;

        public string MasterQuestionerChecklistId { get; set; } = string.Empty;
        public int? MasterQuestionerVersion { get; set; }
        public string? ResolvedMasterQuestionerChecklistId { get; set; }

        public bool HasDynamicChecklistColumns { get; set; }
        public bool HasQqWideColumns { get; set; }
        public bool HasAuditor1Column { get; set; }
        public bool HasAuditor2Column { get; set; }
        public bool HasVerifierColumn { get; set; }
        public bool HasAuditMomIntroColumn { get; set; }
        public bool HasAuditMomFinalColumn { get; set; }

        public bool HasChecklistPayload => HasDynamicChecklistColumns;

        public string GetRaw(string header)
        {
            if (RawValues.TryGetValue(header, out var exact))
                return exact?.Trim() ?? string.Empty;

            var normalized = NormalizeHeader(header);
            foreach (var pair in RawValues)
            {
                if (NormalizeHeader(pair.Key) == normalized)
                    return pair.Value?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
