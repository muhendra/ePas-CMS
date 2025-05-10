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

namespace e_Pas_CMS.Controllers
{
    [Authorize]
    public class AuditReportController : Controller
    {
        private readonly EpasDbContext _context;

        public AuditReportController(EpasDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, string searchTerm = "")
        {
            var query = _context.trx_audits
                .Include(a => a.spbu)
                .Include(a => a.app_user)
                .Where(a => a.status == "UNDER_REVIEW" || a.status == "VERIFIED");

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

            var result = pagedAudits.Select(a => new AuditReportListViewModel
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
                AuditDate = a.audit_schedule_date?.ToDateTime(TimeOnly.MinValue),
                SubmitDate = a.audit_execution_time,
                Auditor = a.app_user.name,
                GoodStatus = a.spbu.status_good,
                ExcellentStatus = a.spbu.status_excellent,
                Score = a.spbu.audit_current_score,
                WTMS = a.spbu.wtms,
                QQ = a.spbu.qq,
                WMEF = a.spbu.wmef,
                FormatFisik = a.spbu.format_fisik,
                CPO = a.spbu.cpo,
                KelasSpbu = "Pasti Pas Excellent"
            }).ToList();

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

            model.MediaNotes = await GetMediaNotesAsync(conn, id, "QUESTION");
            model.FinalDocuments = await GetMediaNotesAsync(conn, id, "FINAL");


            model.QqChecks = await GetQqCheckDataAsync(conn, id);

            var checklistData = await GetChecklistDataAsync(conn, id);
            var mediaList = await GetMediaPerNodeAsync(conn, id);
            model.Elements = BuildHierarchy(checklistData, mediaList);

            CalculateChecklistScores(model.Elements);
            CalculateOverallScore(model, checklistData);

            ViewBag.AuditId = id;
            return View(model);
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
                (ta.report_prefix || ta.report_no)    AS ReportNo,
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
                (SELECT all_comments FROM comment_per_elemen WHERE root_title = 'Elemen 5') AS KomentarManager
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
                AuditType = basic.AuditType,
                TanggalSubmit = basic.SubmitDate,
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
                  tac.score_x
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
                                node.ScoreAF = val;
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
            var nilaiAF = new Dictionary<string, decimal>
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

            foreach (var q in flatItems.Where(x => x.type == "QUESTION"))
            {
                var input = q.score_input?.Trim();
                decimal weight = q.weight ?? 0m;

                var allowed = (q.score_option ?? "")
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .SelectMany(opt =>
                        opt == "A-F" ? new[] { "A", "B", "C", "D", "E", "F" } : new[] { opt.Trim() })
                    .Concat(new[] { "X" })
                    .Select(x => x.Trim())
                    .Distinct()
                    .ToList();

                if (string.IsNullOrWhiteSpace(input) || !allowed.Contains(input))
                    continue;

                if (input == "X")
                {
                    sumX += weight;
                }
                else if (nilaiAF.TryGetValue(input.ToUpper(), out var af))
                {
                    sumAF += af * weight;
                    sumWeight += weight;
                }
            }

            model.TotalScore = sumAF;
            model.MaxScore = sumWeight;
            model.FinalScore = (sumWeight - sumX) > 0 ? (sumAF / (sumWeight - sumX)) * 100m : 0m;
            model.MinPassingScore = 85.00m;
        }

        private List<AuditChecklistNode> BuildHierarchy(
    List<ChecklistFlatItem> flatList,
    Dictionary<string, List<MediaItem>> mediaList)
        {
            var lookup = flatList.ToLookup(x => x.parent_id);

            List<AuditChecklistNode> BuildChildren(string parentId) =>
                lookup[parentId]
                .OrderBy(x => x.weight)
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
