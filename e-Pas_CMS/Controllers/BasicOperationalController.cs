using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.Models;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static NpgsqlTypes.NpgsqlTsQuery;

namespace e_Pas_CMS.Controllers
{
    [Authorize]
    public class BasicOperationalController : Controller
    {
        private readonly EpasDbContext _context;
        private const int DefaultPageSize = 10;
        private readonly ILogger<BasicOperationalController> _logger;

        public BasicOperationalController(EpasDbContext context, ILogger<BasicOperationalController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = DefaultPageSize, string searchTerm = "", string sortColumn = "TanggalAudit", string sortDirection = "desc")
        {
            try
            {
                var currentUser = User.Identity?.Name;
                bool isReadonlyUser = currentUser == "usermanagement1";

                var userRegion = await (from aur in _context.app_user_roles
                                        join au in _context.app_users on aur.app_user_id equals au.id
                                        where au.username == currentUser
                                        select aur.region)
                       .Distinct()
                       .Where(r => r != null)
                       .ToListAsync();

                var query = from a in _context.trx_audits
                            join s in _context.spbus on a.spbu_id equals s.id
                            join u in _context.app_users on a.app_user_id equals u.id into aud
                            from u in aud.DefaultIfEmpty()
                            where a.status == "UNDER_REVIEW" && a.audit_type == "Basic Operational"
                            select new
                            {
                                Audit = a,
                                Spbu = s,
                                AuditorName = u.name
                            };

                if (userRegion.Any())
                {
                    query = query.Where(x => userRegion.Contains(x.Spbu.region));
                }

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    searchTerm = searchTerm.ToLower();
                    query = query.Where(x =>
                        x.Spbu.spbu_no.ToLower().Contains(searchTerm) ||
                        (x.AuditorName != null && x.AuditorName.ToLower().Contains(searchTerm)) ||
                        x.Audit.status.ToLower().Contains(searchTerm) ||
                        (x.Spbu.address != null && x.Spbu.address.ToLower().Contains(searchTerm)) ||
                        (x.Spbu.province_name != null && x.Spbu.province_name.ToLower().Contains(searchTerm)) ||
                        (x.Spbu.city_name != null && x.Spbu.city_name.ToLower().Contains(searchTerm))
                    );
                }

                query = query.OrderBy(x => x.Audit.created_date);

                // Apply sorting
                query = sortColumn switch
                {
                    "Auditor" => sortDirection == "asc" ? query.OrderBy(q => q.AuditorName) : query.OrderByDescending(q => q.AuditorName),
                    "TanggalAudit" => sortDirection == "asc" ? query.OrderBy(q => q.Audit.updated_date) : query.OrderByDescending(q => q.Audit.updated_date),
                    "Status" => sortDirection == "asc" ? query.OrderBy(q => q.Audit.status) : query.OrderByDescending(q => q.Audit.status),
                    "Tahun" => sortDirection == "asc" ? query.OrderBy(q => q.Audit.created_date) : query.OrderByDescending(q => q.Audit.created_date),
                    "NoSpbu" => sortDirection == "asc" ? query.OrderBy(q => q.Spbu.spbu_no) : query.OrderByDescending(q => q.Spbu.spbu_no),
                    "Rayon" => sortDirection == "asc" ? query.OrderBy(q => q.Spbu.region) : query.OrderByDescending(q => q.Spbu.region),
                    "Kota" => sortDirection == "asc" ? query.OrderBy(q => q.Spbu.city_name) : query.OrderByDescending(q => q.Spbu.city_name),
                    "Alamat" => sortDirection == "asc" ? query.OrderBy(q => q.Spbu.address) : query.OrderByDescending(q => q.Spbu.address),
                    "Audit" => sortDirection == "asc" ? query.OrderBy(q => q.Audit.audit_level) : query.OrderByDescending(q => q.Audit.audit_level),
                    _ => query.OrderByDescending(q => q.Audit.updated_date)
                };

                var totalItems = await query.CountAsync();
                var items = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                using var conn = _context.Database.GetDbConnection();

                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                var result = new List<SpbuViewModel>();

                foreach (var a in items)
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

                    var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(sql, new { id = a.Audit.id }))
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

                    // === Hitung Compliance
                    var checklistData = await GetChecklistDataAsync(conn, a.Audit.id);
                    var mediaList = await GetMediaPerNodeAsync(conn, a.Audit.id);
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

                    var penaltyGoodQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
            FROM trx_audit_checklist tac
            INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
            WHERE tac.trx_audit_id = @id AND
              tac.score_input = 'F' AND
              mqd.is_penalty = true AND 
              (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL);";
                    var penaltyGoodResult = await conn.ExecuteScalarAsync<string>(penaltyGoodQuery, new { id = a.Audit.id });

                    bool hasGoodPenalty = !string.IsNullOrEmpty(penaltyGoodResult);

                    string goodStatus = (finalScore >= 75 && !hasGoodPenalty)
                        ? "CERTIFIED"
                        : "NOT CERTIFIED";

                    bool failGood = sss < 80 || eqnq < 85 || rfs < 85;
                    bool failExcellent = sss < 85 || eqnq < 85 || rfs < 85;

                    // === Audit Next
                    string auditNext = null;
                    string levelspbu = null;

                    var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                    var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = a.Audit.audit_level });

                    if (auditFlow != null)
                    {
                        string passedGood = auditFlow.passed_good;
                        string passedExcellent = auditFlow.passed_excellent;
                        string passedAuditLevel = auditFlow.passed_audit_level;
                        string failed_audit_level = auditFlow.failed_audit_level;

                        if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && finalScore >= 75)
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


                    result.Add(new SpbuViewModel
                    {
                        Id = a.Audit.id,
                        NoSpbu = a.Spbu.spbu_no,
                        Rayon = a.Spbu.region,
                        Alamat = a.Spbu.address,
                        TipeSpbu = a.Spbu.type,
                        Tahun = a.Audit.created_date.ToString("yyyy"),
                        Audit = a.Audit.audit_level,
                        //Score = Math.Round(finalScore, 2),
                        Score = Math.Round((decimal)(totalScore ?? a.Audit.score), 2),
                        Good = goodStatus,
                        //Excelent = excellentStatus,
                        Provinsi = a.Spbu.province_name,
                        Kota = a.Spbu.city_name,
                        NamaAuditor = a.AuditorName,
                        Report = a.Audit.report_no,
                        TanggalSubmit = (a.Audit.audit_execution_time == null || a.Audit.audit_execution_time.Value == DateTime.MinValue)
                            ? a.Audit.updated_date.Value
                            : a.Audit.audit_execution_time.Value,
                        Status = a.Audit.status,
                        Komplain = a.Audit.status == "FAIL" ? "ADA" : "TIDAK ADA",
                        Banding = a.Audit.audit_level == "Re-Audit" ? "ADA" : "TIDAK ADA",
                        Type = a.Spbu.audit_current,
                        TanggalApprove = a.Audit.approval_date ?? DateTime.Now,
                        Approver = string.IsNullOrWhiteSpace(a.Audit.approval_by) ? "-" : a.Audit.approval_by
                    });
                }

                // Sorting untuk hasil final list

                result = sortColumn switch
                {
                    "Score" => sortDirection == "asc" ? result.OrderBy(r => r.Score).ToList() : result.OrderByDescending(r => r.Score).ToList(),
                    "Good" => sortDirection == "asc" ? result.OrderBy(r => r.Good).ToList() : result.OrderByDescending(r => r.Good).ToList(),
                    //"Excellent" => sortDirection == "asc" ? result.OrderBy(r => r.Excelent).ToList() : result.OrderByDescending(r => r.Excelent).ToList(),
                    "Komplain" => sortDirection == "asc" ? result.OrderBy(r => r.Komplain).ToList() : result.OrderByDescending(r => r.Komplain).ToList(),
                    "Banding" => sortDirection == "asc" ? result.OrderBy(r => r.Banding).ToList() : result.OrderByDescending(r => r.Banding).ToList(),
                    _ => result
                };

                var paginationModel = new PaginationModel<SpbuViewModel>
                {
                    Items = result,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalItems = totalItems
                };
                ViewBag.SearchTerm = searchTerm;
                ViewBag.SortColumn = sortColumn;
                ViewBag.SortDirection = sortDirection;

                return View(paginationModel);
            }

            catch (Exception ex)
            {
                TempData["Error"] = "Terjadi kesalahan saat memuat data. Silakan coba lagi.";
                return View(new PaginationModel<SpbuViewModel>
                {
                    Items = new List<SpbuViewModel>(),
                    PageNumber = 1,
                    PageSize = DefaultPageSize,
                    TotalItems = 0
                });
            }
        }

        private (decimal? SSS, decimal? EQnQ, decimal? RFS) HitungComplianceLevelDariElements(List<AuditChecklistNode> elements)
        {
            decimal? Ambil(string title) =>
                elements.FirstOrDefault(e => e.Title?.Trim().ToUpperInvariant() == title.Trim().ToUpperInvariant())?.ScoreAF * 100;

            return (
                SSS: Ambil("Elemen 1"),
                EQnQ: Ambil("Elemen 2"),
                RFS: Ambil("Elemen 3")
            );
        }

        [HttpGet("BasicOperational/Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            // Pull the main audit header
            var audit = await (
                from ta in _context.trx_audits
                join au in _context.app_users on ta.app_user_id equals au.id
                join s in _context.spbus on ta.spbu_id equals s.id
                where ta.id == id
                select new DetailAuditViewModel
                {
                    ReportNo = ta.report_no,
                    NamaAuditor = au.name,
                    TanggalSubmit = (ta.audit_execution_time == null
                                    || ta.audit_execution_time.Value == DateTime.MinValue)
                                    ? ta.updated_date.Value
                                    : ta.audit_execution_time.Value,
                    Status = ta.status,
                    SpbuNo = s.spbu_no,
                    Provinsi = s.province_name,
                    Kota = s.city_name,
                    Alamat = s.address,
                    Notes = ta.audit_mom_final,
                    AuditType = ta.audit_type
                }
            ).FirstOrDefaultAsync();

            if (audit == null)
                return NotFound();

            // Load QUESTION-type media notes
            var mediaQuestionSql = @"
                SELECT media_path, media_type
                FROM trx_audit_media
                WHERE trx_audit_id = @id
                  AND type = 'QUESTION'";
            var mq = (await conn.QueryAsync<(string media_path, string media_type)>(mediaQuestionSql, new { id }))
                       .ToList();
            if ((audit.AuditType == "Mystery Audit" || audit.AuditType == "Mystery Guest") && mq.Any())
            {
                audit.MediaNotes = mq
                    .Select(x => new MediaItem
                    {
                        MediaType = x.media_type,
                        MediaPath = "https://epas-assets.zarata.co.id" + x.media_path
                    })
                    .ToList();
            }

            // Load the flat checklist
            var checklistSql = @"
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
                  tac.comment,
                  mqd.is_penalty,
                  mqd.order_no,
                  mqd.is_relaksasi,
                  mqd.number,
                  (
                    SELECT string_agg(mqd2.penalty_alert, ', ')
                    FROM trx_audit_checklist tac2
                    INNER JOIN master_questioner_detail mqd2 ON mqd2.id = tac2.master_questioner_detail_id
                    WHERE 
                      tac2.trx_audit_id = @id
                      AND tac2.score_input = 'F'
                      AND mqd2.is_penalty = true
                  ) AS penalty_alert
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac
                  ON tac.master_questioner_detail_id = mqd.id
                 AND tac.trx_audit_id = @id
                WHERE mqd.master_questioner_id = (
                  SELECT master_questioner_checklist_id
                  FROM trx_audit
                  WHERE id = @id
                )
                ORDER BY mqd.order_no;";

            var checklistData = (await conn.QueryAsync<ChecklistFlatItem>(checklistSql, new { id }))
                                    .ToList();

            // Load any media per-node
            var mediaSql = @"
                SELECT master_questioner_detail_id, media_type, media_path
                FROM trx_audit_media
                WHERE trx_audit_id = @id
                  AND master_questioner_detail_id IS NOT NULL";
            var mediaList = (await conn.QueryAsync<(string master_questioner_detail_id, string media_type, string media_path)>(mediaSql, new { id }))
                .GroupBy(x => x.master_questioner_detail_id)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(m => new MediaItem
                    {
                        MediaType = m.media_type,
                        MediaPath = "https://epas-assets.zarata.co.id" + m.media_path
                    }).ToList()
                );

            // Build tree
            audit.Elements = BuildHierarchy(checklistData, mediaList);

            var validAF = new Dictionary<string, decimal>
            {
                ["A"] = 1.00m,
                ["B"] = 0.80m,
                ["C"] = 0.60m,
                ["D"] = 0.40m,
                ["E"] = 0.20m,
                ["F"] = 0.00m
            };

            decimal sumAF = 0m;
            decimal sumX = 0m;
            decimal sumWeight = 0m;

            foreach (var q in checklistData.Where(x => x.type == "QUESTION"))
            {
                string input = q.score_input?.Trim();
                decimal weight = q.weight ?? 0m;

                // Expand A-F into [A,B,C,D,E,F], then ensure X is in the list
                var allowed = (q.score_option ?? "")
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .SelectMany(opt =>
                        opt == "A-F"
                          ? new[] { "A", "B", "C", "D", "E", "F" }
                          : new[] { opt.Trim() })
                    .Concat(new[] { "X" })
                    .Select(x => x.Trim())
                    .Distinct()
                    .ToList();

                // skip if empty or not in allowed
                if (string.IsNullOrWhiteSpace(input) || !allowed.Contains(input))
                    continue;

                if (input == "X")
                {
                    sumX += weight;
                }
                else
                {
                    // Override input jadi F kalau relaksasi
                    if (q.is_relaksasi == true)
                        input = "F";

                    if (validAF.TryGetValue(input, out var af))
                    {
                        sumAF += af * weight;
                        sumWeight += weight;
                    }
                }
            }

            audit.TotalScore = sumAF;
            audit.MaxScore = sumWeight;
            audit.FinalScore = (sumWeight - sumX) > 0
                ? (sumAF / (sumWeight - sumX)) * 100m
                : 0m;

            var qqSql = @"
                SELECT nozzle_number AS NozzleNumber,
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
            audit.QqChecks = (await conn.QueryAsync<AuditQqCheckItem>(qqSql, new { id })).ToList();

            var finalMediaSql = @"
                SELECT media_type, media_path
                FROM trx_audit_media
                WHERE trx_audit_id = @id
                  AND type = 'FINAL'";
            audit.FinalDocuments = (await conn.QueryAsync<(string media_type, string media_path)>(finalMediaSql, new { id }))
                .Select(x => new MediaItem
                {
                    MediaType = x.media_type,
                    MediaPath = "https://epas-assets.zarata.co.id" + x.media_path
                })
                .ToList();

            ViewBag.AuditId = id;
            return View(audit);
        }

        [HttpPost("BasicOperational/approve/{id}")]
        public async Task<IActionResult> Approve(string id)
        {
            var currentUser = User.Identity?.Name;

            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var basic = await GetAuditHeaderAsync(conn, id);
            if (basic == null)
                return NotFound();

            var model = MapToViewModel(basic);

            var existingSPBU = await _context.spbus
                    .Where(x => x.spbu_no == model.SpbuNo)
                    .OrderByDescending(x => x.created_date)
                    .FirstOrDefaultAsync();

            var checklistData = await GetChecklistDataAsync(conn, id);
            var mediaList = await GetMediaPerNodeAsync(conn, id);
            model.Elements = BuildHierarchy(checklistData, mediaList);

            foreach (var element in model.Elements)
            {
                AssignWeightRecursive(element);
            }

            CalculateChecklistScores(model.Elements);
            CalculateOverallScore(model, checklistData);

            ViewBag.AuditId = id;

            string sql = @"
            UPDATE trx_audit
            SET approval_date = now(),
                approval_by = @p0,
                updated_date = now(),
                updated_by = @p0,
                status = 'VERIFIED',
                score = @p1
            WHERE id = @p2";

            int affected = await _context.Database.ExecuteSqlRawAsync(sql, currentUser, model.TotalScore, id);

            if (affected == 0)
                return NotFound();

            var updateSql = @"
            UPDATE spbu
            SET audit_next = @auditnext
            WHERE spbu_no = @spbuNo";

            await conn.ExecuteAsync(updateSql, new
            {
                auditnext = model.AuditNext,
                spbuNo = model.SpbuNo
            });

            TempData["Success"] = "Laporan audit telah disetujui.";
            return RedirectToAction("Detail", new { id });
        }

        private async Task<AuditHeaderDto> GetAuditHeaderAsync(IDbConnection conn, string id)
        {
            string sql = @"
    WITH RECURSIVE question_hierarchy AS (
        SELECT 
            mqd.id,
            mqd.title,
            mqd.parent_id,
            mqd.title AS root_title
        FROM master_questioner_detail mqd
        WHERE mqd.title IN ('Elemen 1', 'Elemen 2', 'Elemen 3', 'Elemen 4', 'Elemen 5')
        UNION ALL
        SELECT 
            mqd.id,
            mqd.title,
            mqd.parent_id,
            qh.root_title
        FROM master_questioner_detail mqd
        INNER JOIN question_hierarchy qh ON mqd.parent_id = qh.id
    ),
    comment_per_elemen AS (
        SELECT
            qh.root_title,
            string_agg(tac.comment, E'\n') AS all_comments
        FROM question_hierarchy qh
        JOIN trx_audit_checklist tac ON tac.master_questioner_detail_id = qh.id
        WHERE tac.trx_audit_id = @id
          AND tac.comment IS NOT NULL
          AND trim(tac.comment) <> ''
        GROUP BY qh.root_title
    )
    SELECT 
        ta.id,
        ta.report_no                          AS ReportNo,
        ta.audit_type                         AS AuditType,
        ta.audit_execution_time               AS SubmitDate,
        ta.status,
        ta.audit_mom_intro                    AS Notes,
        ta.audit_level,
        s.spbu_no                             AS SpbuNo,
        s.region,
        s.city_name                           AS Kota,
        s.address                             AS Alamat,
        s.owner_name                          AS OwnerName,
        s.manager_name                        AS ManagerName,
        s.owner_type                          AS OwnershipType,
        s.quater                              AS Quarter,
        s.""year""                              AS Year,
        s.mor                                 AS Mor,
        s.sales_area                          AS SalesArea,
        s.sbm                                 AS Sbm,
        s.""level""                           AS ClassSpbu,
        s.phone_number_1                      AS Phone,
        (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 1') AS KomentarStaf,
        (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 2') AS KomentarQuality,
        (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 3') AS KomentarHSSE,
        (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 4') AS KomentarVisual,
        CASE 
            WHEN audit_mom_final IS NOT NULL AND audit_mom_final <> '' THEN audit_mom_final
            ELSE audit_mom_intro
        END AS KomentarManager,
        approval_date as ApproveDate,
        (select name from app_user where username = ta.approval_by) as ApproveBy,
        ta.updated_date as UpdateDate,
        ta.audit_level as AuditCurrent,
        s.audit_next as AuditNext,
        au.name as NamaAuditor
    FROM trx_audit ta
    JOIN spbu s ON ta.spbu_id = s.id
    join app_user au on au.id = ta.app_user_id
    WHERE ta.id = @id";

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

            string column;

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
                AuditCurrent = basic.AuditCurrent,
                AuditNext = basic.AuditNext,
                ApproveBy = basic.ApproveBy,
                NamaAuditor = basic.NamaAuditor
            };
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
                bool isSpecial = element == "ELEMEN 2";

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
                    bool isSpecialElement = node.Title?.Trim().ToUpperInvariant() == "ELEMEN 2";

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

            var raw = await conn.QueryAsync<(string master_questioner_detail_id, string media_type, string media_path)>(sql, new { id });

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


        [HttpPost]
        public async Task<IActionResult> UpdateScore([FromBody] UpdateScoreRequest request)
        {
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            var sql = @"
                    UPDATE trx_audit_checklist
                    SET score_input = @score
                    WHERE master_questioner_detail_id = @nodeId
                      AND trx_audit_id = @auditId";

            var affected = await conn.ExecuteAsync(sql, new
            {
                score = request.Score,
                nodeId = request.NodeId,
                auditId = request.AuditId
            });

            return affected > 0 ? Ok() : BadRequest("Tidak berhasil update");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBeritaAcaraText(string id, string notes)
        {
            var audit = await _context.trx_audits.FirstOrDefaultAsync(x => x.id == id);
            if (audit == null) return NotFound();

            audit.audit_mom_final = notes;
            audit.updated_by = User.Identity?.Name;
            audit.updated_date = DateTime.Now;

            await _context.SaveChangesAsync();
            TempData["Success"] = "Teks Berita Acara berhasil diperbarui";
            return RedirectToAction("Detail", new { id });
        }

        [HttpPost("BasicOperational/UploadBeritaAcaraMedia")]
        public async Task<IActionResult> UploadBeritaAcaraMedia(IFormFile file, string auditId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File tidak ditemukan atau kosong.");

            var generatedNodeId = Guid.NewGuid().ToString();
            var uploadsPath = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", auditId, generatedNodeId);

            Directory.CreateDirectory(uploadsPath);

            var fileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadsPath, fileName);

            // Simpan file fisik
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Simpan informasi file ke database
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var insertSql = @"
INSERT INTO trx_audit_media 
    (id, trx_audit_id, master_questioner_detail_id, media_type, media_path, type, status, created_date, created_by, updated_date, updated_by)
VALUES 
    (uuid_generate_v4(), @auditId, null, @mediaType, @mediaPath, 'FINAL', 'ACTIVE', NOW(), @createdBy, NOW(), @createdBy)";


            await conn.ExecuteAsync(insertSql, new
            {
                auditId,
                mediaType = Path.GetExtension(fileName).Trim('.').ToLower(),
                mediaPath = $"/uploads/{auditId}/{generatedNodeId}/{fileName}",
                createdBy = User.Identity?.Name ?? "anonymous"
            });

            return RedirectToAction("Detail", new { id = auditId });
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateComment([FromBody] UpdateCommentRequest request)
        {
            if (string.IsNullOrEmpty(request.AuditId) || string.IsNullOrEmpty(request.NodeId))
                return BadRequest("Data tidak lengkap");

            var entity = await _context.trx_audit_checklists
                .FirstOrDefaultAsync(x => x.trx_audit_id == request.AuditId && x.master_questioner_detail_id == request.NodeId);

            if (entity == null)
                return NotFound("Data tidak ditemukan");

            entity.comment = request.Comment;
            entity.updated_by = User.Identity?.Name;
            entity.updated_date = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok();
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
                    Comment = item.comment,
                    IsPenalty = item.is_penalty,
                    PenaltyAlert = item.penalty_alert,
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

        //        [HttpPost("BasicOperational/UploadDocument")]
        //        public async Task<IActionResult> UploadDocument(IFormFile file, string nodeId, string auditId)
        //        {
        //            if (file == null || file.Length == 0)
        //                return BadRequest("File tidak ditemukan atau kosong.");

        //            // Direktori penyimpanan
        //            var uploadsPath = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", auditId);
        //            Directory.CreateDirectory(uploadsPath);

        //            var fileName = Path.GetFileName(file.FileName);
        //            var filePath = Path.Combine(uploadsPath, fileName);

        //            // Simpan file fisik
        //            using (var stream = new FileStream(filePath, FileMode.Create))
        //            {
        //                await file.CopyToAsync(stream);
        //            }

        //            // Simpan informasi file ke database
        //            using var conn = _context.Database.GetDbConnection();
        //            if (conn.State != ConnectionState.Open)
        //                await conn.OpenAsync();

        //            var insertSql = @"
        //INSERT INTO trx_audit_media 
        //    (id, trx_audit_id, master_questioner_detail_id, media_type, media_path, type, status, created_date, created_by, updated_date, updated_by)
        //VALUES 
        //    (uuid_generate_v4(), @auditId, @nodeId, @mediaType, @mediaPath, 'QUESTION', 'ACTIVE', NOW(), @createdBy, NOW(), @createdBy)";


        //            await conn.ExecuteAsync(insertSql, new
        //            {
        //                auditId,
        //                nodeId,
        //                mediaType = Path.GetExtension(fileName).Trim('.').ToLower(),
        //                mediaPath = $"/uploads/{auditId}/{fileName}",
        //                createdBy = User.Identity?.Name ?? "anonymous"
        //            });

        //            return RedirectToAction("Detail", new { id = auditId });
        //        }

        [HttpPost("BasicOperational/UploadDocument")]
        public async Task<IActionResult> UploadDocument(IFormFile file, string nodeId, string auditId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File tidak ditemukan atau kosong.");

            var uploadsPath = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", auditId, nodeId);

            if (!Directory.Exists(uploadsPath))
            {
                Directory.CreateDirectory(uploadsPath);
            }

            // Selalu set permission ke 2775, baik folder baru maupun lama
            var chmod = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"2775 \"{uploadsPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(chmod)?.WaitForExit();

            var fileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadsPath, fileName);

            // Simpan file fisik
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Simpan informasi file ke database
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var insertSql = @"
INSERT INTO trx_audit_media 
    (id, trx_audit_id, master_questioner_detail_id, media_type, media_path, type, status, created_date, created_by, updated_date, updated_by)
VALUES 
    (uuid_generate_v4(), @auditId, @nodeId, @mediaType, @mediaPath, 'QUESTION', 'ACTIVE', NOW(), @createdBy, NOW(), @createdBy)";

            await conn.ExecuteAsync(insertSql, new
            {
                auditId,
                nodeId,
                mediaType = Path.GetExtension(fileName).Trim('.').ToLower(),
                mediaPath = $"/uploads/{auditId}/{nodeId}/{fileName}",
                createdBy = User.Identity?.Name ?? "anonymous"
            });

            return RedirectToAction("Detail", new { id = auditId });
        }

        [HttpGet("BasicOperational/ViewDocument/{auditId}/{id}")]
        public async Task<IActionResult> ViewDocument(string auditId, string id)
        {
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var querySql = @"
        SELECT media_path, media_type
        FROM trx_audit_media
        WHERE trx_audit_id = @auditId AND master_questioner_detail_id = @id
        ORDER BY created_date DESC
        LIMIT 1";

            var media = await conn.QueryFirstOrDefaultAsync<(string media_path, string media_type)>(querySql, new { auditId, id });

            if (string.IsNullOrEmpty(media.media_path))
                return NotFound("Dokumen tidak ditemukan.");

            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", media.media_path.TrimStart('/'));

            if (!System.IO.File.Exists(fullPath))
                return NotFound("File fisik tidak ditemukan di server.");

            var mimeType = media.media_type?.ToLower() switch
            {
                "pdf" => "application/pdf",
                "jpg" or "jpeg" => "image/jpeg",
                "png" => "image/png",
                _ => "application/octet-stream"
            };

            return PhysicalFile(fullPath, mimeType);
        }

        [HttpGet]
        public IActionResult GetLibraryMedia(int page = 1, int pageSize = 10, string search = "")
        {
            //var libraryDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "library");

            //var libraryDir = "/var/www/epas-asset/uploads/library";

            var libraryDir = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", "library");

            _logger.LogInformation("Gallery requested. Page: {Page}, PageSize: {PageSize}, Search: {Search}", page, pageSize, search);

            if (!Directory.Exists(libraryDir))
            {
                _logger.LogWarning("Library directory not found: {Dir}", libraryDir);
                return Json(new { total = 0, data = new List<object>() });
            }

            var allFiles = Directory.GetFiles(libraryDir)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => System.IO.File.GetCreationTime(f));

            // Apply search filter if search term is provided
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();
                allFiles = allFiles.Where(f =>
                    Path.GetFileName(f).ToLower().Contains(search) ||
                    Path.GetFileNameWithoutExtension(f).ToLower().Contains(search)
                ).OrderByDescending(f => System.IO.File.GetCreationTime(f));
            }

            var total = allFiles.Count();
            _logger.LogInformation("Total media files found: {Total}", total);

            var pagedFiles = allFiles
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new
                {
                    MediaType = f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "VIDEO" : "IMAGE",
                    MediaPath = $"/uploads/library/{Path.GetFileName(f)}"
                })
                .ToList();

            _logger.LogInformation("Returning {Count} media files for page {Page}", pagedFiles.Count, page);

            return Json(new { total, data = pagedFiles });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMediaPath([FromBody] UpdateMediaPathRequest request)
        {
            if (string.IsNullOrEmpty(request.NodeId) || string.IsNullOrEmpty(request.MediaPath))
            {
                _logger.LogWarning("UpdateMediaPath: Invalid request - NodeId or MediaPath is empty");
                return BadRequest("Data tidak lengkap");
            }

            if (request.AuditId.Contains("..") || request.NodeId.Contains(".."))
            {
                _logger.LogWarning("UpdateMediaPath: Invalid audit ID or node ID - contains '..'");
                return BadRequest("Invalid audit ID or node ID");
            }

            _logger.LogInformation("UpdateMediaPath: Searching for media entity with AuditId: {AuditId}, NodeId: {NodeId}", request.AuditId, request.NodeId);
            var entity = await _context.trx_audit_media
                .FirstOrDefaultAsync(x => x.trx_audit_id == request.AuditId && x.master_questioner_detail_id == request.NodeId);

            if (entity == null)
            {
                _logger.LogWarning("UpdateMediaPath: Media entity not found for AuditId: {AuditId}, NodeId: {NodeId}", request.AuditId, request.NodeId);
                return NotFound("Data tidak ditemukan");
            }

            try
            {
                var fileName = Path.GetFileName(request.MediaPath);
                _logger.LogInformation("UpdateMediaPath: Processing file: {FileName}", fileName);

                var destinationDir = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", request.AuditId, request.NodeId);
                _logger.LogInformation("UpdateMediaPath: Creating destination directory: {DestinationDir}", destinationDir);
                Directory.CreateDirectory(destinationDir);

                // var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", request.MediaPath.TrimStart('/'));
                var sourcePath = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", "library", fileName);
                var destinationPath = Path.Combine(destinationDir, fileName);
                _logger.LogInformation("UpdateMediaPath: Source path: {SourcePath}, Destination path: {DestinationPath}", sourcePath, destinationPath);

                if (!System.IO.File.Exists(sourcePath))
                {
                    _logger.LogWarning("UpdateMediaPath: Source file not found: {SourcePath}", sourcePath);
                    return BadRequest("Source file not found");
                }

                if (System.IO.File.Exists(destinationPath))
                {
                    _logger.LogInformation("UpdateMediaPath: Destination file exists, computing hashes for comparison");
                    var sourceHash = ComputeFileHash(sourcePath);
                    var destHash = ComputeFileHash(destinationPath);

                    if (sourceHash == destHash)
                    {
                        _logger.LogInformation("UpdateMediaPath: File already exists and is identical, skipping copy");
                        return Ok();
                    }
                    _logger.LogInformation("UpdateMediaPath: File exists but is different, proceeding with copy");
                }

                System.IO.File.Copy(sourcePath, destinationPath, true);
                _logger.LogInformation("UpdateMediaPath: File copied successfully from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);

                var newMediaPath = $"/uploads/{request.AuditId}/{request.NodeId}/{fileName}";
                _logger.LogInformation("UpdateMediaPath: Updating entity with new media path: {NewMediaPath}", newMediaPath);
                entity.media_path = newMediaPath;
                entity.media_type = request.MediaType;
                entity.updated_by = User.Identity?.Name;
                entity.updated_date = DateTime.Now;

                await _context.SaveChangesAsync();
                _logger.LogInformation("UpdateMediaPath: Successfully updated media entity in database");
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying file for audit {AuditId}, node {NodeId}", request.AuditId, request.NodeId);
                return BadRequest("Error copying file: " + ex.Message);
            }
        }

        private string ComputeFileHash(string filePath)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = System.IO.File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

    }
}
