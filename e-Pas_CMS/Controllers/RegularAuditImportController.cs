using System.Globalization;
using System.Text;
using e_Pas_CMS.Data;
using e_Pas_CMS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "File CSV belum dipilih.";
            return RedirectToAction("Index", "AuditReport");
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Template Regular Audit harus berupa file CSV.";
            return RedirectToAction("Index", "AuditReport");
        }

        var currentUser = User.Identity?.Name ?? "SYSTEM";
        var now = DateTime.Now;

        await using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            List<RegularAuditImportRow> rows;

            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true))
            {
                rows = ReadCsv(reader);
            }

            if (rows.Count == 0)
                throw new InvalidOperationException("CSV tidak memiliki data.");

            var success = 0;
            var skipped = 0;
            var updated = 0;

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.SpbuNo) || !row.AuditDate.HasValue)
                {
                    skipped++;
                    continue;
                }

                var spbuNo = row.SpbuNo.Trim();
                var auditDate = DateTime.SpecifyKind(row.AuditDate.Value.Date, DateTimeKind.Unspecified);
                var sendDate = row.SendDate.HasValue
                    ? DateTime.SpecifyKind(row.SendDate.Value.Date, DateTimeKind.Unspecified)
                    : auditDate;

                // =========================================================
                // 1. MASTER SPBU
                // =========================================================
                var spbu = await _context.spbus.FirstOrDefaultAsync(x => x.spbu_no == spbuNo);

                if (spbu == null)
                {
                    spbu = new spbu
                    {
                        id = Guid.NewGuid().ToString(),
                        spbu_no = spbuNo,
                        region = RequiredFallback(row.Region),
                        province_name = "-",
                        city_name = RequiredFallback(row.CityName),
                        address = NullIfEmpty(row.Address),
                        type = NullIfEmpty(row.TipeSpbu),
                        sbm = NullIfEmpty(row.Rayon),
                        year = row.Year,
                        audit_next = NullIfEmpty(row.AuditNext),
                        status_good = NullIfEmpty(row.GoodStatus),
                        status_excellent = NullIfEmpty(row.ExcellentStatus),
                        audit_current_score = row.TotalScore,
                        audit_current_time = auditDate,
                        status = "ACTIVE",
                        created_by = currentUser,
                        created_date = now,
                        updated_by = currentUser,
                        updated_date = now,

                        // These columns are non-null on the existing SPBU table.
                        // They are master SPBU technical defaults, NOT imported audit answers.
                        wtms = 0,
                        qq = 0,
                        wmef = 0,
                        format_fisik = 0,
                        cpo = 0
                    };

                    _context.spbus.Add(spbu);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(row.Region)) spbu.region = row.Region.Trim();
                    if (!string.IsNullOrWhiteSpace(row.CityName)) spbu.city_name = row.CityName.Trim();
                    if (!string.IsNullOrWhiteSpace(row.Address)) spbu.address = row.Address.Trim();
                    if (!string.IsNullOrWhiteSpace(row.TipeSpbu)) spbu.type = row.TipeSpbu.Trim();
                    if (!string.IsNullOrWhiteSpace(row.Rayon)) spbu.sbm = row.Rayon.Trim();
                    if (row.Year.HasValue) spbu.year = row.Year;

                    spbu.audit_next = NullIfEmpty(row.AuditNext);
                    spbu.status_good = NullIfEmpty(row.GoodStatus);
                    spbu.status_excellent = NullIfEmpty(row.ExcellentStatus);
                    spbu.audit_current_score = row.TotalScore;
                    spbu.audit_current_time = auditDate;
                    spbu.updated_by = currentUser;
                    spbu.updated_date = now;
                }

                await _context.SaveChangesAsync();

                // =========================================================
                // 2. AUDIT
                // Dedupe by SPBU + audit date + audit level + Regular Audit.
                // =========================================================
                var nextDate = auditDate.AddDays(1);
                var normalizedAuditLevel = string.IsNullOrWhiteSpace(row.AuditLevel)
                    ? "-"
                    : row.AuditLevel.Trim();

                var audit = await _context.trx_audits.FirstOrDefaultAsync(x =>
                    x.spbu_id == spbu.id &&
                    x.audit_type == "Regular Audit" &&
                    x.audit_execution_time >= auditDate &&
                    x.audit_execution_time < nextDate &&
                    x.audit_level == normalizedAuditLevel);

                var isNewAudit = audit == null;

                if (audit == null)
                {
                    audit = new trx_audit
                    {
                        id = Guid.NewGuid().ToString(),
                        report_prefix = "IMP",
                        report_no = GenerateReportNo(row),
                        spbu_id = spbu.id,

                        // Template does not contain auditor/user id.
                        app_user_id = null,
                        app_user_id_auditor2 = null,

                        audit_level = normalizedAuditLevel,
                        audit_type = "Regular Audit",
                        score = row.TotalScore,
                        audit_schedule_date = DateOnly.FromDateTime(auditDate),
                        audit_execution_time = auditDate,
                        status = "VERIFIED",
                        form_type_auditor1 = "FULL",
                        form_status_auditor1 = "COMPLETED",
                        good_status = NullIfEmpty(row.GoodStatus),
                        excellent_status = NullIfEmpty(row.ExcellentStatus),
                        created_by = currentUser,
                        created_date = sendDate,
                        updated_by = currentUser,
                        updated_date = now,
                        approval_by = currentUser,
                        approval_date = sendDate,
                        km_range = 0
                    };

                    _context.trx_audits.Add(audit);
                }
                else
                {
                    audit.score = row.TotalScore;
                    audit.good_status = NullIfEmpty(row.GoodStatus);
                    audit.excellent_status = NullIfEmpty(row.ExcellentStatus);
                    audit.status = "VERIFIED";
                    audit.approval_date = sendDate;
                    audit.updated_by = currentUser;
                    audit.updated_date = now;
                    updated++;
                }

                await _context.SaveChangesAsync();

                // =========================================================
                // 3. IMPORTED REPORT SUMMARY
                // Store exactly what exists in CSV. Missing optional fields stay NULL.
                // =========================================================
                var summary = await _context.trx_audit_import_summaries
                    .FirstOrDefaultAsync(x => x.trx_audit_id == audit.id);

                if (summary == null)
                {
                    summary = new trx_audit_import_summary
                    {
                        id = Guid.NewGuid().ToString(),
                        trx_audit_id = audit.id,
                        created_by = currentUser,
                        created_date = now
                    };

                    _context.trx_audit_import_summaries.Add(summary);
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

                await _context.SaveChangesAsync();

                // =========================================================
                // 4. FINANCE INVOICE
                // Existing Finance Invoice Index only displays:
                // invoice=IN_PROGRESS, detail=IN_PROGRESS, claim=UNDER_REVIEW.
                // =========================================================
                await EnsureFinanceInvoiceAsync(audit, spbuNo, currentUser, now);

                // IMPORTANT:
                // No trx_audit_qq / trx_audit_checklist / trx_audit_medium rows are
                // inserted here. If the CSV does not contain QQ/checklist/media data,
                // those detail tables remain empty as requested.

                if (isNewAudit)
                    success++;
            }

            await _context.SaveChangesAsync();
            await dbTransaction.CommitAsync();

            TempData["Success"] =
                $"Upload Regular Audit berhasil. New={success}, Updated={updated}, Skipped={skipped}.";

            return RedirectToAction("Index", "AuditReport");
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();

            _logger.LogError(ex, "Regular Audit CSV import failed for {FileName}", file.FileName);

            TempData["Error"] = $"Upload Regular Audit gagal: {ex.Message}";
            return RedirectToAction("Index", "AuditReport");
        }
    }

    private async Task EnsureFinanceInvoiceAsync(
        trx_audit audit,
        string spbuNo,
        string currentUser,
        DateTime now)
    {
        var detailExists = await _context.TrxInvoiceDetails
            .AnyAsync(x => x.TrxAuditId == audit.id);

        if (detailExists)
            return;

        var auditDate = audit.audit_execution_time ?? audit.created_date;
        var invoicePeriodStart = new DateTime(auditDate.Year, auditDate.Month, 1);
        var invoicePeriodEnd = invoicePeriodStart.AddMonths(1).AddDays(-1);

        var invoiceId = Guid.NewGuid().ToString();
        var invoiceNo =
            $"IMP-{auditDate:yyyyMM}-{NormalizeForNumber(spbuNo)}-{audit.id[..8]}";

        var invoice = new TrxInvoice
        {
            Id = invoiceId,
            AppUserId = null,
            InvoicePrefix = "IMP",
            InvoiceNo = invoiceNo,
            InvoicePeriodStart = invoicePeriodStart,
            InvoicePeriodEnd = invoicePeriodEnd,
            IssuedDate = auditDate,
            DueDate = auditDate.AddDays(30),
            Status = "IN_PROGRESS",
            CreatedBy = currentUser,
            CreatedDate = now,
            UpdatedBy = currentUser,
            UpdatedDate = now
        };

        var detail = new TrxInvoiceDetail
        {
            Id = Guid.NewGuid().ToString(),
            TrxInvoiceId = invoiceId,
            TrxAuditId = audit.id,

            // Template has no finance fee column. Do not fabricate an amount.
            AuditFee = 0m,
            LumpsumFee = null,

            Status = "IN_PROGRESS",
            CreatedBy = currentUser,
            CreatedDate = now,
            UpdatedBy = currentUser,
            UpdatedDate = now
        };

        var claimDateUtc = DateTime.SpecifyKind(auditDate, DateTimeKind.Utc);

        var claim = new trx_claim
        {
            id = Guid.NewGuid().ToString(),
            trx_invoice_id = invoiceId,
            app_user_id = null,
            claim_date = claimDateUtc,
            claim_media_upload = 0,
            claim_media_total = 0,
            status = "UNDER_REVIEW",
            created_by = currentUser,
            created_date = now,
            updated_by = currentUser,
            updated_date = now
        };

        _context.TrxInvoices.Add(invoice);
        _context.TrxInvoiceDetails.Add(detail);
        _context.TrxClaims.Add(claim);
    }

    private static List<RegularAuditImportRow> ReadCsv(TextReader reader)
    {
        var result = new List<RegularAuditImportRow>();

        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            return result;

        var headerValues = ParseCsvLine(headerLine);
        var headers = headerValues
            .Select((name, index) => new { Name = NormalizeHeader(name), Index = index })
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Index, StringComparer.OrdinalIgnoreCase);

        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = ParseCsvLine(line);

            string Get(params string[] names)
            {
                foreach (var name in names)
                {
                    var key = NormalizeHeader(name);

                    if (headers.TryGetValue(key, out var index) && index < values.Count)
                        return values[index].Trim();
                }

                return "";
            }

            result.Add(new RegularAuditImportRow
            {
                SendDate = ParseDate(Get("send_date")),
                AuditDate = ParseDate(Get("Audit Date", "audit_date")),
                SpbuNo = Get("spbu_no"),
                Region = Get("region"),
                Year = ParseInt(Get("year")),
                Address = Get("address"),
                CityName = Get("city_name"),
                TipeSpbu = Get("tipe_spbu"),
                Rayon = Get("rayon"),
                AuditLevel = Get("audit_level"),
                AuditNext = Get("audit_next"),
                GoodStatus = Get("good_status"),
                ExcellentStatus = Get("excellent_status"),
                TotalScore = ParseDecimal(Get("Total Score", "total_score")),
                Sss = ParseDecimal(Get("SSS")),
                Eqnq = ParseDecimal(Get("EQnQ")),
                Rfs = ParseDecimal(Get("RFS")),
                Vfc = ParseDecimal(Get("VFC")),
                Epo = ParseDecimal(Get("EPO")),

                // Optional future-compatible columns. Current Mar_2026 template does
                // not contain these, therefore values become NULL.
                Wtms = ParseDecimal(Get("WTMS")),
                Qq = ParseDecimal(Get("QQ")),
                Wmef = ParseDecimal(Get("WMEF")),
                FormatFisik = ParseDecimal(Get("FORMAT FISIK", "format_fisik")),
                Cpo = ParseDecimal(Get("CPO")),

                KelasSpbu = Get("kelas_spbu"),
                PenaltyGoodAlerts = Get("penalty_good_alerts"),
                PenaltyExcellentAlerts = Get("penalty_excellent_alerts")
            });
        }

        return result;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var buffer = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    buffer.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                values.Add(buffer.ToString());
                buffer.Clear();
                continue;
            }

            buffer.Append(c);
        }

        values.Add(buffer.ToString());
        return values;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        // Template uses decimal comma ("94,11"). Try Indonesian culture first.
        if (decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.GetCultureInfo("id-ID"),
            out var idValue))
        {
            return idValue;
        }

        if (decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var invariantValue))
        {
            return invariantValue;
        }

        return null;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var formats = new[]
        {
            "yyyy-MM-dd",
            "dd/MM/yyyy",
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

        return null;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var number) ? number : null;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequiredFallback(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string NormalizeHeader(string? value) =>
        (value ?? "")
            .Trim()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "")
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

    private sealed class RegularAuditImportRow
    {
        public DateTime? SendDate { get; set; }
        public DateTime? AuditDate { get; set; }

        public string SpbuNo { get; set; } = "";
        public string Region { get; set; } = "";
        public int? Year { get; set; }
        public string Address { get; set; } = "";
        public string CityName { get; set; } = "";
        public string TipeSpbu { get; set; } = "";
        public string Rayon { get; set; } = "";
        public string AuditLevel { get; set; } = "";
        public string AuditNext { get; set; } = "";
        public string GoodStatus { get; set; } = "";
        public string ExcellentStatus { get; set; } = "";

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

        public string KelasSpbu { get; set; } = "";
        public string PenaltyGoodAlerts { get; set; } = "";
        public string PenaltyExcellentAlerts { get; set; } = "";
    }
}
