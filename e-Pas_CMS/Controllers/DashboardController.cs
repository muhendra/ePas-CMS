using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace e_Pas_CMS.Controllers
{
    public class DashboardController : Controller
    {
        private readonly EpasDbContext _context;

        private readonly IConfiguration _configuration;

        public DashboardController(EpasDbContext context, IConfiguration configuration)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }


        public async Task<IActionResult> Nasional(int pageNumber = 1, int pageSize = 10, string searchTerm = "", int? filterMonth = null, int? filterYear = null)
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
                    Provinsi = a.spbu.province_name,
                    AuditType = a.audit_type,
                    Status = a.status,
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

        public async Task<IActionResult> Province(
    int pageNumber = 1, int pageSize = 10, string searchTerm = "",
    int? filterMonth = null, int? filterYear = null, string filterProvince = ""
)
        {
            var currentUser = User.Identity?.Name;

            // Ambil hak akses user
            var userRegion = await (from aur in _context.app_user_roles
                                    join au in _context.app_users on aur.app_user_id equals au.id
                                    where au.username == currentUser
                                    select aur.region)
                                .Where(r => r != null)
                                .Distinct()
                                .ToListAsync();

            var userSbm = await (from aur in _context.app_user_roles
                                 join au in _context.app_users on aur.app_user_id equals au.id
                                 where au.username == currentUser
                                 select aur.sbm)
                                .Where(s => s != null)
                                .Distinct()
                                .ToListAsync();

            // Query utama data audit
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

            // === FILTER PROVINCE (baru) ===
            if (!string.IsNullOrWhiteSpace(filterProvince))
            {
                var fp = filterProvince.Trim();
                query = query.Where(a => a.spbu.province_name == fp);
            }

            // Search bebas
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var s = searchTerm.ToLower();
                query = query.Where(a =>
                    a.spbu.spbu_no.ToLower().Contains(s) ||
                    a.app_user.name.ToLower().Contains(s) ||
                    a.status.ToLower().Contains(s) ||
                    a.spbu.address.ToLower().Contains(s) ||
                    a.spbu.province_name.ToLower().Contains(s) ||
                    a.spbu.city_name.ToLower().Contains(s)
                );
            }

            // Filter bulan/tahun
            if (filterMonth.HasValue && filterYear.HasValue)
            {
                query = query.Where(a =>
                    ((a.audit_execution_time != null ? a.audit_execution_time.Value.Month : a.created_date.Month) == filterMonth.Value) &&
                    ((a.audit_execution_time != null ? a.audit_execution_time.Value.Year : a.created_date.Year) == filterYear.Value)
                );
            }

            // === Daftar provinsi untuk dropdown: dibatasi oleh hak akses user ===
            var spbuScoped = _context.spbus.AsQueryable();
            if (userRegion.Any() || userSbm.Any())
            {
                spbuScoped = spbuScoped.Where(s =>
                    (s.region != null && userRegion.Contains(s.region)) ||
                    (s.sbm != null && userSbm.Contains(s.sbm))
                );
            }
            var provinces = await spbuScoped
                .Where(s => !string.IsNullOrEmpty(s.province_name))
                .Select(s => s.province_name)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            ViewBag.Provinces = provinces;
            ViewBag.FilterProvince = filterProvince;
            ViewBag.FilterMonth = filterMonth;
            ViewBag.FilterYear = filterYear;
            ViewBag.SearchTerm = searchTerm;

            // Sorting, paging, mapping (lanjutkan kodemu persis seperti sebelumnya)...
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
                    Provinsi = a.spbu.province_name,
                    AuditType = a.audit_type,
                    Status = a.status,
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

        public async Task<IActionResult> Regional(
    int pageNumber = 1, int pageSize = 10, string searchTerm = "",
    int? filterMonth = null, int? filterYear = null, string filterregion = ""
)
        {
            var currentUser = User.Identity?.Name;
            ViewBag.SelectedRegion = filterregion;
            // Ambil hak akses user
            var userRegion = await (from aur in _context.app_user_roles
                                    join au in _context.app_users on aur.app_user_id equals au.id
                                    where au.username == currentUser
                                    select aur.region)
                                .Where(r => r != null)
                                .Distinct()
                                .ToListAsync();

            var userSbm = await (from aur in _context.app_user_roles
                                 join au in _context.app_users on aur.app_user_id equals au.id
                                 where au.username == currentUser
                                 select aur.sbm)
                                .Where(s => s != null)
                                .Distinct()
                                .ToListAsync();

            // Query utama data audit
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

            // === FILTER PROVINCE (baru) ===
            if (!string.IsNullOrWhiteSpace(filterregion))
            {
                var fp = filterregion.Trim();
                query = query.Where(a => a.spbu.region == fp);
            }

            // Search bebas
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var s = searchTerm.ToLower();
                query = query.Where(a =>
                    a.spbu.spbu_no.ToLower().Contains(s) ||
                    a.app_user.name.ToLower().Contains(s) ||
                    a.status.ToLower().Contains(s) ||
                    a.spbu.address.ToLower().Contains(s) ||
                    a.spbu.province_name.ToLower().Contains(s) ||
                    a.spbu.city_name.ToLower().Contains(s)
                );
            }

            // Filter bulan/tahun
            if (filterMonth.HasValue && filterYear.HasValue)
            {
                query = query.Where(a =>
                    ((a.audit_execution_time != null ? a.audit_execution_time.Value.Month : a.created_date.Month) == filterMonth.Value) &&
                    ((a.audit_execution_time != null ? a.audit_execution_time.Value.Year : a.created_date.Year) == filterYear.Value)
                );
            }

            // === Daftar provinsi untuk dropdown: dibatasi oleh hak akses user ===
            var spbuScoped = _context.spbus.AsQueryable();
            if (userRegion.Any() || userSbm.Any())
            {
                spbuScoped = spbuScoped.Where(s =>
                    (s.region != null && userRegion.Contains(s.region)) ||
                    (s.sbm != null && userSbm.Contains(s.sbm))
                );
            }
            var provinces = await spbuScoped
                .Where(s => !string.IsNullOrEmpty(s.province_name))
                .Select(s => s.province_name)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            ViewBag.Provinces = provinces;
            ViewBag.FilterProvince = filterregion;
            ViewBag.FilterMonth = filterMonth;
            ViewBag.FilterYear = filterYear;
            ViewBag.SearchTerm = searchTerm;

            // Sorting, paging, mapping (lanjutkan kodemu persis seperti sebelumnya)...
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
                    Provinsi = a.spbu.province_name,
                    AuditType = a.audit_type,
                    Status = a.status,
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

        public async Task<IActionResult> Spbu(
        int pageNumber = 1,
        int pageSize = 10,
        string search = ""
    )
        {
            var query = _context.spbus.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(x =>
                    (x.spbu_no ?? "").ToLower().Contains(s) ||
                    (x.province_name ?? "").ToLower().Contains(s) ||
                    (x.city_name ?? "").ToLower().Contains(s) ||
                    (x.owner_name ?? "").ToLower().Contains(s) ||
                    (x.manager_name ?? "").ToLower().Contains(s) ||
                    (x.address ?? "").ToLower().Contains(s) ||
                    (x.region ?? "").ToLower().Contains(s) ||
                    (x.sbm ?? "").ToLower().Contains(s)
                );
            }

            var totalItems = await query.CountAsync();

            query = query
                .OrderBy(x => x.spbu_no)
                .ThenBy(x => x.id);

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SpbuSimpleViewModel
                {
                    Id = s.id,
                    SpbuNo = s.spbu_no,
                    Region = s.region,
                    Provinsi = s.province_name,
                    Kota = s.city_name,
                    Alamat = s.address,
                    Pemilik = s.owner_name,
                    Pengelola = s.manager_name,
                    JenisPemilik = s.owner_type,
                    Tipe = s.type,
                    Level = s.level,
                    Kontak = s.phone_number_1,
                    SkorAuditTerakhir = s.audit_current_score,
                    TanggalAuditTerakhir = s.audit_current_time,
                    Status = s.status
                })
                .ToListAsync();

            var model = new PaginationModel<SpbuSimpleViewModel>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            ViewBag.Search = search;
            return View(model); 
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
    au.name               AS NamaAuditor
FROM trx_audit ta
JOIN spbu s   ON ta.spbu_id = s.id
JOIN app_user au ON au.id = ta.app_user_id
WHERE ta.id = @id;
";

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
                NamaAuditor = basic.NamaAuditor
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
                commandTimeout: 6000
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
