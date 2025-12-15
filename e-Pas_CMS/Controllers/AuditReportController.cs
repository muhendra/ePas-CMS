using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using QuestPDF.Fluent;
using Newtonsoft.Json;
using System.Xml.Linq;
using Npgsql;
using System.Reflection.Emit;
using e_Pas_CMS.Models;
using System.Text;
using static System.Formats.Asn1.AsnWriter;
using Microsoft.Extensions.Configuration;
using System.Data.Common;
using System.Globalization;

namespace e_Pas_CMS.Controllers
{
    [Authorize]
    public class AuditReportController : Controller
    {
        private readonly EpasDbContext _context;
        private readonly ILogger<AuditReportController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public AuditReportController(EpasDbContext context, ILogger<AuditReportController> logger, IConfiguration configuration, IWebHostEnvironment env)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _env = env;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, string searchTerm = "", int? filterMonth = null, int? filterYear = null)
        {
            var currentUser = User.Identity?.Name;

            var userRegion = await (from aur in _context.app_user_roles
                                    join au in _context.app_users on aur.app_user_id equals au.id
                                    where au.username == currentUser
                                    select aur.region)
                       .Distinct()
                       .Where(r => r != null)
                       .ToListAsync();

            var userSbm = await (from aur in _context.app_user_roles
                                 join au in _context.app_users on aur.app_user_id equals au.id
                                 where au.username == currentUser
                                 select aur.sbm)
                    .Where(s => s != null)
                    .Distinct()
                    .ToListAsync();

            var query = _context.trx_audits
                .Include(a => a.spbu)
                .Include(a => a.app_user)
                .Where(a => a.status == "VERIFIED" && a.audit_type != "Basic Operational");

            if (userRegion.Any() || userSbm.Any())
            {
                query = query.Where(x =>
                    (x.spbu.region != null && userRegion.Contains(x.spbu.region)) ||
                    (x.spbu.sbm != null && userSbm.Contains(x.spbu.sbm))
                );
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.ToLower();
                query = query.Where(a =>
                    a.spbu.spbu_no.ToLower().Contains(searchTerm) ||
                    a.app_user.name.ToLower().Contains(searchTerm) ||
                    a.status.ToLower().Contains(searchTerm) ||
                    a.spbu.address.ToLower().Contains(searchTerm) ||
                    a.spbu.province_name.ToLower().Contains(searchTerm) ||
                    a.spbu.city_name.ToLower().Contains(searchTerm)
                ).OrderByDescending(a => a.audit_execution_time);
            }

            if (filterMonth.HasValue && filterYear.HasValue)
            {
                query = query.Where(a =>
                ((a.audit_execution_time != null ? a.audit_execution_time.Value.Month : a.created_date.Month) == filterMonth.Value) &&
                ((a.audit_execution_time != null ? a.audit_execution_time.Value.Year : a.created_date.Year) == filterYear.Value));
            }

            ViewBag.FilterMonth = filterMonth;
            ViewBag.FilterYear = filterYear;

            query = query.OrderByDescending(a => a.audit_execution_time)
                         .ThenByDescending(a => a.updated_date);

            var totalItems = await query.CountAsync();

            var pagedAudits = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var result = new List<AuditReportListViewModel>();

            foreach (var a in pagedAudits)
            {
                var sql = @"
                SELECT 
                    mqd.weight, 
                    tac.score_input, 
                    tac.score_x, 
                    mqd.is_relaksasi
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac 
                    ON tac.master_questioner_detail_id = mqd.id 
                    AND tac.trx_audit_id = @id
                WHERE mqd.master_questioner_id = (
                    SELECT master_questioner_checklist_id 
                    FROM trx_audit 
                    WHERE id = @id
                )
                AND mqd.type = 'QUESTION'";

                var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(sql, new { id = a.id }))
                    .ToList();

                decimal sumAF = 0, sumWeight = 0, sumX = 0;

                foreach (var item in checklist)
                {
                    decimal w = item.weight ?? 0;
                    string input = item.score_input?.Trim().ToUpperInvariant() ?? "";

                    if (input == "X")
                    {
                        sumX += w;
                        sumAF += item.score_x ?? 0;
                    }
                    else if (input == "F" && item.is_relaksasi == true)
                    {
                        sumAF += 1.00m * w;
                    }
                    else
                    {
                        decimal af = input switch
                        {
                            "A" => 1.00m,
                            "B" => 0.80m,
                            "C" => 0.60m,
                            "D" => 0.40m,
                            "E" => 0.20m,
                            "F" => 0.00m,
                            _ => 0.00m
                        };
                        sumAF += af * w;
                    }

                    sumWeight += w;
                }

                decimal finalScore = (sumWeight - sumX) > 0
                    ? (sumAF / (sumWeight - sumX)) * sumWeight
                    : 0m;

                // === Special Node Score Check ===
                var specialNodeIds = new List<Guid>
                {
                    Guid.Parse("555fe2e4-b95b-461b-9c92-ad8b5c837119"),
                    Guid.Parse("bafc206f-ed29-4bbc-8053-38799e186fb0"),
                    Guid.Parse("d26f4caa-e849-4ab4-9372-298693247272")
                };

                if (a.created_date > new DateTime(2025, 5, 31))
                {
                    specialNodeIds.Add(Guid.Parse("5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b"));
                }

                var specialScoreSql = @"
                SELECT mqd.id, tac.score_input, ta.created_date
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac 
                    ON tac.master_questioner_detail_id = mqd.id 
                    AND tac.trx_audit_id = @id
                LEFT JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE mqd.id = ANY(@ids);";

                var specialScoresRaw = (await conn.QueryAsync<(string id, string score_input, DateTime? created_date)>(
                    specialScoreSql,
                    new { id = a.id, ids = specialNodeIds.Select(x => x.ToString()).ToArray() }
                )).ToList();

                var specialScores = specialScoresRaw
                    .Where(x =>
                        x.id != "5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b" ||
                        (x.created_date != null && x.created_date.Value < new DateTime(2025, 6, 1))
                    )
                    .ToDictionary(x => x.id, x => x.score_input?.Trim().ToUpperInvariant());

                bool forceGoodOnly = false;
                bool forceNotCertified = false;

                foreach (var score in specialScores.Values)
                {
                    if (score == "C")
                        forceGoodOnly = true;
                    else if (score != "A")
                        forceNotCertified = true;
                }

                // === Penalty Check
                var penaltyExcellentQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));";

                var penaltyGoodQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
            FROM trx_audit_checklist tac
            INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
            WHERE tac.trx_audit_id = @id AND
              tac.score_input = 'F' AND
              mqd.is_penalty = true AND 
              (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';";

                var penaltyExcellentResult = await conn.ExecuteScalarAsync<string>(penaltyExcellentQuery, new { id = a.id });
                var penaltyGoodResult = await conn.ExecuteScalarAsync<string>(penaltyGoodQuery, new { id = a.id });

                bool hasExcellentPenalty = !string.IsNullOrEmpty(penaltyExcellentResult);
                bool hasGoodPenalty = !string.IsNullOrEmpty(penaltyGoodResult);

                //string goodStatus = (finalScore >= 75 && !hasGoodPenalty) ? "CERTIFIED" : "NOT CERTIFIED";

                //string excellentStatus = (finalScore >= 80 && !hasExcellentPenalty && !forceNotCertified)
                //    ? (forceGoodOnly ? "GOOD" : "CERTIFIED")
                //    : "NOT CERTIFIED";

                // === Audit Next
                string auditNext = "";
                string levelspbu = null;

                var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = a.audit_level });


                // === Hitung Compliance
                var checklistData = await GetChecklistDataAsync(conn, a.id);
                var mediaList = await GetMediaPerNodeAsync(conn, a.id);
                var elements = BuildHierarchy(checklistData, mediaList);
                foreach (var element in elements) AssignWeightRecursive(element);
                CalculateChecklistScores(elements);
                CalculateOverallScore(new DetailReportViewModel { Elements = elements }, checklistData);
                var modelstotal = new DetailReportViewModel { Elements = elements };
                CalculateOverallScore(modelstotal, checklistData);
                decimal? totalScore = modelstotal.TotalScore;
                var compliance = HitungComplianceLevelDariElements(elements);

                // === Compliance validation
                var sss = Math.Round(compliance.SSS ?? 0, 2);
                var eqnq = Math.Round(compliance.EQnQ ?? 0, 2);
                var rfs = Math.Round(compliance.RFS ?? 0, 2);
                var vfc = Math.Round(compliance.VFC ?? 0, 2);
                var epo = Math.Round(compliance.EPO ?? 0, 2);

                bool failGood = sss < 80 || eqnq < 85 || rfs < 85 || vfc < 15 || epo < 25;
                bool failExcellent = sss < 85 || eqnq < 85 || rfs < 85 || vfc < 20 || epo < 50;

                // === Update status with compliance logic
                string goodStatus = (finalScore >= 75 && !hasGoodPenalty && !failGood)
                    ? "CERTIFIED"
                    : "NOT CERTIFIED";

                string excellentStatus = (finalScore >= 80 && !hasExcellentPenalty && !failExcellent && !forceNotCertified)
                    ? (forceGoodOnly ? "GOOD" : "CERTIFIED")
                    : "NOT CERTIFIED";

                decimal scoress = Math.Round((decimal)totalScore, 2);


                if (auditFlow != null)
                {
                    string passedGood = auditFlow.passed_good;
                    string passedExcellent = auditFlow.passed_excellent;
                    string passedAuditLevel = auditFlow.passed_audit_level;
                    string failed_audit_level = auditFlow.failed_audit_level;

                    if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                    {
                        auditNext = passedAuditLevel;
                    }
                    else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                    {
                        auditNext = passedAuditLevel;
                    }
                    else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                    {
                        auditNext = failed_audit_level;
                    }
                    else if (goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                    {
                        auditNext = failed_audit_level;
                    }
                    else if (goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                    {
                        auditNext = passedGood;
                    }
                    else if (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                    {
                        auditNext = passedExcellent;
                    }
                    else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && finalScore >= 75)
                    {
                        auditNext = passedAuditLevel;
                    }
                    else
                    {
                        auditNext = failed_audit_level;
                    }

                    var auditlevelClassSql = @"SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                    var auditlevelClass = await conn.QueryFirstOrDefaultAsync<dynamic>(auditlevelClassSql, new { level = auditNext });
                    levelspbu = auditlevelClass != null
                    ? (auditlevelClass.audit_level_class ?? "")
                    : "";
                }

                result.Add(new AuditReportListViewModel
                {
                    TrxAuditId = a.id,
                    ReportNo = a.report_no,
                    SpbuNo = a.spbu.spbu_no,
                    Region = a.spbu.region,
                    Address = a.spbu.address,
                    City = a.spbu.city_name,
                    SBM = a.spbu.sbm,
                    SAM = a.spbu.sam,
                    Province = a.spbu.province_name,
                    Year = a.spbu.year ?? DateTime.Now.Year,
                    AuditDate = (a.audit_execution_time == null || a.audit_execution_time.Value == DateTime.MinValue) ? a.updated_date.Value : a.audit_execution_time.Value,
                    SubmitDate = a.approval_date.GetValueOrDefault() == DateTime.MinValue ? a.updated_date : a.approval_date.GetValueOrDefault(),
                    Auditor = a.app_user.name,
                    GoodStatus = goodStatus,
                    ExcellentStatus = excellentStatus,
                    //Score = (a.score ?? a.spbu.audit_current_score ?? (decimal?)finalScore).Value,
                    Score = totalScore ?? a.score,
                    WTMS = a.spbu.wtms,
                    QQ = a.spbu.qq,
                    WMEF = a.spbu.wmef,
                    FormatFisik = a.spbu.format_fisik,
                    CPO = a.spbu.cpo,
                    KelasSpbu = levelspbu,
                    Auditlevel = a.audit_level,
                    AuditNext = auditNext,
                    ApproveDate = a.approval_date ?? DateTime.Now,
                    ApproveBy = string.IsNullOrWhiteSpace(a.approval_by) ? "-" : a.approval_by,
                    SSS = Math.Round(compliance.SSS ?? 0, 2),
                    EQnQ = Math.Round(compliance.EQnQ ?? 0, 2),
                    RFS = Math.Round(compliance.RFS ?? 0, 2),
                    VFC = Math.Round(compliance.VFC ?? 0, 2),
                    EPO = Math.Round(compliance.EPO ?? 0, 2)
                });
            }

            var model = new PaginationModel<AuditReportListViewModel>
            {
                Items = result,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            ViewBag.SearchTerm = searchTerm;
            return View(model);
        }

        public async Task<IActionResult> IndexSpbu(int pageNumber = 1, int pageSize = 50, string searchTerm = "", int? filterMonth = null, int? filterYear = null)
        {
            var currentUser = User.Identity?.Name;

            var userRegion = await (from aur in _context.app_user_roles
                                    join au in _context.app_users on aur.app_user_id equals au.id
                                    where au.username == currentUser
                                    select aur.region)
                       .Distinct()
                       .Where(r => r != null)
                       .ToListAsync();

            var userSbm = await (from aur in _context.app_user_roles
                                 join au in _context.app_users on aur.app_user_id equals au.id
                                 where au.username == currentUser
                                 select aur.sbm)
                    .Where(s => s != null)
                    .Distinct()
                    .ToListAsync();

            var query = _context.trx_audits
                .FromSqlInterpolated($@"
            SELECT DISTINCT ON (spbu_id) *
            FROM trx_audit
            WHERE status = 'VERIFIED' AND audit_type <> 'Basic Operational'
            ORDER BY spbu_id, audit_execution_time DESC, audit_schedule_date DESC, created_date DESC
        ")
                .Include(a => a.spbu)
                .Include(a => a.app_user)
                .AsNoTracking();

            if (userRegion.Any() || userSbm.Any())
            {
                query = query.Where(x =>
                    (x.spbu.region != null && userRegion.Contains(x.spbu.region)) ||
                    (x.spbu.sbm != null && userSbm.Contains(x.spbu.sbm))
                );
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.ToLower();
                query = query.Where(a =>
                    a.spbu.spbu_no.ToLower().Contains(searchTerm) ||
                    a.app_user.name.ToLower().Contains(searchTerm) ||
                    a.status.ToLower().Contains(searchTerm) ||
                    a.spbu.address.ToLower().Contains(searchTerm) ||
                    a.spbu.province_name.ToLower().Contains(searchTerm) ||
                    a.spbu.city_name.ToLower().Contains(searchTerm)
                );
            }

            if (filterMonth.HasValue && filterYear.HasValue)
            {
                query = query.Where(a =>
                    ((a.audit_execution_time != null ? a.audit_execution_time.Value.Month : a.created_date.Month) == filterMonth.Value) &&
                    ((a.audit_execution_time != null ? a.audit_execution_time.Value.Year : a.created_date.Year) == filterYear.Value));
            }

            ViewBag.FilterMonth = filterMonth;
            ViewBag.FilterYear = filterYear;

            query = query.OrderByDescending(a => a.audit_execution_time)
                         .ThenByDescending(a => a.id);

            var totalItems = await query.CountAsync();

            var pagedAudits = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var result = new List<AuditReportListViewModel>();

            // === Transaksi untuk batch update audit_next ===
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                foreach (var a in pagedAudits)
                {
                    // --- Ambil checklist untuk hitung finalScore ---
                    var sql = @"
            SELECT 
                mqd.weight, 
                tac.score_input, 
                tac.score_x, 
                mqd.is_relaksasi
            FROM master_questioner_detail mqd
            LEFT JOIN trx_audit_checklist tac 
                ON tac.master_questioner_detail_id = mqd.id 
                AND tac.trx_audit_id = @id
            WHERE mqd.master_questioner_id = (
                SELECT master_questioner_checklist_id 
                FROM trx_audit 
                WHERE id = @id
            )
            AND mqd.type = 'QUESTION'";

                    var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(
                        sql, new { id = a.id }, transaction: tx)).ToList();

                    decimal sumAF = 0, sumWeight = 0, sumX = 0;

                    foreach (var item in checklist)
                    {
                        decimal w = item.weight ?? 0;
                        string input = item.score_input?.Trim().ToUpperInvariant() ?? "";

                        if (input == "X")
                        {
                            sumX += w;
                            sumAF += item.score_x ?? 0;
                        }
                        else if (input == "F" && item.is_relaksasi == true)
                        {
                            sumAF += 1.00m * w;
                        }
                        else
                        {
                            decimal af = input switch
                            {
                                "A" => 1.00m,
                                "B" => 0.80m,
                                "C" => 0.60m,
                                "D" => 0.40m,
                                "E" => 0.20m,
                                "F" => 0.00m,
                                _ => 0.00m
                            };
                            sumAF += af * w;
                        }

                        sumWeight += w;
                    }

                    decimal finalScore = (sumWeight - sumX) > 0
                        ? (sumAF / (sumWeight - sumX)) * sumWeight
                        : 0m;

                    // === Special Node Score Check ===
                    var specialNodeIds = new List<Guid>
            {
                Guid.Parse("555fe2e4-b95b-461b-9c92-ad8b5c837119"),
                Guid.Parse("bafc206f-ed29-4bbc-8053-38799e186fb0"),
                Guid.Parse("d26f4caa-e849-4ab4-9372-298693247272")
            };

                    if (a.created_date > new DateTime(2025, 5, 31))
                    {
                        specialNodeIds.Add(Guid.Parse("5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b"));
                    }

                    var specialScoreSql = @"
            SELECT mqd.id, tac.score_input, ta.created_date
            FROM master_questioner_detail mqd
            LEFT JOIN trx_audit_checklist tac 
                ON tac.master_questioner_detail_id = mqd.id 
                AND tac.trx_audit_id = @id
            LEFT JOIN trx_audit ta ON ta.id = tac.trx_audit_id
            WHERE mqd.id = ANY(@ids);";

                    var specialScoresRaw = (await conn.QueryAsync<(string id, string score_input, DateTime? created_date)>(
                        specialScoreSql,
                        new { id = a.id, ids = specialNodeIds.Select(x => x.ToString()).ToArray() },
                        transaction: tx
                    )).ToList();

                    var specialScores = specialScoresRaw
                        .Where(x =>
                            x.id != "5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b" ||
                            (x.created_date != null && x.created_date.Value < new DateTime(2025, 6, 1))
                        )
                        .ToDictionary(x => x.id, x => x.score_input?.Trim().ToUpperInvariant());

                    bool forceGoodOnly = false;
                    bool forceNotCertified = false;

                    foreach (var score in specialScores.Values)
                    {
                        if (score == "C")
                            forceGoodOnly = true;
                        else if (score != "A")
                            forceNotCertified = true;
                    }

                    // === Penalty checks ===
                    var penaltyExcellentQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));";

                    var penaltyGoodQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                WHERE tac.trx_audit_id = @id AND
                  tac.score_input = 'F' AND
                  mqd.is_penalty = true AND 
                  (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';";

                    var penaltyExcellentResult = await conn.ExecuteScalarAsync<string>(penaltyExcellentQuery, new { id = a.id }, transaction: tx);
                    var penaltyGoodResult = await conn.ExecuteScalarAsync<string>(penaltyGoodQuery, new { id = a.id }, transaction: tx);

                    bool hasExcellentPenalty = !string.IsNullOrEmpty(penaltyExcellentResult);
                    bool hasGoodPenalty = !string.IsNullOrEmpty(penaltyGoodResult);

                    // === Audit Next base (Flow) ===
                    string auditNext = "";
                    string levelspbu = null;

                    var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                    var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = a.audit_level }, transaction: tx);

                    // === Hitung Compliance ===
                    var checklistData = await GetChecklistDataAsync2(conn, a.id, tx);
                    var mediaList = await GetMediaPerNodeAsync2(conn, a.id, tx);
                    var elements = BuildHierarchy(checklistData, mediaList);
                    foreach (var element in elements) AssignWeightRecursive(element);
                    CalculateChecklistScores(elements);
                    CalculateOverallScore(new DetailReportViewModel { Elements = elements }, checklistData);
                    var modelstotal = new DetailReportViewModel { Elements = elements };
                    CalculateOverallScore(modelstotal, checklistData);
                    decimal? totalScore = modelstotal.TotalScore;
                    var compliance = HitungComplianceLevelDariElements(elements);

                    // === Compliance validation ===
                    var sss = Math.Round(compliance.SSS ?? 0, 2);
                    var eqnq = Math.Round(compliance.EQnQ ?? 0, 2);
                    var rfs = Math.Round(compliance.RFS ?? 0, 2);
                    var vfc = Math.Round(compliance.VFC ?? 0, 2);
                    var epo = Math.Round(compliance.EPO ?? 0, 2);

                    bool failGood = sss < 80 || eqnq < 85 || rfs < 85 || vfc < 15 || epo < 25;
                    bool failExcellent = sss < 85 || eqnq < 85 || rfs < 85 || vfc < 20 || epo < 50;

                    string goodStatus = (finalScore >= 75 && !hasGoodPenalty && !failGood)
                        ? "CERTIFIED"
                        : "NOT CERTIFIED";

                    string excellentStatus = (finalScore >= 80 && !hasExcellentPenalty && !failExcellent && !forceNotCertified)
                        ? (forceGoodOnly ? "GOOD" : "CERTIFIED")
                        : "NOT CERTIFIED";

                    decimal scoress = Math.Round((decimal)(totalScore ?? 0m), 2);

                    if (auditFlow != null)
                    {
                        string passedGood = auditFlow.passed_good;
                        string passedExcellent = auditFlow.passed_excellent;
                        string passedAuditLevel = auditFlow.passed_audit_level;
                        string failed_audit_level = auditFlow.failed_audit_level;

                        if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                        {
                            auditNext = passedAuditLevel;
                        }
                        else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                        {
                            auditNext = passedAuditLevel;
                        }
                        else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                        {
                            auditNext = failed_audit_level;
                        }
                        else if (goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                        {
                            auditNext = failed_audit_level;
                        }
                        else if (goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                        {
                            auditNext = passedGood;
                        }
                        else if (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                        {
                            auditNext = passedExcellent;
                        }
                        else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && finalScore >= 75)
                        {
                            auditNext = passedAuditLevel;
                        }
                        else
                        {
                            auditNext = failed_audit_level;
                        }

                        var auditlevelClassSql = @"SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                        var auditlevelClass = await conn.QueryFirstOrDefaultAsync<dynamic>(auditlevelClassSql, new { level = auditNext }, transaction: tx);
                        levelspbu = auditlevelClass != null
                            ? (auditlevelClass.audit_level_class ?? "")
                            : "";
                    }

                    // === UPDATE spbu.audit_next sesuai spbu_id audit ===
                    // Note: auditNext bisa null; jika ingin kosongkan kolom saat null, biarkan saja (Dapper => NULL).
                    var updateSql = @"
                    UPDATE spbu 
                    SET 
                        audit_current = @auditLevel,
                        audit_next    = @auditNext
                    WHERE id = @spbuId;
                    ";

                    await conn.ExecuteAsync(updateSql,
                        new { auditLevel = a.audit_level ?? string.Empty, auditNext = auditNext ?? string.Empty, spbuId = a.spbu_id },
                        transaction: tx);

                    // === Susun item untuk view ===
                    result.Add(new AuditReportListViewModel
                    {
                        TrxAuditId = a.id,
                        ReportNo = a.report_no,
                        SpbuNo = a.spbu.spbu_no,
                        Region = a.spbu.region,
                        Address = a.spbu.address,
                        City = a.spbu.city_name,
                        SBM = a.spbu.sbm,
                        SAM = a.spbu.sam,
                        Province = a.spbu.province_name,
                        Year = a.spbu.year ?? DateTime.Now.Year,
                        AuditDate = (a.audit_execution_time == null || a.audit_execution_time.Value == DateTime.MinValue) ? a.updated_date.Value : a.audit_execution_time.Value,
                        SubmitDate = a.approval_date.GetValueOrDefault() == DateTime.MinValue ? a.updated_date : a.approval_date.GetValueOrDefault(),
                        Auditor = a.app_user.name,
                        GoodStatus = goodStatus,
                        ExcellentStatus = excellentStatus,
                        Score = totalScore ?? a.score,
                        WTMS = a.spbu.wtms,
                        QQ = a.spbu.qq,
                        WMEF = a.spbu.wmef,
                        FormatFisik = a.spbu.format_fisik,
                        CPO = a.spbu.cpo,
                        KelasSpbu = levelspbu,
                        Auditlevel = a.audit_level,
                        AuditNext = auditNext,
                        ApproveDate = a.approval_date ?? DateTime.Now,
                        ApproveBy = string.IsNullOrWhiteSpace(a.approval_by) ? "-" : a.approval_by,
                        SSS = Math.Round(compliance.SSS ?? 0, 2),
                        EQnQ = Math.Round(compliance.EQnQ ?? 0, 2),
                        RFS = Math.Round(compliance.RFS ?? 0, 2),
                        VFC = Math.Round(compliance.VFC ?? 0, 2),
                        EPO = Math.Round(compliance.EPO ?? 0, 2)
                    });
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            var model = new PaginationModel<AuditReportListViewModel>
            {
                Items = result,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            ViewBag.SearchTerm = searchTerm;
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCsv(string searchTerm = "")
        {
            try
            {
                var currentUser = User.Identity?.Name;

                _context.Database.SetCommandTimeout(123600);

                var userRegion = await (from aur in _context.app_user_roles
                                        join au in _context.app_users on aur.app_user_id equals au.id
                                        where au.username == currentUser
                                        select aur.region)
                                   .Distinct()
                                   .Where(r => r != null)
                                   .ToListAsync();

                var allowedStatuses = new[] { "VERIFIED", "UNDER_REVIEW" };

                var baseQuery = _context.trx_audits
                    .Where(a =>
                        allowedStatuses.Contains(a.status) &&
                        a.audit_type != "Basic Operational"
                    );

                var lastAuditIdsQuery = baseQuery
                    .GroupBy(a => a.spbu_id) // atau a.spbu.spbu_no, sesuaikan
                    .Select(g => g
                        .OrderByDescending(a => a.audit_execution_time ?? a.created_date)
                        .Select(a => a.id)
                        .FirstOrDefault()
                    );

                var query = _context.trx_audits
                    .Where(a => lastAuditIdsQuery.Contains(a.id))
                    .Include(a => a.spbu)
                    .Include(a => a.app_user);

                var audits = await query
                    .OrderBy(a => a.spbu.spbu_no)
                    .ThenByDescending(a => a.audit_execution_time ?? a.created_date)
                    .ToListAsync();
                await using var conn2 = _context.Database.GetDbConnection();
                if (conn2.State != ConnectionState.Open)
                    await conn2.OpenAsync();

                var checklistNumbers = await conn2.QueryAsync<string>(@"
            SELECT DISTINCT number 
            FROM master_questioner_detail 
            WHERE number IS NOT NULL AND TRIM(number) <> '' 
            ORDER BY number ASC;
        ");

                var numberList = checklistNumbers.ToList();

                var csv = new StringBuilder();
                var headers = new[]
                {
            "send_date","Audit Date","spbu_no","region","year","address","city_name","tipe_spbu","rayon",
            "audit_level","audit_next","good_status","excellent_status","Total Score",
            "SSS","EQnQ","RFS","VFC","EPO","wtms","qq","wmef","format_fisik","cpo",
            "kelas_spbu","penalty_good_alerts","penalty_excellent_alerts"
        };

                // Tambahkan header number checklist
                csv.AppendLine(string.Join(",", headers.Concat(numberList).Select(h => $"\"{h}\"")));

                await using var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                foreach (var a in audits)
                {
                    var sql = @"
                SELECT 
                    mqd.weight, 
                    tac.score_input, 
                    tac.score_x, 
                    mqd.is_relaksasi
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac 
                    ON tac.master_questioner_detail_id = mqd.id 
                    AND tac.trx_audit_id = @id
                WHERE mqd.master_questioner_id = (
                    SELECT master_questioner_checklist_id 
                    FROM trx_audit 
                    WHERE id = @id
                )
                AND mqd.type = 'QUESTION'";

                    var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(
                        sql,
                        new { id = a.id },
                        commandTimeout: 60000
                    )).ToList();

                    decimal sumAF = 0, sumWeight = 0, sumX = 0;

                    foreach (var item in checklist)
                    {
                        decimal w = item.weight ?? 0;
                        string input = item.score_input?.Trim().ToUpperInvariant() ?? "";

                        if (input == "X")
                        {
                            sumX += w;
                            sumAF += item.score_x ?? 0;
                        }
                        else if (input == "F" && item.is_relaksasi == true)
                        {
                            sumAF += 1.00m * w;
                        }
                        else
                        {
                            decimal af = input switch
                            {
                                "A" => 1.00m,
                                "B" => 0.80m,
                                "C" => 0.60m,
                                "D" => 0.40m,
                                "E" => 0.20m,
                                "F" => 0.00m,
                                _ => 0.00m
                            };
                            sumAF += af * w;
                        }

                        sumWeight += w;
                    }

                    decimal finalScore = (sumWeight - sumX) > 0
                        ? (sumAF / (sumWeight - sumX)) * sumWeight
                        : 0m;

                    // === Special Node Score Check ===
                    var specialNodeIds = new List<Guid>
            {
                Guid.Parse("555fe2e4-b95b-461b-9c92-ad8b5c837119"),
                Guid.Parse("bafc206f-ed29-4bbc-8053-38799e186fb0"),
                Guid.Parse("d26f4caa-e849-4ab4-9372-298693247272")
            };

                    if (a.created_date > new DateTime(2025, 5, 31))
                    {
                        specialNodeIds.Add(Guid.Parse("5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b"));
                    }

                    var specialScoreSql = @"
                SELECT mqd.id, tac.score_input, ta.created_date
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac 
                    ON tac.master_questioner_detail_id = mqd.id 
                    AND tac.trx_audit_id = @id
                LEFT JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE mqd.id = ANY(@ids);";

                    var specialScoresRaw = (await conn.QueryAsync<(string id, string score_input, DateTime? created_date)>(
                        specialScoreSql,
                        new { id = a.id, ids = specialNodeIds.Select(x => x.ToString()).ToArray() }
                    )).ToList();

                    var specialScores = specialScoresRaw
                        .Where(x =>
                            x.id != "5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b" ||
                            (x.created_date != null && x.created_date.Value < new DateTime(2025, 6, 1))
                        )
                        .ToDictionary(x => x.id, x => x.score_input?.Trim().ToUpperInvariant());

                    bool forceGoodOnly = false;
                    bool forceNotCertified = false;

                    foreach (var score in specialScores.Values)
                    {
                        if (score == "C")
                            forceGoodOnly = true;
                        else if (score != "A")
                            forceNotCertified = true;
                    }

                    // === Penalty Check
                    var penaltyExcellentQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));";

                    var penaltyGoodQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                WHERE tac.trx_audit_id = @id AND
                  tac.score_input = 'F' AND
                  mqd.is_penalty = true AND 
                  (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';";


                    string penaltyExcellentResult = "";
                    string penaltyGoodResult = "";

                    try
                    {
                        // === Query penalty excellent
                        penaltyExcellentResult = await conn.ExecuteScalarAsync<string>(
                            penaltyExcellentQuery,
                            new { id = a.id },
                            commandTimeout: 60000
                        );

                        // === Query penalty good
                        penaltyGoodResult = await conn.ExecuteScalarAsync<string>(
                            penaltyGoodQuery,
                            new { id = a.id },
                            commandTimeout: 60000
                        );
                    }
                    catch (Exception ex)
                    {
                        // Logging optional
                        Console.WriteLine($"[ERROR] AuditID: {a.id} gagal ambil penalty. Reason: {ex.Message}");

                        // Kamu bisa simpan ke database audit log kalau perlu

                        // Supaya CSV tetap lanjut, jangan throw — set value khusus
                        penaltyExcellentResult = $"ERROR-{a.id}";
                        penaltyGoodResult = $"ERROR-{a.id}";
                    }

                    bool hasExcellentPenalty = !string.IsNullOrEmpty(penaltyExcellentResult);
                    bool hasGoodPenalty = !string.IsNullOrEmpty(penaltyGoodResult);

                    // === Audit Next
                    string auditNext = null;
                    string levelspbu = null;

                    var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                    var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = a.audit_level });

                    // === Hitung Compliance
                    var checklistData = await GetChecklistDataAsync(conn, a.id);
                    var mediaList = await GetMediaPerNodeAsync(conn, a.id);
                    var elements = BuildHierarchy(checklistData, mediaList);
                    foreach (var element in elements) AssignWeightRecursive(element);
                    CalculateChecklistScores(elements);
                    CalculateOverallScore(new DetailReportViewModel { Elements = elements }, checklistData);
                    var modelstotal = new DetailReportViewModel { Elements = elements };
                    CalculateOverallScore(modelstotal, checklistData);
                    decimal? totalScore = modelstotal.TotalScore;
                    var compliance = HitungComplianceLevelDariElements(elements);

                    var auditDate = a.audit_execution_time ?? a.updated_date ?? DateTime.MinValue;

                    var submitDate = a.approval_date == null || a.approval_date == DateTime.MinValue
                        ? a.updated_date
                        : a.approval_date;

                    // === Compliance validation
                    var sss = Math.Round(compliance.SSS ?? 0, 2);
                    var eqnq = Math.Round(compliance.EQnQ ?? 0, 2);
                    var rfs = Math.Round(compliance.RFS ?? 0, 2);
                    var vfc = Math.Round(compliance.VFC ?? 0, 2);
                    var epo = Math.Round(compliance.EPO ?? 0, 2);

                    bool failGood = sss < 80 || eqnq < 85 || rfs < 85 || vfc < 15 || epo < 25;
                    bool failExcellent = sss < 85 || eqnq < 85 || rfs < 85 || vfc < 20 || epo < 50;

                    // === Update status with compliance logic
                    string goodStatus = (finalScore >= 75 && !hasGoodPenalty && !failGood)
                        ? "CERTIFIED"
                        : "NOT CERTIFIED";

                    string excellentStatus = (finalScore >= 80 && !hasExcellentPenalty && !failExcellent && !forceNotCertified)
                        ? (forceGoodOnly ? "GOOD" : "CERTIFIED")
                        : "NOT CERTIFIED";

                    if (auditFlow != null)
                    {
                        string passedGood = auditFlow.passed_good;
                        string passedExcellent = auditFlow.passed_excellent;
                        string passedAuditLevel = auditFlow.passed_audit_level;
                        string failed_audit_level = auditFlow.failed_audit_level;

                        if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                        {
                            auditNext = passedAuditLevel;
                        }
                        else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                        {
                            auditNext = passedAuditLevel;
                        }
                        else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                        {
                            auditNext = failed_audit_level;
                        }
                        else if (goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                        {
                            auditNext = failed_audit_level;
                        }
                        else if (goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                        {
                            auditNext = passedGood;
                        }
                        else if (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                        {
                            auditNext = passedExcellent;
                        }
                        else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && finalScore >= 75)
                        {
                            auditNext = passedAuditLevel;
                        }
                        else
                        {
                            auditNext = failed_audit_level;
                        }

                        var auditlevelClassSql = @"SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                        var auditlevelClass = await conn.QueryFirstOrDefaultAsync<dynamic>(auditlevelClassSql, new { level = auditNext });
                        levelspbu = auditlevelClass != null
                            ? (auditlevelClass.audit_level_class ?? "")
                            : "";
                    }

                    // === UPDATE AUDIT_NEXT & LEVEL SPBU KE TABEL SPBU
                    var updateSpbuSql = @"
                    UPDATE spbu 
                    SET 
                        audit_next = @auditNext,
                        ""level""   = @level,
                        updated_date = NOW()
                    WHERE id = @spbuId;
                    ";

                    await conn.ExecuteAsync(updateSpbuSql, new
                    {
                        auditNext = auditNext,
                        level = levelspbu,
                        spbuId = a.spbu_id
                    });

                    var checklistRaw = await conn.QueryAsync<(string number, decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(@"
                SELECT DISTINCT ON (mqd.number) 
                    mqd.number, mqd.weight, tac.score_input, tac.score_x, mqd.is_relaksasi
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac 
                    ON tac.master_questioner_detail_id = mqd.id 
                    AND tac.trx_audit_id = @id
                WHERE mqd.number IS NOT NULL AND TRIM(mqd.number) <> ''
                ORDER BY mqd.number, tac.updated_date DESC NULLS LAST
            ", new { id = a.id });

                    var checklistMap = checklistRaw
                        .GroupBy(x => x.number)
                        .ToDictionary(
                            g => g.Key,
                            g => g.First().score_input?.Trim().ToUpperInvariant() ?? ""
                        );

                    var checklistValues = numberList.Select(number => $"\"{(checklistMap.TryGetValue(number, out var val) ? val : "")}\"");

                    decimal scores = (decimal)(totalScore ?? a.score);

                    csv.AppendLine(string.Join(",", new[]
                    {
                $"\"{submitDate:yyyy-MM-dd}\"",
                $"\"{auditDate:yyyy-MM-dd}\"",
                $"\"{a.spbu.spbu_no}\"",
                $"\"{a.spbu.region}\"",
                $"\"{a.spbu.year ?? DateTime.Now.Year}\"",
                $"\"{a.spbu.address}\"",
                $"\"{a.spbu.city_name}\"",
                $"\"{a.spbu.owner_type}\"",
                $"\"{a.spbu.sbm}\"",
                $"\"{a.audit_level}\"",
                $"\"{auditNext}\"",
                $"\"{goodStatus}\"",
                $"\"{excellentStatus}\"",
                $"\"{scores:0.##}\"",
                $"\"{sss}\"",
                $"\"{eqnq}\"",
                $"\"{rfs}\"",
                $"\"{vfc}\"",
                $"\"{epo}\"",
                $"\"{a.spbu.wtms}\"",
                $"\"{a.spbu.qq}\"",
                $"\"{a.spbu.wmef}\"",
                $"\"{a.spbu.format_fisik}\"",
                $"\"{a.spbu.cpo}\"",
                $"\"{levelspbu}\"",
                $"\"{penaltyGoodResult}\"",
                $"\"{penaltyExcellentResult}\""
            }.Concat(checklistValues)));
                }

                var fileName = $"Audit_Summary_{DateTime.Now:yyyyMMddHHmmss}.csv";
                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                return File(bytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                // TODO: kalau kamu punya ILogger, pakai ini:
                // _logger.LogError(ex, "Error saat generate CSV di DownloadCsv");

                // Bisa pakai TempData kalau mau tampilkan ke View:
                // TempData["ErrorMessage"] = "Terjadi kesalahan saat generate CSV. Silakan coba lagi atau hubungi administrator.";
                // return RedirectToAction("Index");

                return StatusCode(StatusCodes.Status500InternalServerError,
                    $"Terjadi error saat generate CSV: {ex.Message}");
            }
        }

        [HttpGet]
        public IActionResult DownloadCsvByDate(int month, int year, string? searchTerm = "")
        {
            // Validasi basic
            if (month < 1 || month > 12 || year < 1 || year > 9999)
            {
                return BadRequest("Parameter bulan/tahun tidak valid.");
            }

            var reportFolder = Path.Combine(_env.WebRootPath, "Report");
            if (!Directory.Exists(reportFolder))
                return NotFound("Folder report tidak ditemukan.");

            // Ambil singkatan bulan: Jan, Feb, Mar, ... Sep, dst
            var monthAbbr = CultureInfo.InvariantCulture
                .DateTimeFormat
                .AbbreviatedMonthNames[month - 1];   // index = month - 1

            if (string.IsNullOrEmpty(monthAbbr))
                return BadRequest("Bulan tidak valid.");

            // Nama file di wwwroot/Report, contoh: Sep_2025_Audit_Summary.csv
            var sourceFileName = $"{monthAbbr}_{year}_Audit_Summary.csv";
            var fullPath = Path.Combine(reportFolder, sourceFileName);

            if (!System.IO.File.Exists(fullPath))
                return NotFound($"File report {sourceFileName} tidak ditemukan.");

            // Nama file saat di-download (boleh pakai timestamp supaya unik)
            var downloadFileName = $"{monthAbbr}_{year}_Audit_Summary_{DateTime.Now:yyyyMMddHHmmss}.csv";

            return PhysicalFile(fullPath, "text/csv", downloadFileName);
        }

        //    [HttpGet]
        //    public async Task<IActionResult> DownloadCsvByDate(
        //string searchTerm = "",
        //DateTime? startDate = null,
        //DateTime? endDate = null)
        //    {
        //        if (startDate == null || endDate == null)
        //        {
        //            // boleh kamu ganti default-nya kalau perlu
        //            return BadRequest("startDate dan endDate wajib diisi.");
        //        }

        //        // Normalisasi jam supaya perbandingan gampang
        //        startDate = startDate.Value.Date;
        //        endDate = endDate.Value.Date;

        //        // Folder: wwwroot/Report
        //        var reportFolder = Path.Combine(_env.WebRootPath, "Report");

        //        if (!Directory.Exists(reportFolder))
        //            return NotFound("Folder report tidak ditemukan.");

        //        // Kumpulkan list file berdasarkan rentang bulan
        //        var files = new List<string>();

        //        var cursor = new DateTime(startDate.Value.Year, startDate.Value.Month, 1);
        //        var last = new DateTime(endDate.Value.Year, endDate.Value.Month, 1);

        //        while (cursor <= last)
        //        {
        //            var monthAbbr = cursor.ToString("MMM", CultureInfo.InvariantCulture); // Jul, Aug, dst
        //            var fileName = $"{monthAbbr}_{cursor:yyyy}_Audit_Summary.csv";
        //            var fullPath = Path.Combine(reportFolder, fileName);

        //            if (System.IO.File.Exists(fullPath))
        //            {
        //                files.Add(fullPath);
        //            }

        //            cursor = cursor.AddMonths(1);
        //        }

        //        if (files.Count == 0)
        //            return NotFound("Tidak ada file report untuk periode tersebut.");

        //        var sb = new StringBuilder();
        //        bool headerWritten = false;

        //        searchTerm = searchTerm?.Trim();
        //        bool hasSearch = !string.IsNullOrWhiteSpace(searchTerm);

        //        foreach (var file in files)
        //        {
        //            var lines = await System.IO.File.ReadAllLinesAsync(file);
        //            if (lines.Length == 0) continue;

        //            // Tuliskan header sekali saja (baris pertama file pertama)
        //            if (!headerWritten)
        //            {
        //                sb.AppendLine(lines[0]);
        //                headerWritten = true;
        //            }

        //            // Mulai dari baris ke-2 (index 1) = data
        //            for (int i = 1; i < lines.Length; i++)
        //            {
        //                var line = lines[i];
        //                if (string.IsNullOrWhiteSpace(line)) continue;

        //                // Filter searchTerm (cek di seluruh baris)
        //                if (hasSearch &&
        //                    !line.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
        //                {
        //                    continue;
        //                }

        //                // Filter tanggal berdasarkan kolom "Audit Date"
        //                // Asumsi format baris: "send_date","Audit Date","spbu_no",...
        //                // dan kita simpan sebagai "yyyy-MM-dd"
        //                if (startDate != null || endDate != null)
        //                {
        //                    var cols = SplitCsvLine(line); // helper di bawah
        //                    if (cols.Length > 1) // kolom ke-2 = Audit Date
        //                    {
        //                        var auditDateStr = cols[1]; // sudah tanpa tanda kutip
        //                        if (DateTime.TryParse(auditDateStr, out var auditDate))
        //                        {
        //                            auditDate = auditDate.Date;

        //                            if (auditDate < startDate.Value || auditDate > endDate.Value)
        //                                continue;
        //                        }
        //                    }
        //                }

        //                sb.AppendLine(line);
        //            }
        //        }
        //        // Tentukan bulan dan tahun dari endDate (atau current date kalau null)
        //        var reportMonth = (startDate ?? DateTime.Now).ToString("MMM", CultureInfo.InvariantCulture); // contoh: "Nov"
        //        var reportYear = (endDate ?? DateTime.Now).ToString("yyyy");

        //        // Format akhir: "Nov_2025_BOA_Audit_Summary_20251110115233.csv"
        //        var outFileName = $"{reportMonth}_{reportYear}_Audit_Summary_{DateTime.Now:yyyyMMddHHmmss}.csv";

        //        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        //        return File(bytes, "text/csv", outFileName);
        //    }

        private static string[] SplitCsvLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return Array.Empty<string>();

            // buang quote awal/akhir, lalu split pakai "," di tengah
            // contoh: "\"2025-07-01\",\"2025-07-03\",\"xxxx\""
            // -> 2025-07-01 | 2025-07-03 | xxxx
            return line.Trim()
                       .Trim('"')
                       .Split("\",\"", StringSplitOptions.None);
        }

        //    [HttpGet]
        //    public async Task<IActionResult> DownloadCsvByDate(
        //string searchTerm = "",
        //DateTime? startDate = null,
        //DateTime? endDate = null)
        //    {
        //        var currentUser = User.Identity?.Name;

        //        _context.Database.SetCommandTimeout(123600);

        //        var userRegion = await (from aur in _context.app_user_roles
        //                                join au in _context.app_users on aur.app_user_id equals au.id
        //                                where au.username == currentUser
        //                                select aur.region)
        //                           .Distinct()
        //                           .Where(r => r != null)
        //                           .ToListAsync();

        //        var allowedStatuses = new[] { "VERIFIED" };

        //        var query = _context.trx_audits
        //            .Include(a => a.spbu)
        //            .Include(a => a.app_user)
        //            .Where(a =>
        //                allowedStatuses.Contains(a.status) &&
        //                a.audit_execution_time >= startDate &&
        //                a.audit_execution_time < endDate &&
        //                a.audit_type != "Basic Operational"
        //            );

        //        if (userRegion.Any())
        //        {
        //            query = query.Where(x => userRegion.Contains(x.spbu.region));
        //        }

        //        if (!string.IsNullOrWhiteSpace(searchTerm))
        //        {
        //            searchTerm = searchTerm.ToLower();
        //            query = query.Where(a =>
        //                a.spbu.spbu_no.ToLower().Contains(searchTerm) ||
        //                a.app_user.name.ToLower().Contains(searchTerm) ||
        //                a.status.ToLower().Contains(searchTerm) ||
        //                a.spbu.address.ToLower().Contains(searchTerm) ||
        //                a.spbu.province_name.ToLower().Contains(searchTerm) ||
        //                a.spbu.city_name.ToLower().Contains(searchTerm)
        //            );
        //        }

        //        var audits = await query
        //            .OrderByDescending(a => a.audit_execution_time ?? a.updated_date)
        //            .ToListAsync();

        //        // Header CSV (tanpa kolom checklist number)
        //        var csv = new StringBuilder();
        //        var headers = new[]
        //        {
        //    "send_date","Audit Date","spbu_no","region","year","address","city_name","tipe_spbu","rayon",
        //    "audit_level","audit_next","good_status","excellent_status","Total Score",
        //    "SSS","EQnQ","RFS","VFC","EPO","wtms","qq","wmef","format_fisik","cpo",
        //    "kelas_spbu","penalty_good_alerts","penalty_excellent_alerts"
        //};
        //        csv.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));

        //        await using var conn = _context.Database.GetDbConnection();
        //        if (conn.State != ConnectionState.Open)
        //            await conn.OpenAsync();

        //        foreach (var a in audits)
        //        {
        //            var auditDate = a.audit_execution_time ?? a.updated_date ?? DateTime.MinValue;

        //            var submitDate = a.approval_date == null || a.approval_date == DateTime.MinValue
        //                ? a.updated_date
        //                : a.approval_date;

        //            // === Penalty Check (EXCELLENT - non-relaksasi)
        //            var penaltyExcellentQuery = @"
        //        SELECT STRING_AGG(mqd.penalty_alert, ', ')
        //        FROM trx_audit_checklist tac
        //        JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
        //        JOIN trx_audit ta ON ta.id = tac.trx_audit_id
        //        WHERE tac.trx_audit_id = @id
        //          AND (
        //                (
        //                    tac.master_questioner_detail_id IN (
        //                        '555fe2e4-b95b-461b-9c92-ad8b5c837119',
        //                        'bafc206f-ed29-4bbc-8053-38799e186fb0',
        //                        'd26f4caa-e849-4ab4-9372-298693247272'
        //                    )
        //                    AND tac.score_input <> 'A'
        //                )
        //             OR (
        //                    tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
        //                    AND ta.created_date < DATE '2025-06-01'
        //                    AND tac.score_input <> 'A'
        //                )
        //             OR (
        //                      (
        //                          (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A')
        //                       OR (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
        //                      )
        //                      AND COALESCE(mqd.is_relaksasi, false) = false
        //                      AND mqd.is_penalty = true
        //                      AND NOT (
        //                          mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
        //                          AND ta.created_date >= DATE '2025-06-01'
        //                      )
        //                  )
        //            );";

        //            // === Penalty Check (GOOD - non-relaksasi)
        //            var penaltyGoodQuery = @"
        //            SELECT STRING_AGG(mqd.penalty_alert, ', ')
        //            FROM trx_audit_checklist tac
        //            JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
        //            WHERE tac.trx_audit_id = @id
        //              AND tac.score_input = 'F'
        //              AND mqd.is_penalty = true
        //              AND COALESCE(mqd.is_relaksasi, false) = false
        //              AND mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';";

        //            var penaltyExcellentResult = await conn.ExecuteScalarAsync<string>(penaltyExcellentQuery, new { id = a.id });
        //            var penaltyGoodResult = await conn.ExecuteScalarAsync<string>(penaltyGoodQuery, new { id = a.id });

        //            // === Hitung Compliance
        //            var checklistData = await GetChecklistDataAsync(conn, a.id);
        //            var mediaList = await GetMediaPerNodeAsync(conn, a.id);
        //            var elements = BuildHierarchy(checklistData, mediaList);
        //            foreach (var element in elements) AssignWeightRecursive(element);
        //            CalculateChecklistScores(elements);

        //            // Total score
        //            var modelTotal = new DetailReportViewModel { Elements = elements };
        //            CalculateOverallScore(modelTotal, checklistData);
        //            decimal? totalScore = modelTotal.TotalScore;

        //            // Compliance breakdown
        //            var compliance = HitungComplianceLevelDariElements(elements);
        //            var sss = Math.Round(compliance.SSS ?? 0, 2);
        //            var eqnq = Math.Round(compliance.EQnQ ?? 0, 2);
        //            var rfs = Math.Round(compliance.RFS ?? 0, 2);
        //            var vfc = Math.Round(compliance.VFC ?? 0, 2);
        //            var epo = Math.Round(compliance.EPO ?? 0, 2);

        //            decimal scores = (decimal)(totalScore ?? a.score);

        //            csv.AppendLine(string.Join(",", new[]
        //            {
        //                $"\"{submitDate:yyyy-MM-dd}\"",
        //                $"\"{auditDate:yyyy-MM-dd}\"",
        //                $"\"{a.spbu.spbu_no}\"",
        //                $"\"{a.spbu.region}\"",
        //                $"\"{a.spbu.year ?? DateTime.Now.Year}\"",
        //                $"\"{a.spbu.address}\"",
        //                $"\"{a.spbu.city_name}\"",
        //                $"\"{a.spbu.owner_type}\"",
        //                $"\"{a.spbu.sbm}\"",
        //                $"\"{a.audit_level}\"",
        //                $"\"{a.spbu.audit_next}\"",
        //                $"\"{a.good_status}\"",
        //                $"\"{a.excellent_status}\"",
        //                $"\"{scores:0.##}\"",
        //                $"\"{sss}\"",
        //                $"\"{eqnq}\"",
        //                $"\"{rfs}\"",
        //                $"\"{vfc}\"",
        //                $"\"{epo}\"",
        //                $"\"{a.spbu.wtms}\"",
        //                $"\"{a.spbu.qq}\"",
        //                $"\"{a.spbu.wmef}\"",
        //                $"\"{a.spbu.format_fisik}\"",
        //                $"\"{a.spbu.cpo}\"",
        //                $"\"{a.spbu.level}\"",
        //                $"\"{penaltyGoodResult}\"",
        //                $"\"{penaltyExcellentResult}\""
        //            }));
        //        }

        //        var fileName = $"Audit_Summary_{DateTime.Now:yyyyMMddHHmmss}.csv";
        //        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        //        return File(bytes, "text/csv", fileName);
        //    }

        private async Task<List<AuditReportListViewModel>> GetAuditReportViewModels(List<trx_audit> audits)
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var result = new List<AuditReportListViewModel>();

            foreach (var a in audits)
            {
                var sql = @"
        SELECT 
            mqd.weight, 
            tac.score_input, 
            tac.score_x, 
            mqd.is_relaksasi
        FROM master_questioner_detail mqd
        LEFT JOIN trx_audit_checklist tac 
            ON tac.master_questioner_detail_id = mqd.id 
            AND tac.trx_audit_id = @id
        WHERE mqd.master_questioner_id = (
            SELECT master_questioner_checklist_id 
            FROM trx_audit 
            WHERE id = @id
        )
        AND mqd.type = 'QUESTION'";

                var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(sql, new { id = a.id })).ToList();

                decimal sumAF = 0, sumWeight = 0, sumX = 0;

                foreach (var item in checklist)
                {
                    decimal w = item.weight ?? 0;
                    string input = item.score_input?.Trim().ToUpperInvariant() ?? "";

                    if (input == "X")
                    {
                        sumX += w;
                        sumAF += item.score_x ?? 0;
                    }
                    else if (input == "F" && item.is_relaksasi == true)
                    {
                        sumAF += 1.00m * w;
                    }
                    else
                    {
                        decimal af = input switch
                        {
                            "A" => 1.00m,
                            "B" => 0.80m,
                            "C" => 0.60m,
                            "D" => 0.40m,
                            "E" => 0.20m,
                            "F" => 0.00m,
                            _ => 0.00m
                        };
                        sumAF += af * w;
                    }

                    sumWeight += w;
                }

                decimal finalScore = (sumWeight - sumX) > 0 ? (sumAF / (sumWeight - sumX)) * sumWeight : 0m;

                // === Special Node Score Check ===
                var specialNodeIds = new List<Guid>
                {
                    Guid.Parse("555fe2e4-b95b-461b-9c92-ad8b5c837119"),
                    Guid.Parse("bafc206f-ed29-4bbc-8053-38799e186fb0"),
                    Guid.Parse("d26f4caa-e849-4ab4-9372-298693247272")
                };

                if (a.created_date > new DateTime(2025, 5, 31))
                {
                    specialNodeIds.Add(Guid.Parse("5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b"));
                }

                var specialScoreSql = @"
                SELECT mqd.id, tac.score_input, ta.created_date
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac 
                    ON tac.master_questioner_detail_id = mqd.id 
                    AND tac.trx_audit_id = @id
                LEFT JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE mqd.id = ANY(@ids);";

                var specialScoresRaw = (await conn.QueryAsync<(string id, string score_input, DateTime? created_date)>(
                    specialScoreSql,
                    new { id = a.id, ids = specialNodeIds.Select(x => x.ToString()).ToArray() }
                )).ToList();

                var specialScores = specialScoresRaw
                    .Where(x =>
                        x.id != "5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b" ||
                        (x.created_date != null && x.created_date.Value < new DateTime(2025, 6, 1))
                    )
                    .ToDictionary(x => x.id, x => x.score_input?.Trim().ToUpperInvariant());


                bool forceGoodOnly = false;
                bool forceNotCertified = false;

                foreach (var score in specialScores.Values)
                {
                    if (score == "C") forceGoodOnly = true;
                    else if (score != "A") forceNotCertified = true;
                }

                var penaltyExcellentQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));";

                var penaltyGoodQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
            FROM trx_audit_checklist tac
            INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
            WHERE tac.trx_audit_id = @id AND tac.score_input = 'F' AND mqd.is_penalty = true AND 
                  (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';";

                var penaltyExcellentResult = await conn.ExecuteScalarAsync<string>(penaltyExcellentQuery, new { id = a.id });
                var penaltyGoodResult = await conn.ExecuteScalarAsync<string>(penaltyGoodQuery, new { id = a.id });

                bool hasExcellentPenalty = !string.IsNullOrEmpty(penaltyExcellentResult);
                bool hasGoodPenalty = !string.IsNullOrEmpty(penaltyGoodResult);

                string auditNext = null;
                string levelspbu = null;

                var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1",
                    new { level = a.audit_level });

                var checklistData = await GetChecklistDataAsync(conn, a.id);
                var mediaList = await GetMediaPerNodeAsync(conn, a.id);
                var elements = BuildHierarchy(checklistData, mediaList);
                foreach (var element in elements) AssignWeightRecursive(element);
                CalculateChecklistScores(elements);
                CalculateOverallScore(new DetailReportViewModel { Elements = elements }, checklistData);
                var compliance = HitungComplianceLevelDariElements(elements);

                var sss = Math.Round(compliance.SSS ?? 0, 2);
                var eqnq = Math.Round(compliance.EQnQ ?? 0, 2);
                var rfs = Math.Round(compliance.RFS ?? 0, 2);
                var vfc = Math.Round(compliance.VFC ?? 0, 2);
                var epo = Math.Round(compliance.EPO ?? 0, 2);

                bool failGood = sss < 80 || eqnq < 85 || rfs < 85 || vfc < 15 || epo < 25;
                bool failExcellent = sss < 85 || eqnq < 85 || rfs < 85 || vfc < 20 || epo < 50;

                string goodStatus = (finalScore >= 75 && !hasGoodPenalty && !failGood) ? "CERTIFIED" : "NOT CERTIFIED";
                string excellentStatus = (finalScore >= 80 && !hasExcellentPenalty && !failExcellent && !forceNotCertified)
                    ? (forceGoodOnly ? "GOOD" : "CERTIFIED")
                    : "NOT CERTIFIED";

                if (auditFlow != null)
                {
                    string passedGood = auditFlow.passed_good;
                    string passedExcellent = auditFlow.passed_excellent;
                    string passedAuditLevel = auditFlow.passed_audit_level;
                    string failed_audit_level = auditFlow.failed_audit_level;

                    if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                        auditNext = passedAuditLevel;
                    else if (goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                        auditNext = passedGood;
                    else if (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                        auditNext = passedExcellent;
                    else
                        auditNext = failed_audit_level;

                    var auditClass = await conn.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1",
                        new { level = auditNext });
                    levelspbu = auditClass?.audit_level_class ?? "";
                }


                result.Add(new AuditReportListViewModel
                {
                    TrxAuditId = a.id,
                    ReportNo = a.report_no,
                    SpbuNo = a.spbu.spbu_no,
                    Region = a.spbu.region,
                    Address = a.spbu.address,
                    City = a.spbu.city_name,
                    SBM = a.spbu.sbm,
                    SAM = a.spbu.sam,
                    Province = a.spbu.province_name,
                    Year = a.spbu.year ?? DateTime.Now.Year,
                    AuditDate = (a.audit_execution_time == null || a.audit_execution_time.Value == DateTime.MinValue) ? a.updated_date.Value : a.audit_execution_time.Value,
                    SubmitDate = a.approval_date.GetValueOrDefault() == DateTime.MinValue ? a.updated_date : a.approval_date.GetValueOrDefault(),
                    Auditor = a.app_user.name,
                    GoodStatus = goodStatus,
                    ExcellentStatus = excellentStatus,
                    Score = (a.spbu.audit_current_score.HasValue && a.spbu.audit_current_score.Value != 0) ? a.spbu.audit_current_score.Value : finalScore,
                    WTMS = a.spbu.wtms,
                    QQ = a.spbu.qq,
                    WMEF = a.spbu.wmef,
                    FormatFisik = a.spbu.format_fisik,
                    CPO = a.spbu.cpo,
                    KelasSpbu = levelspbu,
                    Auditlevel = a.audit_level,
                    AuditNext = auditNext,
                    ApproveDate = a.approval_date ?? DateTime.Now,
                    ApproveBy = string.IsNullOrWhiteSpace(a.approval_by) ? "-" : a.approval_by,
                    SSS = sss,
                    EQnQ = eqnq,
                    RFS = rfs,
                    VFC = vfc,
                    EPO = epo
                });
            }

            return result;
        }

        [HttpGet("Report/PreviewDetail/{id}")]
        public async Task<IActionResult> PreviewDetail(string id)
        {
            // --- Open Manual NpgsqlConnection (SAFE) ---
            var connectionString = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // --- Header Audit ---
            var basic = await GetAuditHeaderAsync(conn, id);
            if (basic == null)
                return NotFound();

            var model = MapToViewModel(basic);

            var penaltySql = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));";

            model.PenaltyAlerts = await conn.ExecuteScalarAsync<string>(penaltySql, new { id = id.ToString() });


            var penaltySqlGood = @"
            SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
            FROM trx_audit_checklist tac
            INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
            WHERE 
                tac.trx_audit_id = @id AND
                tac.score_input = 'F' AND
                mqd.is_penalty = true AND 
                (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';";

            model.PenaltyAlertsGood = await conn.ExecuteScalarAsync<string>(penaltySqlGood, new { id = id.ToString() });

            model.MediaNotes = await GetMediaNotesAsync(conn, id, "QUESTION");
            model.FinalDocuments = await GetMediaNotesAsync(conn, id, "FINAL");

            model.QqChecks = await GetQqCheckDataAsync(conn, id);

            var checklistData = await GetChecklistDataAsync(conn, id);
            var mediaList = await GetMediaPerNodeAsync(conn, id);
            model.Elements = BuildHierarchy(checklistData, mediaList);

            foreach (var element in model.Elements)
            {
                AssignWeightRecursive(element);
            }

            CalculateChecklistScores(model.Elements);
            CalculateOverallScore(model, checklistData);

            var compliance = HitungComplianceLevelDariElements(model.Elements);
            model.SSS = Math.Round(compliance.SSS ?? 0, 2);
            model.EQnQ = Math.Round(compliance.EQnQ ?? 0, 2);
            model.RFS = Math.Round(compliance.RFS ?? 0, 2);
            model.VFC = Math.Round(compliance.VFC ?? 0, 2);
            model.EPO = Math.Round(compliance.EPO ?? 0, 2);

            ViewBag.AuditId = id;

            var penaltyExcellentQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));";

            var penaltyGoodQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                WHERE tac.trx_audit_id = @id AND
                      tac.score_input = 'F' AND
                      mqd.is_penalty = true AND 
                      (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';";

            var penaltyExcellentResult = await conn.ExecuteScalarAsync<string>(penaltyExcellentQuery, new { id = model.AuditId });
            var penaltyGoodResult = await conn.ExecuteScalarAsync<string>(penaltyGoodQuery, new { id = model.AuditId });

            bool hasExcellentPenalty = !string.IsNullOrEmpty(penaltyExcellentResult);
            bool hasGoodPenalty = !string.IsNullOrEmpty(penaltyGoodResult);

            string goodStatus = (model.TotalScore >= 75 && !hasGoodPenalty) ? "CERTIFIED" : "NOT CERTIFIED";
            string excellentStatus = (model.TotalScore >= 80 && !hasExcellentPenalty) ? "CERTIFIED" : "NOT CERTIFIED";

            string auditNext = null;
            string levelspbu = null;

            var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
            var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = model.AuditCurrent });

            if (auditFlow != null)
            {
                string passedGood = auditFlow.passed_good;
                string passedExcellent = auditFlow.passed_excellent;
                string passedAuditLevel = auditFlow.passed_audit_level;
                string failed_audit_level = auditFlow.failed_audit_level;

                if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                {
                    model.AuditNext = passedAuditLevel;
                }
                else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                {
                    model.AuditNext = passedAuditLevel;
                }
                else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                {
                    model.AuditNext = failed_audit_level;
                }
                else if (goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                {
                    model.AuditNext = failed_audit_level;
                }
                else if (goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                {
                    model.AuditNext = passedGood;
                }
                else if (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                {
                    model.AuditNext = passedExcellent;
                }
                else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && model.TotalScore >= 75)
                {
                    model.AuditNext = passedAuditLevel;
                }
                else
                {
                    model.AuditNext = failed_audit_level;
                }

                var auditlevelClassSql = @"SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                var auditlevelClass = await conn.QueryFirstOrDefaultAsync<dynamic>(auditlevelClassSql, new { level = model.AuditNext });
                levelspbu = auditlevelClass != null
                ? (auditlevelClass.audit_level_class ?? "")
                : "";
            }

            model.ClassSPBU = levelspbu;

            var updateSql = @"
            UPDATE spbu
            SET audit_current_score = @score
            WHERE spbu_no = @spbuNo";

            await conn.ExecuteAsync(updateSql, new
            {
                score = Math.Round(model.TotalScore, 2),
                spbuNo = model.SpbuNo
            });

            return View(model);
        }


        [HttpGet("Report/Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            // Buka koneksi manual, bukan _context.Database.GetDbConnection()
            var connectionString = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // Pull the main audit header pakai Dapper helper
            var basic = await GetAuditHeaderAsync(conn, id);
            if (basic == null)
                return NotFound();

            var model = MapToViewModel(basic);

            var penaltySql = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));";

            model.PenaltyAlerts = await conn.ExecuteScalarAsync<string>(penaltySql, new { id });

            var penaltySqlGood = @"
    SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
    FROM trx_audit_checklist tac
    INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
    WHERE 
        tac.trx_audit_id = @id AND
        tac.score_input = 'F' AND
        mqd.is_penalty = true AND 
        (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';";
            model.PenaltyAlertsGood = await conn.ExecuteScalarAsync<string>(penaltySqlGood, new { id });

            model.MediaNotes = await GetMediaNotesAsync(conn, id, "QUESTION");
            model.FinalDocuments = await GetMediaNotesAsync(conn, id, "FINAL");
            model.QqChecks = await GetQqCheckDataAsync(conn, id);

            var checklistData = await GetChecklistDataAsync(conn, id);
            var mediaList = await GetMediaPerNodeAsync(conn, id);
            model.Elements = BuildHierarchy(checklistData, mediaList);

            foreach (var element in model.Elements)
                AssignWeightRecursive(element);

            CalculateChecklistScores(model.Elements);
            CalculateOverallScore(model, checklistData);

            // Hitung compliance level
            var compliance = HitungComplianceLevelDariElements(model.Elements);
            model.SSS = Math.Round(compliance.SSS ?? 0, 2);
            model.EQnQ = Math.Round(compliance.EQnQ ?? 0, 2);
            model.RFS = Math.Round(compliance.RFS ?? 0, 2);
            model.VFC = Math.Round(compliance.VFC ?? 0, 2);
            model.EPO = Math.Round(compliance.EPO ?? 0, 2);

            // Validasi sertifikasi
            bool failGood = model.SSS < 80 || model.EQnQ < 85 || model.RFS < 85 || model.VFC < 15 || model.EPO < 25;
            bool failExcellent = model.SSS < 85 || model.EQnQ < 85 || model.RFS < 85 || model.VFC < 20 || model.EPO < 50;
            bool hasExcellentPenalty = !string.IsNullOrEmpty(model.PenaltyAlerts);
            bool hasGoodPenalty = !string.IsNullOrEmpty(model.PenaltyAlertsGood);

            model.GoodStatus = (model.TotalScore >= 75 && !hasGoodPenalty && !failGood) ? "CERTIFIED" : "NOT CERTIFIED";
            model.ExcellentStatus = (model.TotalScore >= 80 && !hasExcellentPenalty && !failExcellent) ? "CERTIFIED" : "NOT CERTIFIED";

            // Audit next dan class
            var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
            var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = model.AuditCurrent });

            if (auditFlow != null)
            {
                string goodStatus = model.GoodStatus;
                string excellentStatus = model.ExcellentStatus;
                string auditNext = null;

                string passedLevel = auditFlow.passed_audit_level;

                if (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                    auditNext = auditFlow.passed_excellent;
                else if (goodStatus == "CERTIFIED")
                    auditNext = auditFlow.passed_good;
                else
                    auditNext = auditFlow.failed_audit_level;

                model.AuditNext = auditNext;

                if (auditNext == null)
                {
                    auditNext = passedLevel;
                    model.AuditNext = passedLevel;
                }

                var auditlevelClassSql = @"SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                var auditlevelClass = await conn.QueryFirstOrDefaultAsync<dynamic>(auditlevelClassSql, new { level = auditNext });
                model.ClassSPBU = auditlevelClass?.audit_level_class ?? "";
            }

            ViewBag.AuditId = id;

            // Update audit_current_score ke spbu
            var updateSql = @"UPDATE spbu SET audit_current_score = @score WHERE spbu_no = @spbuNo";
            await conn.ExecuteAsync(updateSql, new
            {
                score = Math.Round(model.TotalScore, 2),
                spbuNo = model.SpbuNo
            });

            var updateSqltrx_audit = @"UPDATE trx_audit SET score = @score WHERE id = @id";
            await conn.ExecuteAsync(updateSqltrx_audit, new
            {
                score = Math.Round(model.TotalScore, 2),
                id = id
            });

            return View(model);
        }


        public async Task<IActionResult> DownloadPdfQuest(Guid id)
        {
            var model = await GetDetailReportAsync(id);

            var json = JsonConvert.SerializeObject(model);

            var document = new ReportExcellentTemplate(model);

            var pdfStream = new MemoryStream();
            document.GeneratePdf(pdfStream);
            pdfStream.Position = 0;
            string spbuNo = model.SpbuNo?.Replace(" ", "") ?? "SPBU";
            string tanggalAudit = model.TanggalAudit?.ToString("yyyyMMdd") ?? "00000000";
            string fileName = $"audit_{spbuNo}_{tanggalAudit}.pdf";

            return File(pdfStream, "application/pdf", fileName);

        }

        public async Task<IActionResult> DownloadPdfQuestGood(Guid id)
        {
            var model = await GetDetailReportAsync(id);

            var json = JsonConvert.SerializeObject(model);

            var document = new ReportGoodTemplate(model);

            var pdfStream = new MemoryStream();
            document.GeneratePdf(pdfStream);
            pdfStream.Position = 0;
            string spbuNo = model.SpbuNo?.Replace(" ", "") ?? "SPBU";
            string tanggalAudit = model.TanggalAudit?.ToString("yyyyMMdd") ?? "00000000";
            string fileName = $"audit_{spbuNo}_{tanggalAudit}.pdf";

            return File(pdfStream, "application/pdf", fileName);

        }

        //[HttpGet]
        //public async Task<IActionResult> GenerateAllVerifiedPdfReports()
        //{
        //    try
        //    {
        //        var outputDirectory = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", "reports");
        //        if (!Directory.Exists(outputDirectory))
        //            Directory.CreateDirectory(outputDirectory);

        //        // ✅ Gunakan EF Core langsung agar koneksi tidak conflict dengan GetDetailReportAsync
        //        var auditIds = await _context.trx_audits
        //            .Where(a => a.status == "VERIFIED")
        //            .OrderByDescending(a => a.audit_execution_time)
        //            .Take(10)
        //            .Select(a => a.id)
        //            .ToListAsync();

        //        foreach (var id in auditIds)
        //        {
        //            var model = await GetDetailReportAsync2(Guid.Parse(id));

        //            if (model == null)
        //                continue;

        //            var document = new ReportExcellentTemplate(model); // ganti logic jika perlu
        //            await using var pdfStream = new MemoryStream();
        //            document.GeneratePdf(pdfStream);
        //            pdfStream.Position = 0;

        //            string spbuNo = model.SpbuNo?.Replace(" ", "") ?? "SPBU";
        //            string tanggalAudit = model.TanggalAudit?.ToString("yyyyMMdd") ?? "00000000";
        //            string fileName = $"audit_{spbuNo}_{tanggalAudit}.pdf";
        //            string fullPath = Path.Combine(outputDirectory, fileName);

        //            await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        //            await pdfStream.CopyToAsync(fileStream);
        //        }

        //        return Ok(new { message = "PDF generation completed"});
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
        //    }
        //}

        [HttpGet]
        public async Task<IActionResult> GenerateAllVerifiedPdfReports()
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromHours(6));
            var token = CancellationTokenSource
                .CreateLinkedTokenSource(timeoutCts.Token, HttpContext.RequestAborted)
                .Token;

            try
            {
                var outputDirectory = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", "reports");
                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                var auditIds = await _context.trx_audits
                    .Where(a => a.status == "VERIFIED" && a.audit_type != "Basic Operational" && a.report_file_excellent == null)
                    .OrderByDescending(a => a.audit_execution_time)
                    //.Take(10)
                    .Select(a => a.id)
                    .ToListAsync(token);  // <- inject token

                foreach (var id in auditIds)
                {
                    token.ThrowIfCancellationRequested();

                    var model = await GetDetailReportAsync2(Guid.Parse(id));
                    if (model == null)
                        continue;

                    var document = new ReportExcellentTemplate(model);
                    await using var pdfStream = new MemoryStream();
                    document.GeneratePdf(pdfStream);
                    pdfStream.Position = 0;

                    string spbuNo = model.SpbuNo?.Replace(" ", "") ?? "SPBU";
                    string tanggalAudit = model.TanggalAudit?.ToString("yyyyMMdd") ?? "00000000";
                    string idSuffix = id.Replace("-", "");
                    idSuffix = idSuffix.Substring(idSuffix.Length - 6);
                    string fileName = $"report_excellent_{spbuNo}_{tanggalAudit}_{idSuffix}.pdf";
                    string fullPath = Path.Combine(outputDirectory, fileName);


                    await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                    await pdfStream.CopyToAsync(fileStream, token);

                    await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE trx_audit SET report_file_excellent = {0} WHERE id = {1}",
                    fileName, id.ToString());
                }

                return Ok(new { message = "PDF generation completed" });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(408, new { error = "Operation timed out or request was cancelled." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GenerateScores(int batchSize = 200)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromHours(6));
            var token = CancellationTokenSource
                .CreateLinkedTokenSource(timeoutCts.Token, HttpContext.RequestAborted)
                .Token;

            try
            {
                // 1) Ambil list ID dulu (materialize) agar EF selesai memakai koneksinya
                _context.ChangeTracker.AutoDetectChangesEnabled = false;

                var ids = await _context.trx_audits
                    .AsNoTracking()
                    .Where(a => a.status == "VERIFIED" && a.audit_type != "Basic Operational")
                    .OrderByDescending(a => a.audit_execution_time)
                    .ThenByDescending(a => a.id)
                    .Select(a => a.id)
                    .ToListAsync(token); // <= materialize: koneksi EF bebas setelah ini

                // 2) Pakai koneksi baru, dedicated untuk batch (jangan reuse EF connection)
                var cs = _context.Database.GetConnectionString(); // EF Core 5+
                await using var conn = new Npgsql.NpgsqlConnection(cs);
                await conn.OpenAsync(token);

                const int dapperCmdTimeout = 3000;

                // 3) Proses per-batch (sekuensial)
                for (int i = 0; i < ids.Count; i += batchSize)
                {
                    var chunk = ids.Skip(i).Take(batchSize).ToList();
                    await ProcessBatchAsync(conn, chunk, dapperCmdTimeout, token);
                }

                return Ok(new { message = "Score update completed", processed = ids.Count });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(408, new { error = "Operation timed out or request was cancelled." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }

        // Tidak berubah banyak, tapi pastikan semua QueryAsync di-ToList() / AsList() sebelum lanjut.
        private async Task ProcessBatchAsync(DbConnection conn, List<string> ids, int cmdTimeoutSeconds, CancellationToken token)
        {
            await using var tran = await conn.BeginTransactionAsync(token);

            foreach (var auditId in ids)
            {
                const string sqlChecklist = @"
        SELECT 
            mqd.weight, 
            tac.score_input, 
            tac.score_x, 
            mqd.is_relaksasi
        FROM master_questioner_detail mqd
        LEFT JOIN trx_audit_checklist tac 
            ON tac.master_questioner_detail_id = mqd.id 
            AND tac.trx_audit_id = @id
        WHERE mqd.master_questioner_id = (
            SELECT master_questioner_checklist_id 
            FROM trx_audit 
            WHERE id = @id
        )
        AND mqd.type = 'QUESTION'";

                // Penting: materialize hasil Dapper sepenuhnya
                var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(
                        sqlChecklist, new { id = auditId }, transaction: tran, commandTimeout: cmdTimeoutSeconds))
                    .AsList();

                decimal sumAF = 0, sumWeight = 0, sumX = 0;

                foreach (var item in checklist)
                {
                    decimal w = item.weight ?? 0;
                    string input = item.score_input?.Trim().ToUpperInvariant() ?? "";

                    if (input == "X")
                    {
                        sumX += w;
                        sumAF += item.score_x ?? 0;
                    }
                    else if (input == "F" && item.is_relaksasi == true)
                    {
                        sumAF += 1.00m * w;
                    }
                    else
                    {
                        decimal af = input switch
                        {
                            "A" => 1.00m,
                            "B" => 0.80m,
                            "C" => 0.60m,
                            "D" => 0.40m,
                            "E" => 0.20m,
                            "F" => 0.00m,
                            _ => 0.00m
                        };
                        sumAF += af * w;
                    }

                    sumWeight += w;
                }

                decimal finalScore = (sumWeight - sumX) > 0
                    ? (sumAF / (sumWeight - sumX)) * sumWeight
                    : 0m;

                // Hitung compliance (pastikan semua helper ini TIDAK membuat koneksi baru sendiri).
                var checklistData = await GetChecklistDataAsync(conn, auditId);
                var mediaList = await GetMediaPerNodeAsync(conn, auditId);
                var elements = BuildHierarchy(checklistData, mediaList);
                foreach (var element in elements) AssignWeightRecursive(element);
                CalculateChecklistScores(elements);
                var modelstotal = new DetailReportViewModel { Elements = elements };
                CalculateOverallScore(modelstotal, checklistData);
                decimal? totalScore = modelstotal.TotalScore; 
                var compliance = HitungComplianceLevelDariElements(elements); 
                decimal scoress = Math.Round((decimal)totalScore, 2);

                const string sqlUpdate = @"UPDATE trx_audit SET score = @score WHERE id = @id";
                await conn.ExecuteAsync(sqlUpdate, new { score = scoress, id = auditId },
                    transaction: tran, commandTimeout: cmdTimeoutSeconds);
            }

            await tran.CommitAsync(token);
        }


        [HttpGet]
        public async Task<IActionResult> GenerateAllVerifiedPdfReportsGood()
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromHours(6));
            var token = CancellationTokenSource
                .CreateLinkedTokenSource(timeoutCts.Token, HttpContext.RequestAborted)
                .Token;

            try
            {
                var outputDirectory = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", "reports");
                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                var auditIds = await _context.trx_audits
                    .Where(a => a.status == "VERIFIED" && a.audit_type != "Basic Operational" && a.report_file_good == null)
                    .OrderByDescending(a => a.audit_execution_time)
                    //.Take(10)
                    .Select(a => a.id)
                    .ToListAsync(token);  // <- inject token

                foreach (var id in auditIds)
                {
                    token.ThrowIfCancellationRequested();

                    var model = await GetDetailReportAsync2(Guid.Parse(id));
                    if (model == null)
                        continue;

                    var document = new ReportGoodTemplate(model);
                    await using var pdfStream = new MemoryStream();
                    document.GeneratePdf(pdfStream);
                    pdfStream.Position = 0;

                    string spbuNo = model.SpbuNo?.Replace(" ", "") ?? "SPBU";
                    string tanggalAudit = model.TanggalAudit?.ToString("yyyyMMdd") ?? "00000000";
                    string idSuffix = id.Replace("-", "");
                    idSuffix = idSuffix.Substring(idSuffix.Length - 6);
                    string fileName = $"report_good_{spbuNo}_{tanggalAudit}_{idSuffix}.pdf";
                    string fullPath = Path.Combine(outputDirectory, fileName);

                    await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                    await pdfStream.CopyToAsync(fileStream, token);

                    await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE trx_audit SET report_file_good = {0} WHERE id = {1}",
                    fileName, id.ToString());
                }

                return Ok(new { message = "PDF generation completed" });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(408, new { error = "Operation timed out or request was cancelled." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }

        private async Task<DetailReportViewModel> GetDetailReportAsync(Guid id)
        {
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var auditHeader = await GetAuditHeaderAsync(conn, id.ToString());
            if (auditHeader == null)
                throw new Exception("Data tidak ditemukan.");

            var model = MapToViewModel(auditHeader);
            model.FinalDocuments = await GetMediaNotesAsync(conn, id.ToString(), "FINAL");
            model.QqChecks = await GetQqCheckDataAsync(conn, id.ToString());

            var checklistData = await GetChecklistDataAsync(conn, id.ToString());
            var mediaList = await GetMediaPerNodeAsync(conn, id.ToString());
            model.Elements = BuildHierarchy(checklistData, mediaList);
            model.FotoTemuan = await GetMediaReportFAsync(conn, id.ToString());
            _logger.LogInformation("FotoTemuan: {Path}", model.FotoTemuan);

            foreach (var element in model.Elements)
                AssignWeightRecursive(element);

            CalculateChecklistScores(model.Elements);

            // Hitung final score seperti Index
            var scoreSql = @"
SELECT mqd.weight, tac.score_input, mqd.is_relaksasi
FROM master_questioner_detail mqd
LEFT JOIN trx_audit_checklist tac 
    ON tac.master_questioner_detail_id = mqd.id 
    AND tac.trx_audit_id = @id
WHERE mqd.master_questioner_id = (
    SELECT master_questioner_checklist_id 
    FROM trx_audit 
    WHERE id = @id
)
AND mqd.type = 'QUESTION'";

            var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, bool? is_relaksasi)>(scoreSql, new { id = id.ToString() })).ToList();

            decimal totalScore = 0, maxScore = 0;
            foreach (var item in checklist)
            {
                decimal w = item.weight ?? 0;
                decimal v = item.is_relaksasi == true
                    ? 1.00m
                    : item.score_input switch
                    {
                        "A" => 1.00m,
                        "B" => 0.80m,
                        "C" => 0.60m,
                        "D" => 0.40m,
                        "E" => 0.20m,
                        "F" => 0.00m,
                        _ => 0.00m
                    };
                totalScore += v * w;
                maxScore += w;
            }

            decimal finalScore = maxScore > 0 ? totalScore / maxScore * 100 : 0m;
            model.Score = finalScore;

            // Penalty
            var penaltySql = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));";

            model.PenaltyAlerts = await conn.ExecuteScalarAsync<string>(penaltySql, new { id = id.ToString() });

            var penaltySqlGood = @"
SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
FROM trx_audit_checklist tac
INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
WHERE 
    tac.trx_audit_id = @id AND
    tac.score_input = 'F' AND
    mqd.is_penalty = true AND 
    (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';";
            model.PenaltyAlertsGood = await conn.ExecuteScalarAsync<string>(penaltySqlGood, new { id = id.ToString() });

            // Hitung compliance dan simpan ke model
            CalculateOverallScore(model, checklistData);
            // Hitung compliance level
            var compliance = HitungComplianceLevelDariElements(model.Elements);
            model.SSS = Math.Round(compliance.SSS ?? 0, 2);
            model.EQnQ = Math.Round(compliance.EQnQ ?? 0, 2);
            model.RFS = Math.Round(compliance.RFS ?? 0, 2);
            model.VFC = Math.Round(compliance.VFC ?? 0, 2);
            model.EPO = Math.Round(compliance.EPO ?? 0, 2);

            // Sertifikasi default
            model.GoodStatus = "NOT CERTIFIED";
            model.ExcellentStatus = "NOT CERTIFIED";

            // Ambil execution time untuk TanggalAudit
            var executionTimeSql = @"SELECT audit_execution_time FROM trx_audit WHERE id = @id";
            model.TanggalAudit = await conn.ExecuteScalarAsync<DateTime?>(
    executionTimeSql, new { id = id.ToString() });

            // Validasi status berdasarkan compliance + penalty
            bool failGood = model.SSS < 80 || model.EQnQ < 85 || model.RFS < 85 || model.VFC < 15 || model.EPO < 25;
            bool failExcellent = model.SSS < 85 || model.EQnQ < 85 || model.RFS < 85 || model.VFC < 20 || model.EPO < 50;
            bool hasExcellentPenalty = !string.IsNullOrEmpty(model.PenaltyAlerts);
            bool hasGoodPenalty = !string.IsNullOrEmpty(model.PenaltyAlertsGood);

            if (model.FinalScore >= 75 && !hasGoodPenalty && !failGood)
                model.GoodStatus = "CERTIFIED";

            if (model.FinalScore >= 80 && !hasExcellentPenalty && !failExcellent)
                model.ExcellentStatus = "CERTIFIED";

            // Audit Next dan kelas SPBU
            string goodStatus = model.GoodStatus;
            string excellentStatus = model.ExcellentStatus;
            string auditNext = null;
            string levelspbu = null;

            var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
            var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = model.AuditCurrent });

            if (auditFlow != null)
            {
                string passedGood = auditFlow.passed_good;
                string passedExcellent = auditFlow.passed_excellent;
                string passedAuditLevel = auditFlow.passed_audit_level;
                string failed_audit_level = auditFlow.failed_audit_level;

                if (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                    auditNext = passedExcellent;
                else if (goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                    auditNext = passedGood;
                else
                    auditNext = failed_audit_level;

                string passedLevel = auditFlow.passed_audit_level;

                if (auditNext == null)
                {
                    auditNext = passedLevel;
                }

                var auditlevelClassSql = @"SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                var auditlevelClass = await conn.QueryFirstOrDefaultAsync<dynamic>(auditlevelClassSql, new { level = auditNext });
                levelspbu = auditlevelClass != null ? (auditlevelClass.audit_level_class ?? "") : "";
            }

            var createdDate = await _context.TrxFeedbacks
            .Where(tf => tf.TrxAuditId == id.ToString())
            .OrderBy(tf => tf.CreatedDate)
            .Select(tf => tf.CreatedDate)
            .FirstOrDefaultAsync();

            model.CreatedDateBanding = createdDate;

            model.AuditNext = auditNext;
            model.ClassSPBU = levelspbu;

            return model;
        }

        private async Task<DetailReportViewModel> GetDetailReportAsync2(Guid id)
        {
            await using var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            var idStr = id.ToString();

            var auditHeader = await GetAuditHeaderAsync(conn, idStr);
            if (auditHeader == null)
                throw new Exception("Data tidak ditemukan.");

            var model = MapToViewModel(auditHeader);
            model.FinalDocuments = await GetMediaNotesAsync(conn, idStr, "FINAL");
            model.QqChecks = await GetQqCheckDataAsync(conn, idStr);

            var checklistData = await GetChecklistDataAsync(conn, idStr);
            var mediaList = await GetMediaPerNodeAsync(conn, idStr);
            model.Elements = BuildHierarchy(checklistData, mediaList);
            model.FotoTemuan = await GetMediaReportFAsync(conn, idStr);
            _logger.LogInformation("FotoTemuan: {Path}", model.FotoTemuan);

            foreach (var element in model.Elements)
                AssignWeightRecursive(element);

            CalculateChecklistScores(model.Elements);

            var scoreSql = @"
SELECT mqd.weight, tac.score_input, mqd.is_relaksasi
FROM master_questioner_detail mqd
LEFT JOIN trx_audit_checklist tac 
    ON tac.master_questioner_detail_id = mqd.id 
    AND tac.trx_audit_id = @id
WHERE mqd.master_questioner_id = (
    SELECT master_questioner_checklist_id 
    FROM trx_audit 
    WHERE id = @id
)
AND mqd.type = 'QUESTION'";
            var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, bool? is_relaksasi)>(scoreSql, new { id = idStr })).ToList();

            decimal totalScore = 0, maxScore = 0;
            foreach (var item in checklist)
            {
                decimal w = item.weight ?? 0;
                decimal v = item.is_relaksasi == true
                    ? 1.00m
                    : item.score_input switch
                    {
                        "A" => 1.00m,
                        "B" => 0.80m,
                        "C" => 0.60m,
                        "D" => 0.40m,
                        "E" => 0.20m,
                        "F" => 0.00m,
                        _ => 0.00m
                    };
                totalScore += v * w;
                maxScore += w;
            }

            decimal finalScore = maxScore > 0 ? totalScore / maxScore * 100 : 0m;
            model.Score = finalScore;

            // Penalty
            model.PenaltyAlerts = await conn.ExecuteScalarAsync<string>(@"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));", new { id = idStr });

            model.PenaltyAlertsGood = await conn.ExecuteScalarAsync<string>(@"
            SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
            FROM trx_audit_checklist tac
            INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
            WHERE 
                tac.trx_audit_id = @id AND
                tac.score_input = 'F' AND
                mqd.is_penalty = true AND 
                (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b';", new { id = idStr });

            CalculateOverallScore(model, checklistData);
            var compliance = HitungComplianceLevelDariElements(model.Elements);
            model.SSS = Math.Round(compliance.SSS ?? 0, 2);
            model.EQnQ = Math.Round(compliance.EQnQ ?? 0, 2);
            model.RFS = Math.Round(compliance.RFS ?? 0, 2);
            model.VFC = Math.Round(compliance.VFC ?? 0, 2);
            model.EPO = Math.Round(compliance.EPO ?? 0, 2);

            model.GoodStatus = "NOT CERTIFIED";
            model.ExcellentStatus = "NOT CERTIFIED";

            model.TanggalAudit = await conn.ExecuteScalarAsync<DateTime?>(
                @"SELECT audit_execution_time FROM trx_audit WHERE id = @id", new { id = idStr });

            bool failGood = model.SSS < 80 || model.EQnQ < 85 || model.RFS < 85 || model.VFC < 15 || model.EPO < 25;
            bool failExcellent = model.SSS < 85 || model.EQnQ < 85 || model.RFS < 85 || model.VFC < 20 || model.EPO < 50;
            bool hasExcellentPenalty = !string.IsNullOrEmpty(model.PenaltyAlerts);
            bool hasGoodPenalty = !string.IsNullOrEmpty(model.PenaltyAlertsGood);

            if (finalScore >= 75 && !hasGoodPenalty && !failGood)
                model.GoodStatus = "CERTIFIED";

            if (finalScore >= 80 && !hasExcellentPenalty && !failExcellent)
                model.ExcellentStatus = "CERTIFIED";

            var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
            var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = model.AuditCurrent });

            string auditNext = null, levelspbu = null;
            if (auditFlow != null)
            {
                if (model.GoodStatus == "CERTIFIED" && model.ExcellentStatus == "CERTIFIED")
                    auditNext = auditFlow.passed_excellent;
                else if (model.GoodStatus == "CERTIFIED")
                    auditNext = auditFlow.passed_good;
                else
                    auditNext = auditFlow.failed_audit_level;

                if (auditNext == null)
                    auditNext = auditFlow.passed_audit_level;

                var auditLevelClass = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1;",
                    new { level = auditNext });

                levelspbu = auditLevelClass?.audit_level_class ?? "";
            }

            var createdDate = await _context.TrxFeedbacks
            .Where(tf => tf.TrxAuditId == id.ToString())
            .OrderBy(tf => tf.CreatedDate)
            .Select(tf => tf.CreatedDate)
            .FirstOrDefaultAsync();

            model.CreatedDateBanding = createdDate;


            model.AuditNext = auditNext;
            model.ClassSPBU = levelspbu;

            return model;
        }

        private async Task<string> RenderViewToStringAsync(string viewName, object model)
        {
            var viewEngine = HttpContext.RequestServices.GetService<IRazorViewEngine>();
            var tempDataProvider = HttpContext.RequestServices.GetService<ITempDataProvider>();
            var serviceProvider = HttpContext.RequestServices.GetService<IServiceProvider>();

            var actionContext = new ActionContext(HttpContext, RouteData, ControllerContext.ActionDescriptor);

            using var sw = new StringWriter();
            var viewResult = viewEngine.FindView(actionContext, viewName, false);

            if (!viewResult.Success)
                throw new InvalidOperationException($"View {viewName} not found");

            var viewContext = new ViewContext(
                actionContext,
                viewResult.View,
                new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model },
                new TempDataDictionary(HttpContext, tempDataProvider),
                sw,
                new HtmlHelperOptions()
            );

            await viewResult.View.RenderAsync(viewContext);
            return sw.ToString();
        }


        private async Task<AuditHeaderDto> GetAuditHeaderAsync(IDbConnection conn, string id)
        {
            string sql = @"
    WITH RECURSIVE
question_hierarchy AS (
    SELECT
        mqd.id,
        mqd.title,
        mqd.parent_id,
        mqd.title AS root_title
    FROM master_questioner_detail mqd
    WHERE mqd.title IN ('Elemen 1','Elemen 2','Elemen 3','Elemen 4','Elemen 5')
    UNION ALL
    SELECT
        mqd.id,
        mqd.title,
        mqd.parent_id,
        qh.root_title
    FROM master_questioner_detail mqd
    INNER JOIN question_hierarchy qh ON mqd.parent_id = qh.id
),
penalty_flags AS (
    SELECT
        tac.id                           AS tac_id,
        tac.trx_audit_id,
        tac.master_questioner_detail_id  AS mqd_id,
        tac.score_input,
        tac.comment,
        ta.created_date,
        mqd.is_penalty,
        mqd.is_relaksasi,
        mqd.penalty_excellent_criteria,
        (
              (
                  tac.master_questioner_detail_id IN (
                      '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                      'bafc206f-ed29-4bbc-8053-38799e186fb0',
                      'd26f4caa-e849-4ab4-9372-298693247272'
                  )
                  AND tac.score_input <> 'A'
              )
           OR (
                  tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                  AND ta.created_date < DATE '2025-06-01'
                  AND tac.score_input <> 'A'
              )
           OR (
                  (
                      (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A')
                   OR (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                  )
                  AND COALESCE(mqd.is_relaksasi, false) = false
                  AND mqd.is_penalty = true
                  AND NOT (
                      mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                      AND ta.created_date >= DATE '2025-06-01'
                      AND tac.comment IS NOT NULL
                      AND btrim(tac.comment) <> ''
                  )
              )
        ) AS is_penalty_excellent,
        (
            tac.score_input = 'F'
            AND mqd.is_penalty = true
            AND COALESCE(mqd.is_relaksasi, false) = false
            AND mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
        ) AS is_penalty_good,
        (
            mqd.is_penalty = true
            AND mqd.is_relaksasi = true
            AND (
                    (
                        (
                            tac.master_questioner_detail_id IN (
                                '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                                'bafc206f-ed29-4bbc-8053-38799e186fb0',
                                'd26f4caa-e849-4ab4-9372-298693247272'
                            )
                            AND tac.score_input <> 'A'
                        )
                        OR (
                            tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                            AND ta.created_date < DATE '2025-06-01'
                            AND tac.score_input <> 'A'
                        )
                        OR (
                            (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A')
                            OR (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                        )
                    )
                    OR (tac.score_input = 'F' AND mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b')
                )
        ) AS is_penalty_relaksasi
    FROM trx_audit_checklist tac
    JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
    JOIN trx_audit ta ON ta.id = tac.trx_audit_id
    WHERE tac.trx_audit_id = @id
),
comment_per_elemen AS (
    SELECT
        qh.root_title,
        string_agg(
            (
                regexp_replace(pf.comment, E'[\\n\\r]+', ' ', 'g')
                ||
                CASE
                    WHEN (pf.is_penalty_excellent OR pf.is_penalty_good OR pf.is_penalty_relaksasi) THEN
                        ' - ' ||
                        trim(
                            BOTH ' / '
                            FROM concat_ws(
                                ' / ',
                                CASE WHEN pf.is_penalty_excellent THEN 'penalty excellent' END,
                                CASE WHEN pf.is_penalty_good THEN 'penalty good' END,
                                CASE WHEN pf.is_penalty_relaksasi THEN '*relaksasi*' END
                            )
                        )
                    ELSE ''
                END
            ),
            E'\n' ORDER BY pf.tac_id
        ) AS all_comments
    FROM question_hierarchy qh
    JOIN penalty_flags pf ON pf.mqd_id = qh.id
    WHERE pf.comment IS NOT NULL AND btrim(pf.comment) <> ''
    GROUP BY qh.root_title
)
SELECT
    ta.id,
    ta.report_no              AS ReportNo,
    ta.audit_type             AS AuditType,
    ta.audit_execution_time   AS SubmitDate,
    ta.status,
    ta.audit_mom_final        AS Notes,
    ta.audit_level,
    s.spbu_no                 AS SpbuNo,
    s.region,
    s.city_name               AS Kota,
    s.address                 AS Alamat,
    s.owner_name              AS OwnerName,
    s.manager_name            AS ManagerName,
    s.owner_type              AS OwnershipType,
    s.quater                  AS Quarter,
    s.""year""                  AS Year,
    s.mor                     AS Mor,
    s.sales_area              AS SalesArea,
    s.sbm                     AS Sbm,
    s.""level""                 AS ClassSpbu,
    s.phone_number_1          AS Phone,
    (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 1') AS KomentarStaf,
    (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 2') AS KomentarQuality,
    (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 3') AS KomentarHSSE,
    (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 4') AS KomentarVisual,
    (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 5') AS PenawaranKomperhensif,
    CASE
        WHEN audit_mom_final IS NOT NULL AND audit_mom_final <> '' THEN audit_mom_final
        ELSE audit_mom_intro
    END AS KomentarManager,
    approval_date         AS ApproveDate,
    (SELECT name FROM app_user WHERE username = ta.approval_by) AS ApproveBy,
    ta.updated_date       AS UpdateDate,
    ta.audit_level        AS AuditCurrent,
    s.audit_next          AS AuditNext,
    au.name               AS NamaAuditor,
    COALESCE(
            (SELECT name 
             FROM app_user 
             WHERE id = ta.app_user_id_auditor2
             LIMIT 1),
            '-'
        ) AS NamaAuditor2
    FROM trx_audit ta
    JOIN spbu s   ON ta.spbu_id = s.id
    JOIN app_user au ON au.id = ta.app_user_id
    WHERE ta.id = @id;";

            var a = await conn.QueryFirstOrDefaultAsync<AuditHeaderDto>(sql, new { id });
            if (a == null)
                return null;

            // --- Hitung finalScore seperti di Index ---
            var scoreSql = @"
        SELECT mqd.weight, tac.score_input, mqd.is_relaksasi
        FROM master_questioner_detail mqd
        LEFT JOIN trx_audit_checklist tac 
            ON tac.master_questioner_detail_id = mqd.id 
            AND tac.trx_audit_id = @id
        WHERE mqd.master_questioner_id = (
            SELECT master_questioner_checklist_id 
            FROM trx_audit 
            WHERE id = @id
        )
        AND mqd.type = 'QUESTION'";
            var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, bool? is_relaksasi)>(scoreSql, new { id = id.ToString() })).ToList();

            decimal totalScore = 0, maxScore = 0;
            foreach (var item in checklist)
            {
                decimal w = item.weight ?? 0;
                decimal v = item.is_relaksasi == true
                    ? 1.00m
                    : item.score_input switch
                    {
                        "A" => 1.00m,
                        "B" => 0.80m,
                        "C" => 0.60m,
                        "D" => 0.40m,
                        "E" => 0.20m,
                        "F" => 0.00m,
                        _ => 0.00m
                    };
                totalScore += v * w;
                maxScore += w;
            }

            decimal finalScore = maxScore > 0 ? totalScore / maxScore * 100 : 0m;
            a.score = finalScore; // simpan di model

            // Penalty queries
            var penaltyExcellentQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                INNER JOIN trx_audit ta ON ta.id = tac.trx_audit_id
                WHERE 
                tac.trx_audit_id = @id
                AND (
                    (
                        tac.master_questioner_detail_id IN (
                    '555fe2e4-b95b-461b-9c92-ad8b5c837119',
                    'bafc206f-ed29-4bbc-8053-38799e186fb0',
                    'd26f4caa-e849-4ab4-9372-298693247272'
                )
                AND tac.score_input <> 'A'
                )
                OR
                (
                tac.master_questioner_detail_id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                AND ta.created_date < '2025-06-01'
                AND tac.score_input <> 'A')
                OR
                (
                    (
                    (mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
                    (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')
                )
                AND (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL)
                AND mqd.is_penalty = true
                AND NOT (
                    mqd.id = '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'
                    AND ta.created_date >= '2025-06-01'
                )));";

            var penaltyGoodQuery = @"
        SELECT STRING_AGG(mqd.penalty_alert, ', ')
        FROM trx_audit_checklist tac
        INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
        WHERE tac.trx_audit_id = @id AND
              tac.score_input = 'F' AND
              mqd.is_penalty = true AND 
              (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL) and mqd.id <> '5e9ffc47-de99-4d7d-b8bc-0fb9b7acc81b'";

            var penaltyExcellentResult = await conn.ExecuteScalarAsync<string>(penaltyExcellentQuery, new { id });
            var penaltyGoodResult = await conn.ExecuteScalarAsync<string>(penaltyGoodQuery, new { id });

            bool hasExcellentPenalty = !string.IsNullOrEmpty(penaltyExcellentResult);
            bool hasGoodPenalty = !string.IsNullOrEmpty(penaltyGoodResult);

            string goodStatus = (finalScore >= 75 && !hasGoodPenalty) ? "CERTIFIED" : "NOT CERTIFIED";
            string excellentStatus = (finalScore >= 80 && !hasExcellentPenalty) ? "CERTIFIED" : "NOT CERTIFIED";

            // Ambil nilai dari ketiga kolom dulu
            var flowSql = @"
            SELECT passed_good, passed_excellent, passed_audit_level 
            FROM master_audit_flow 
            WHERE audit_level = (select s.audit_current from trx_audit ta 
            join spbu s on ta.spbu_id  = s.id 
            where ta.id = @id) 
            LIMIT 1";

            var flow = await conn.QueryFirstOrDefaultAsync<dynamic>(flowSql, new { id });

            string column;

            // Cek apakah passed_good dan passed_excellent kosong/null
            //if (string.IsNullOrWhiteSpace((string)flow?.passed_good) && string.IsNullOrWhiteSpace((string)flow?.passed_excellent))
            //{
            //    column = "passed_audit_level";
            //}
            //else
            //{
            //    column = (goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED") ? "passed_good"
            //           : (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED") ? "passed_excellent"
            //           : (goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED") ? "failed_audit_level"
            //           : "passed_audit_level";
            //}

            //// Gunakan kolom yang dipilih dalam query berikutnya
            //var auditNextQuery = $"SELECT {column} FROM master_audit_flow WHERE audit_level = @level LIMIT 1";

            //var auditNext = await conn.ExecuteScalarAsync<string>(auditNextQuery, new { level = a.AuditCurrent });

            //// Isi ke DTO
            //a.AuditNext = auditNext;

            return a;
        }

        private DetailReportViewModel MapToViewModel(AuditHeaderDto basic)
        {
            return new DetailReportViewModel
            {
                AuditId = basic.Id,
                ReportNo = basic.ReportNo,
                AuditType = basic.AuditType == "Mystery Audit" ? "Regular Audit" : basic.AuditType,
                TanggalAudit = (basic.SubmitDate.GetValueOrDefault() == DateTime.MinValue)
                ? basic.UpdatedDate
                : basic.SubmitDate.GetValueOrDefault(),
                TanggalSubmit = basic.ApproveDate.GetValueOrDefault() == DateTime.MinValue
                ? basic.UpdatedDate
                : basic.ApproveDate.GetValueOrDefault(),
                Status = basic.Status,
                Notes = basic.Notes,
                SpbuNo = basic.SpbuNo,
                Region = basic.Region,
                Kota = basic.Kota,
                Alamat = basic.Alamat,
                OwnerName = basic.OwnerName,
                ManagerName = basic.ManagerName,
                OwnershipType = basic.OwnershipType,
                Quarter = basic.Quarter?.ToString() ?? "-",
                Year = basic.Year ?? DateTime.Now.Year,
                MOR = basic.Mor,
                SalesArea = basic.SalesArea,
                SBM = basic.Sbm,
                ClassSPBU = basic.ClassSpbu,
                Phone = basic.Phone,
                KomentarStaf = basic.KomentarStaf,
                KomentarQuality = basic.KomentarQuality,
                KomentarHSSE = basic.KomentarHSSE,
                KomentarVisual = basic.KomentarVisual,
                KomentarManager = basic.KomentarManager,
                PenawaranKomperhensif = basic.PenawaranKomperhensif,
                AuditCurrent = basic.AuditCurrent,
                AuditNext = basic.AuditNext,
                ApproveBy = basic.ApproveBy,
                NamaAuditor = basic.NamaAuditor,
                NamaAuditor2 = basic.NamaAuditor2
            };
        }

        private async Task<List<MediaItem>> GetMediaNotesAsync(IDbConnection conn, string id, string type)
        {
            string sql = @"SELECT media_type, media_path
                   FROM trx_audit_media
                   WHERE trx_audit_id = @id AND type = @type";

            var raw = await conn.QueryAsync<(string media_type, string media_path)>(sql, new { id, type });

            return raw.Select(x => new MediaItem
            {
                MediaType = x.media_type,
                MediaPath = "https://epas-assets.zarata.co.id" + x.media_path
            }).ToList();
        }

        private async Task<List<FotoTemuan>> GetMediaReportFAsync(IDbConnection conn, string id)
        {
            string sql = @"SELECT ac.""comment"" as title,am.media_path
                        FROM trx_audit_media am
                        JOIN trx_audit_checklist ac
                          ON am.trx_audit_id = ac.trx_audit_id
                          AND am.master_questioner_detail_id = ac.master_questioner_detail_id
                         join master_questioner_detail mqd on mqd.id  = ac.master_questioner_detail_id 
                        WHERE ac.score_input = 'F'
                          AND am.trx_audit_id = @id";

            var raw = await conn.QueryAsync<(string media_type, string media_path)>(sql, new { id });

            return raw.Select(x => new FotoTemuan
            {
                Caption = x.media_type,
                Path = x.media_path
            }).ToList();
        }

        private async Task<List<AuditQqCheckItem>> GetQqCheckDataAsync(IDbConnection conn, string id)
        {
            string sql = @"SELECT nozzle_number AS NozzleNumber,
                       du_make  AS DuMake,
                       du_serial_no AS DuSerialNo,
                       product  AS Product,
                       mode     AS Mode,
                       quantity_variation_with_measure AS QuantityVariationWithMeasure,
                       quantity_variation_in_percentage  AS QuantityVariationInPercentage,
                       observed_density      AS ObservedDensity,
                       observed_temp         AS ObservedTemp,
                       observed_density_15_degree   AS ObservedDensity15Degree,
                       reference_density_15_degree  AS ReferenceDensity15Degree,
                       tank_number  AS TankNumber,
                       density_variation AS DensityVariation
                FROM trx_audit_qq
                WHERE trx_audit_id = @id";
            var data = await conn.QueryAsync<AuditQqCheckItem>(sql, new { id });
            return data.ToList();
        }

        private async Task<List<ChecklistFlatItem>> GetChecklistDataAsync(IDbConnection conn, string id)
        {
            string sql = @"SELECT
                  mqd.id,
                  mqd.title,
                  mqd.description,
                  mqd.parent_id,
                  mqd.type,
                  mqd.weight,
                  mqd.score_option,
                  tac.score_input,
                  tac.score_af,
                  tac.score_x,
                  mqd.order_no,
                  mqd.is_relaksasi,
                  mqd.number
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac
                  ON tac.master_questioner_detail_id = mqd.id
                 AND tac.trx_audit_id = @id
                WHERE mqd.master_questioner_id = (
                  SELECT master_questioner_checklist_id
                  FROM trx_audit
                  WHERE id = @id
                )
                ORDER BY mqd.order_no";
            var data = await conn.QueryAsync<ChecklistFlatItem>(sql, new { id });
            return data.ToList();
        }

        private async Task<Dictionary<string, List<MediaItem>>> GetMediaPerNodeAsync(IDbConnection conn, string id)
        {
            string sql = @"SELECT master_questioner_detail_id, media_type, media_path
                   FROM trx_audit_media
                   WHERE trx_audit_id = @id
                     AND master_questioner_detail_id IS NOT NULL";

            var raw = await conn.QueryAsync<(string master_questioner_detail_id, string media_type, string media_path)>(
                sql,
                new { id },
                commandTimeout: 60000
            );

            return raw.GroupBy(x => x.master_questioner_detail_id)
                      .ToDictionary(
                          g => g.Key,
                          g => g.Select(m => new MediaItem
                          {
                              MediaType = m.media_type,
                              MediaPath = "https://epas-assets.zarata.co.id" + m.media_path
                          }).ToList()
                      );
        }

        // Checklist data: support optional transaction
        private async Task<List<ChecklistFlatItem>> GetChecklistDataAsync2(
            IDbConnection conn,
            string id,
            IDbTransaction tx = null)
        {
            const string sql = @"
        SELECT
            mqd.id,
            mqd.title,
            mqd.description,
            mqd.parent_id,
            mqd.type,
            mqd.weight,
            mqd.score_option,
            tac.score_input,
            tac.score_af,
            tac.score_x,
            mqd.order_no,
            mqd.is_relaksasi,
            mqd.number
        FROM master_questioner_detail mqd
        LEFT JOIN trx_audit_checklist tac
               ON tac.master_questioner_detail_id = mqd.id
              AND tac.trx_audit_id = @id
        WHERE mqd.master_questioner_id = (
              SELECT master_questioner_checklist_id
              FROM trx_audit
              WHERE id = @id
        )
        ORDER BY mqd.order_no";

            var data = await conn.QueryAsync<ChecklistFlatItem>(
                sql,
                new { id },
                transaction: tx,
                commandTimeout: 6000 // 10 menit
            );

            return data.ToList();
        }

        // Media per node: support optional transaction
        private async Task<Dictionary<string, List<MediaItem>>> GetMediaPerNodeAsync2(
            IDbConnection conn,
            string id,
            IDbTransaction tx = null)
        {
            const string sql = @"
        SELECT master_questioner_detail_id, media_type, media_path
        FROM trx_audit_media
        WHERE trx_audit_id = @id
          AND master_questioner_detail_id IS NOT NULL";

            var raw = await conn.QueryAsync<(string master_questioner_detail_id, string media_type, string media_path)>(
                sql,
                new { id },
                transaction: tx,
                commandTimeout: 60000 // 10 menit
            );

            // Normalisasi base URL (hindari double slash)
            const string BASE = "https://epas-assets.zarata.co.id";
            string JoinUrl(string baseUrl, string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return baseUrl;
                // pastikan hanya satu slash pemisah
                if (baseUrl.EndsWith("/")) baseUrl = baseUrl.TrimEnd('/');
                if (!path.StartsWith("/")) path = "/" + path;
                return baseUrl + path;
            }

            return raw
                .GroupBy(x => x.master_questioner_detail_id)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(m => new MediaItem
                    {
                        MediaType = m.media_type,
                        MediaPath = JoinUrl(BASE, m.media_path)
                    }).ToList()
                );
        }


        void RecalcScoreAFWeighted(AuditChecklistNode node)
        {
            if (node.Children != null && node.Children.Any())
            {
                // Rekursif ke setiap anak
                foreach (var child in node.Children)
                    RecalcScoreAFWeighted(child);

                // Hitung total bobot dan nilai total
                var validChildren = node.Children.Where(c => c.ScoreAF.HasValue && c.Weight > 0).ToList();
                var totalWeight = validChildren.Sum(c => c.Weight);

                if (totalWeight > 0)
                {
                    var weightedScore = validChildren.Sum(c => c.ScoreAF.Value * c.Weight);
                    node.ScoreAF = weightedScore / totalWeight;
                }
                else
                {
                    node.ScoreAF = null;
                }
            }
            else
            {
                // Leaf node (pertanyaan): ambil dari ScoreInput (misal "80" artinya 0.8)
                if (decimal.TryParse(node.ScoreInput, out var parsedScore))
                {
                    node.ScoreAF = parsedScore / 100m;
                }
                else
                {
                    node.ScoreAF = null;
                }
            }
        }


        private void CalculateChecklistScores(List<AuditChecklistNode> nodes)
        {
            var nilaiAF = new Dictionary<string, decimal>
            {
                ["A"] = 1.00m,
                ["B"] = 0.80m,
                ["C"] = 0.60m,
                ["D"] = 0.40m,
                ["E"] = 0.20m,
                ["F"] = 0.00m
            };

            void HitungScore(AuditChecklistNode node)
            {
                if (node.Children == null || node.Children.Count == 0)
                {
                    if (node.Type == "QUESTION")
                    {
                        var input = node.ScoreInput?.Trim().ToUpper();
                        var allowed = (node.ScoreOption ?? "")
                                        .Split('/', StringSplitOptions.RemoveEmptyEntries)
                                        .SelectMany(opt =>
                                            opt == "A-F"
                                                ? new[] { "A", "B", "C", "D", "E", "F" }
                                                : new[] { opt.Trim() })
                                        .Concat(new[] { "X" })
                                        .Select(x => x.Trim())
                                        .Distinct()
                                        .ToList();

                        if (!string.IsNullOrWhiteSpace(input) && allowed.Contains(input) && input != "X")
                        {
                            if (nilaiAF.TryGetValue(input, out var val))
                            {
                                node.ScoreAF = val;
                            }
                        }

                        // override value ScoreAF for F Relaksasi
                        if (node.IsRelaksasi == true && input == "F")
                        {
                            node.ScoreAF = 1.00m;
                        }
                    }
                }
                else
                {
                    decimal total = 0, bobot = 0;
                    foreach (var child in node.Children)
                    {
                        HitungScore(child);
                        var w = child.Weight ?? 1m;
                        if (child.ScoreAF.HasValue)
                        {
                            total += child.ScoreAF.Value * w;
                            bobot += w;
                        }
                    }

                    node.ScoreAF = bobot > 0 ? total / bobot : null;
                }
            }

            foreach (var root in nodes)
                HitungScore(root);
        }

        public decimal HitungFinalScore(List<ChecklistFlatSum> flatItems)
        {
            var nilaiAF = new Dictionary<string, decimal>
            {
                ["A"] = 1.00m,
                ["B"] = 0.80m,
                ["C"] = 0.60m,
                ["D"] = 0.40m,
                ["E"] = 0.20m,
                ["F"] = 0.00m
            };

            decimal totalScore = 0m;
            decimal totalWeight = 0m;

            var groupedByElement = flatItems
                .GroupBy(x => x.RootElementTitle?.Trim().ToUpperInvariant())
                .ToList();

            foreach (var elementGroup in groupedByElement)
            {
                string element = elementGroup.Key;
                bool isSpecial = element == "ELEMEN 2" || element == "ELEMEN 5";

                if (isSpecial)
                {
                    var groupedByParent = elementGroup
                        .GroupBy(x => x.ParentId)
                        .ToList();

                    foreach (var subGroup in groupedByParent)
                    {
                        decimal sumAF = 0, sumW = 0, sumX = 0;

                        foreach (var item in subGroup)
                        {
                            string input = item.ScoreInput?.Trim().ToUpper() ?? "";
                            decimal w = item.Weight ?? 0;

                            if (input == "X")
                            {
                                sumX += w;
                                sumAF += item.ScoreX ?? 0;
                            }
                            else if (input == "F" && item.IsRelaksasi == true)
                            {
                                sumAF += 1.00m * w;
                            }
                            else if (nilaiAF.TryGetValue(input, out var af))
                            {
                                sumAF += af * w;
                            }

                            sumW += w;
                        }

                        if (sumW - sumX > 0)
                        {
                            decimal partialScore = (sumAF / (sumW - sumX)) * sumW;
                            totalScore += partialScore;
                            totalWeight += sumW;
                        }
                    }
                }
                else
                {
                    decimal sumAF = 0, sumW = 0, sumX = 0;

                    foreach (var item in elementGroup)
                    {
                        string input = item.ScoreInput?.Trim().ToUpper() ?? "";
                        decimal w = item.Weight ?? 0;

                        if (input == "X")
                        {
                            sumX += w;
                            sumAF += item.ScoreX ?? 0;
                        }
                        else if (input == "F" && item.IsRelaksasi == true)
                        {
                            sumAF += 1.00m * w;
                        }
                        else if (nilaiAF.TryGetValue(input, out var af))
                        {
                            sumAF += af * w;
                        }

                        sumW += w;
                    }

                    if (sumW - sumX > 0)
                    {
                        decimal score = (sumAF / (sumW - sumX)) * sumW;
                        totalScore += score;
                        totalWeight += sumW;
                    }
                }
            }

            return totalWeight > 0 ? (totalScore / totalWeight) * 100m : 0m;
        }


        private void CalculateOverallScore(DetailReportViewModel model, List<ChecklistFlatItem> flatItems)
        {
            decimal totalScore = 0m;
            decimal maxScore = 0m;
            var debug = new List<string>();

            var nilaiAF = new Dictionary<string, decimal>
            {
                ["A"] = 1.00m,
                ["B"] = 0.80m,
                ["C"] = 0.60m,
                ["D"] = 0.40m,
                ["E"] = 0.20m,
                ["F"] = 0.00m
            };

            void HitungPerElemen(AuditChecklistNode node, int level)
            {
                if (level == 0)
                {
                    decimal skor = 0m;
                    decimal localMax = 0m;
                    bool isSpecialElement = node.Title?.Trim().ToUpperInvariant() == "ELEMEN 2" || node.Title?.Trim().ToUpperInvariant() == "ELEMEN 5";

                    if (isSpecialElement)
                    {
                        foreach (var child in node.Children ?? new())
                        {
                            decimal sumAF = 0;
                            decimal sumWeight = 0;
                            decimal sumX = 0;

                            void HitungPertanyaan(AuditChecklistNode q)
                            {
                                if (q.Children != null && q.Children.Any())
                                {
                                    foreach (var c in q.Children)
                                        HitungPertanyaan(c);
                                }
                                else
                                {
                                    string input = q.ScoreInput?.Trim().ToUpper() ?? "";
                                    decimal w = q.Weight ?? 0;

                                    if (input == "X")
                                    {
                                        sumX += w;
                                        sumAF += q.ScoreX ?? 0;
                                    }
                                    else if (input == "F" && q.IsRelaksasi == true)
                                    {
                                        sumAF += 1.00m * w;
                                    }
                                    else if (nilaiAF.TryGetValue(input, out var af))
                                    {
                                        sumAF += af * w;
                                    }

                                    sumWeight += w;
                                }
                            }

                            HitungPertanyaan(child);
                            decimal partial = (sumWeight - sumX) > 0 ? (sumAF / (sumWeight - sumX)) * sumWeight : 0;
                            skor += partial;
                            localMax += sumWeight;
                        }
                    }
                    else
                    {
                        decimal sumAF = 0, sumWeight = 0, sumX = 0;

                        void HitungSkor(AuditChecklistNode n)
                        {
                            if (n.Children != null && n.Children.Any())
                            {
                                foreach (var c in n.Children)
                                    HitungSkor(c);
                            }
                            else
                            {
                                string input = n.ScoreInput?.Trim().ToUpper() ?? "";
                                decimal w = n.Weight ?? 0;

                                if (input == "X")
                                {
                                    sumX += w;
                                    sumAF += n.ScoreX ?? 0;
                                }
                                else if (input == "F" && n.IsRelaksasi == true)
                                {
                                    sumAF += 1.00m * w;
                                }
                                else if (nilaiAF.TryGetValue(input, out var af))
                                {
                                    sumAF += af * w;
                                }

                                sumWeight += w;
                            }
                        }

                        HitungSkor(node);
                        skor = (sumWeight - sumX) > 0 ? (sumAF / (sumWeight - sumX)) * sumWeight : 0;
                        localMax = sumWeight;
                    }

                    totalScore += skor;
                    maxScore += localMax;
                    debug.Add($"→ {node.Title} | Skor: {skor:0.##} dari {localMax:0.##}");
                }

                foreach (var child in node.Children ?? new())
                {
                    HitungPerElemen(child, level + 1);
                }
            }

            foreach (var root in model.Elements ?? new())
            {
                HitungPerElemen(root, 0);
            }

            model.TotalScore = totalScore;
            model.MaxScore = maxScore;
            model.FinalScore = (maxScore > 0) ? (totalScore / maxScore) * 100m : 0m;
            model.MinPassingScore = 80.00m;
            model.MinPassingScoreGood = 75.00m;

            // Log ke Output
            System.Diagnostics.Debug.WriteLine("[DEBUG] Perhitungan Total Skor:");
            foreach (var line in debug)
                System.Diagnostics.Debug.WriteLine(line);
            System.Diagnostics.Debug.WriteLine($"TOTAL: {totalScore:0.##} / {maxScore:0.##} = {model.FinalScore:0.##}%");
        }

        private (decimal? SSS, decimal? EQnQ, decimal? RFS, decimal? VFC, decimal? EPO) HitungComplianceLevelDariElements(List<AuditChecklistNode> elements)
        {
            decimal? Ambil(string title) =>
                elements.FirstOrDefault(e => e.Title?.Trim().ToUpperInvariant() == title.Trim().ToUpperInvariant())?.ScoreAF * 100;

            return (
                SSS: Ambil("Elemen 1"),
                EQnQ: Ambil("Elemen 2"),
                RFS: Ambil("Elemen 3"),
                VFC: Ambil("Elemen 4"),
                EPO: Ambil("Elemen 5")
            );
        }


        private void AssignWeightRecursive(AuditChecklistNode node)
        {
            if (node.Children?.Any() == true)
            {
                foreach (var child in node.Children)
                {
                    AssignWeightRecursive(child);
                }

                node.Weight = node.Children.Sum(c => c.Weight ?? 0m);
            }
        }

        [HttpPost("auditreport/reassign/{id}")]
        public async Task<IActionResult> Reassign(string id)
        {
            var currentUser = User.Identity?.Name;

            string sql = @"
        UPDATE trx_audit
        SET approval_date = now(),
            approval_by = @p0,
            updated_date = now(),
            updated_by = @p0,
            status = 'UNDER_REVIEW'
        WHERE id = @p1";

            int affected = await _context.Database.ExecuteSqlRawAsync(sql, currentUser, id);

            if (affected == 0)
                return NotFound();

            TempData["Success"] = "Laporan audit telah Reassign ke Review.";
            return RedirectToAction("Detail", new { id });
        }
        private List<AuditChecklistNode> BuildHierarchy(
    List<ChecklistFlatItem> flatList,
    Dictionary<string, List<MediaItem>> mediaList)
        {
            var lookup = flatList.ToLookup(x => x.parent_id);

            List<AuditChecklistNode> BuildChildren(string parentId) =>
                lookup[parentId]
                .OrderBy(x => x.order_no)
                .Select(item => new AuditChecklistNode
                {
                    Id = item.id,
                    Title = item.title,
                    number = item.number,
                    Description = item.description,
                    Type = item.type,
                    Weight = item.weight,
                    ScoreOption = item.score_option,
                    ScoreInput = item.score_input,
                    ScoreAF = item.score_af,
                    ScoreX = item.score_x,
                    IsRelaksasi = item.is_relaksasi,
                    MediaItems = mediaList.ContainsKey(item.id)
                                  ? mediaList[item.id]
                                  : new List<MediaItem>(),
                    Children = BuildChildren(item.id)
                })
                .ToList();

            return BuildChildren(
                flatList.Any(x => x.parent_id == null) ? null : ""
            );
        }
    }
}
