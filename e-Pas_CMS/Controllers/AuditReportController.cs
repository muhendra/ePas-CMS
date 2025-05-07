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

namespace e_Pas_CMS.Controllers
{
    public class AuditReportController : Controller
    {
        private readonly EpasDbContext _context;

        public AuditReportController(EpasDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var rawAudits = _context.trx_audits
                .Include(a => a.spbu)
                .Include(a => a.app_user)
                .Where(a => a.status == "UNDER_REVIEW" || a.status == "VERIFIED")
                .ToList();

            var result = rawAudits.Select(a => new AuditReportListViewModel
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
                AuditDate = a.audit_schedule_date.HasValue
                                    ? a.audit_schedule_date.Value.ToDateTime(TimeOnly.MinValue)
                                    : (DateTime?)null,
                SubmitDate = a.audit_execution_time,
                Auditor = a.app_user.name,
                GoodStatus = a.spbu.status_good,
                ExcellentStatus = a.spbu.status_excellent,
                Score = a.spbu.audit_current_score,       
                WTMS = _context.trx_audit_qqs.Where(q => q.trx_audit_id == a.id)
                                  .Average(q => (decimal?)q.quantity_variation_with_measure),
                QQ = _context.trx_audit_qqs.Where(q => q.trx_audit_id == a.id)
                                  .Average(q => (decimal?)q.quantity_variation_in_percentage),
                WMEF = _context.trx_audit_qqs.Where(q => q.trx_audit_id == a.id)
                                  .Average(q => (decimal?)q.observed_density),
                FormatFisik = _context.trx_audit_qqs.Where(q => q.trx_audit_id == a.id)
                                  .Average(q => (decimal?)q.observed_temp),
                CPO = _context.trx_audit_qqs.Where(q => q.trx_audit_id == a.id)
                                  .Average(q => (decimal?)q.density_variation),
                KelasSpbu = "Pasti Pas Excellent" 
            })
            .ToList();

            return View(result);
        }

        [HttpGet("Report/Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            // Pastikan koneksi database siap
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            string headerSql = @"
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
                    s.""year""                            AS Year,
                    s.mor                                 AS Mor,
                    s.sales_area                          AS SalesArea,
                    s.sbm                                 AS Sbm,
                    s.""level""                           AS ClassSpbu,
                    s.phone_number_1                      AS Phone,
                    -- Kolom komentar per kategori (jika ada di trx_audit, gunakan ta.comment_xx, 
                    -- di sini diasumsikan ada dan digunakan. Jika tidak ada, akan bernilai kosong.)
                    ''      AS CommentStaf,
                    ''      AS CommentQuality,
                    ''      AS CommentHsse,
                    ''      AS CommentVisual,
                    ''      AS CommentManager
                FROM trx_audit ta
                JOIN spbu s ON ta.spbu_id = s.id
                WHERE ta.id = @id;
            ";
            var basic = await conn.QueryFirstOrDefaultAsync<AuditHeaderDto>(headerSql, new { id });
            if (basic == null)
            {
                // Jika data audit tidak ditemukan, kembalikan 404.
                return NotFound();
            }

            var model = new DetailReportViewModel
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
                KomentarStaf = "",
                KomentarQuality = "",
                KomentarHSSE = "",
                KomentarVisual = "",
                KomentarManager = ""
            };

            var mediaQuestionList = await conn.QueryAsync<MediaItem>(
                @"SELECT media_type AS MediaType, media_path AS MediaPath FROM trx_audit_media WHERE trx_audit_id = @id AND type = 'QUESTION';",
                new { id }
            );
            model.MediaNotes = mediaQuestionList.ToList();

            var mediaFinalList = await conn.QueryAsync<MediaItem>(
                @"SELECT media_type AS MediaType, media_path AS MediaPath FROM trx_audit_media WHERE trx_audit_id = @id AND type = 'FINAL';",
                new { id }
            );
            model.FinalDocuments = mediaFinalList.ToList();

            var qqData = await conn.QueryAsync<AuditQqCheckItem>(
                @"SELECT nozzle_number AS NozzleNumber,
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
                WHERE trx_audit_id = @id",
                new { id }
            );
            model.QqChecks = qqData.ToList();

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
                        MediaPath = "https://epas.zarata.co.id" + m.media_path
                    }).ToList()
                );

            model.Elements = BuildHierarchy(checklistData, mediaList);

            var nilaiAF = new Dictionary<string, decimal>
            {
                ["A"] = 1.00m,
                ["B"] = 0.80m,
                ["C"] = 0.60m,
                ["D"] = 0.40m,
                ["E"] = 0.20m,
                ["F"] = 0.00m
            };

            foreach (var root in model.Elements)
            {
                HitungScorePerNode(root);
            }

            void HitungScorePerNode(AuditChecklistNode node)
            {
                if (node.Children == null || node.Children.Count == 0)
                {
                    if (node.Type == "QUESTION" && !string.IsNullOrWhiteSpace(node.ScoreInput) && node.ScoreInput != "X")
                    {
                        var input = node.ScoreInput.Trim().ToUpper();
                        if (nilaiAF.TryGetValue(input, out var nilai))
                        {
                            node.ScoreAF = nilai;
                        }
                    }
                }
                else
                {
                    decimal totalScore = 0m;
                    decimal totalBobot = 0m;
                    decimal totalWeight = 0m;

                    foreach (var child in node.Children)
                    {
                        HitungScorePerNode(child);

                        var bobot = child.Weight ?? 1m;

                        if (child.ScoreAF.HasValue)
                        {
                            totalScore += child.ScoreAF.Value * bobot;
                            totalBobot += bobot;
                        }

                        totalWeight += child.Weight ?? 0m;
                    }

                    node.Weight ??= totalWeight; // jika Weight null, isi dengan total bobot anak-anak
                    node.ScoreAF = totalBobot > 0 ? totalScore / totalBobot : null;
                }
            }

            // Skor keseluruhan
            decimal totalNilai = 0m;
            decimal totalBobot = 0m;
            foreach (var q in checklistData.Where(x => x.type == "QUESTION"))
            {
                string input = q.score_input?.Trim().ToUpper();
                decimal bobot = q.weight ?? 0m;

                if (string.IsNullOrWhiteSpace(input) || input == "X")
                    continue;

                if (!nilaiAF.TryGetValue(input, out var nilai))
                    continue;

                totalNilai += nilai * bobot;
                totalBobot += bobot;
            }

            model.TotalScore = totalBobot > 0 ? (totalNilai / totalBobot) * 100m : 0m;
            model.MaxScore = totalBobot;
            model.FinalScore = model.TotalScore;
            model.MinPassingScore = 85.00m;

            ViewBag.AuditId = id;
            return View(model);
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
