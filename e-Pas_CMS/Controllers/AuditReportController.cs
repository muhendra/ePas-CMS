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

namespace e_Pas_CMS.Controllers
{
    [Authorize]
    public class AuditReportController : Controller
    {
        private readonly EpasDbContext _context;
        private readonly ILogger<AuditReportController> _logger;

        public AuditReportController(EpasDbContext context, ILogger<AuditReportController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, string searchTerm = "")
        {
            var currentUser = User.Identity?.Name;

            var userRegion = await (from aur in _context.app_user_roles
                                    join au in _context.app_users on aur.app_user_id equals au.id
                                    where au.username == currentUser
                                    select aur.region)
                       .Distinct()
                       .Where(r => r != null)
                       .ToListAsync();

            var query = _context.trx_audits
                .Include(a => a.spbu)
                .Include(a => a.app_user)
                .Where(a => a.status == "VERIFIED");

            if (userRegion.Any())
            {
                query = query.Where(x => userRegion.Contains(x.spbu.region));
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

            var totalItems = await query.CountAsync();

            var pagedAudits = await query
                .OrderByDescending(a => a.audit_execution_time ?? a.updated_date)
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

                var penaltyQuery = @"
SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
FROM trx_audit_checklist tac
INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
WHERE 
    tac.trx_audit_id = @id AND
    ((mqd.penalty_excellent_criteria = 'LT_1' AND tac.score_input <> 'A') OR
     (mqd.penalty_excellent_criteria = 'EQ_0' AND tac.score_input = 'F')) AND
    mqd.is_penalty = true;";

                var penaltyResult = await conn.ExecuteScalarAsync<string>(penaltyQuery, new { id = a.id });
                bool hasPenalty = !string.IsNullOrEmpty(penaltyResult);

                string goodStatus = "NOT CERTIFIED";
                string excellentStatus = "NOT CERTIFIED";

                if (finalScore >= 80)
                {
                    goodStatus = "CERTIFIED";
                    if (!hasPenalty)
                        excellentStatus = "EXCELLENT";
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
                    AuditDate = (a.audit_execution_time == null || a.audit_execution_time.Value == DateTime.MinValue)
                        ? a.updated_date.Value
                        : a.audit_execution_time.Value,
                    SubmitDate = a.approval_date.GetValueOrDefault() == DateTime.MinValue
                        ? a.updated_date
                        : a.approval_date.GetValueOrDefault(),
                    Auditor = a.app_user.name,
                    GoodStatus = goodStatus,
                    ExcellentStatus = excellentStatus,
                    Score = finalScore,
                    WTMS = a.spbu.wtms,
                    QQ = a.spbu.qq,
                    WMEF = a.spbu.wmef,
                    FormatFisik = a.spbu.format_fisik,
                    CPO = a.spbu.cpo,
                    KelasSpbu = "Pasti Pas Excellent",
                    ApproveDate = a.approval_date ?? DateTime.Now,
                    ApproveBy = string.IsNullOrWhiteSpace(a.approval_by) ? "-" : a.approval_by
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

        [HttpGet("Report/Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var basic = await GetAuditHeaderAsync(conn, id);
            if (basic == null)
                return NotFound();

            var model = MapToViewModel(basic);

            var penaltySql = @"
        SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
FROM trx_audit_checklist tac
INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
WHERE 
    tac.trx_audit_id = @id and
    ((mqd.penalty_excellent_criteria = 'LT_1' and tac.score_input <> 'A') or
    (mqd.penalty_excellent_criteria = 'EQ_0' and tac.score_input = 'F')) and
    mqd.is_penalty = true;";

            model.PenaltyAlerts = await conn.ExecuteScalarAsync<string>(penaltySql, new { id = id.ToString() });

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

            ViewBag.AuditId = id;
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

            CalculateChecklistScores(model.Elements);
            CalculateOverallScore(model, checklistData); // ini bisa dihapus kalau pakai finalScore baru

            // Ambil audit_execution_time
            var executionTimeSql = @"SELECT audit_execution_time FROM trx_audit WHERE id = @id";
            var auditExecutionTime = await conn.ExecuteScalarAsync<DateTime?>(executionTimeSql, new { id = id.ToString() });

            // Set ke model.TanggalSubmit agar bisa dipakai saat buat nama file
            model.TanggalAudit = auditExecutionTime;

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
            model.Score = finalScore; // simpan di model

            // --- Penalty ---
            var penaltySql = @"
        SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
FROM trx_audit_checklist tac
INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
WHERE 
    tac.trx_audit_id = @id and
    ((mqd.penalty_excellent_criteria = 'LT_1' and tac.score_input <> 'A') or
    (mqd.penalty_excellent_criteria = 'EQ_0' and tac.score_input = 'F')) and
    mqd.is_penalty = true";

            model.PenaltyAlerts = await conn.ExecuteScalarAsync<string>(penaltySql, new { id = id.ToString() });
            bool hasPenalty = !string.IsNullOrEmpty(model.PenaltyAlerts);

            // --- GoodStatus dan ExcellentStatus ---
            model.GoodStatus = "NOT CERTIFIED";
            model.ExcellentStatus = "NOT CERTIFIED";

            if (finalScore >= 80)
            {
                model.GoodStatus = "CERTIFIED";
                if (!hasPenalty)
                {
                    model.ExcellentStatus = "EXCELLENT";
                }
            }

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
            string sql = @"WITH RECURSIVE question_hierarchy AS (
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
                approval_by as ApproveBy,
                ta.updated_date as UpdateDate
            FROM trx_audit ta
            JOIN spbu s ON ta.spbu_id = s.id
            WHERE ta.id = @id";
            return await conn.QueryFirstOrDefaultAsync<AuditHeaderDto>(sql, new { id });
        }

        private DetailReportViewModel MapToViewModel(AuditHeaderDto basic)
        {
            return new DetailReportViewModel
            {
                AuditId = basic.Id,
                ReportNo = basic.ReportNo,
                AuditType = basic.AuditType == "Mystery Audit" ? "Regular Audit" : basic.AuditType,
                TanggalAudit = (basic.SubmitDate.GetValueOrDefault() == DateTime.MinValue)
                ?basic.UpdatedDate
                :basic.SubmitDate.GetValueOrDefault(),
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
                KomentarManager = basic.KomentarManager
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
                MediaPath = "https://epas.zarata.co.id" + x.media_path
            }).ToList();
        }

        private async Task<List<FotoTemuan>> GetMediaReportFAsync(IDbConnection conn, string id)
        {
            string sql = @"SELECT am.media_type,am.media_path
                        FROM trx_audit_media am
                        JOIN trx_audit_checklist ac
                          ON am.trx_audit_id = ac.trx_audit_id
                          AND am.master_questioner_detail_id = ac.master_questioner_detail_id
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
                              MediaPath = "https://epas.zarata.co.id" + m.media_path
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

            // Log ke Output
            System.Diagnostics.Debug.WriteLine("[DEBUG] Perhitungan Total Skor:");
            foreach (var line in debug)
                System.Diagnostics.Debug.WriteLine(line);
            System.Diagnostics.Debug.WriteLine($"TOTAL: {totalScore:0.##} / {maxScore:0.##} = {model.FinalScore:0.##}%");
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
