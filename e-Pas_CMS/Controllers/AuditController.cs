using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class AuditController : Controller
{
    private readonly EpasDbContext _context;

    public AuditController(EpasDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var audits = await (from s in _context.spbus
                            join a in _context.trx_audits on s.id equals a.spbu_id
                            join u in _context.app_users on a.app_user_id equals u.id into aud
                            from u in aud.DefaultIfEmpty()
                            where a.status == "UNDER_REVIEW" || a.status == "VERIFIED"
                            select new
                            {
                                Audit = a,
                                Spbu = s,
                                AuditorName = u.name
                            }).ToListAsync();

        using var conn = _context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var result = new List<SpbuViewModel>();

        foreach (var item in audits)
        {
            var checkSql = @"
                SELECT COUNT(*) 
                FROM master_questioner_detail mqd 
                INNER JOIN trx_audit_checklist tac2 
                    ON mqd.id = tac2.master_questioner_detail_id
                WHERE tac2.score_af IS NULL
                  AND mqd.type != 'TITLE'";

            var needsUpdate = await conn.ExecuteScalarAsync<int>(checkSql, new { id = item.Audit.id });

            if (needsUpdate > 0)
            {
                var updateSql = @"
                    UPDATE trx_audit_checklist tac
                    SET score_af = CASE tac.score_input
                        WHEN 'A' THEN 1.00
                        WHEN 'B' THEN 0.80
                        WHEN 'C' THEN 0.60
                        WHEN 'D' THEN 0.40
                        WHEN 'E' THEN 0.20
                        WHEN 'F' THEN 0.00
                        ELSE NULL
                    END
                    FROM master_questioner_detail mqd
                    WHERE tac.master_questioner_detail_id = mqd.id
                      AND tac.score_af IS NULL
                      AND mqd.type != 'TITLE'";

                await conn.ExecuteAsync(updateSql, new { id = item.Audit.id });
            }

            var sql = @"
                SELECT mqd.weight, tac.score_input
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

            var checklist = (await conn.QueryAsync<(decimal? weight, string score_input)>(sql, new { id = item.Audit.id })).ToList();

            decimal totalScore = 0, maxScore = 0;

            foreach (var q in checklist)
            {
                decimal weight = q.weight ?? 0;
                decimal scoreValue = q.score_input switch
                {
                    "A" => 1.00m,
                    "B" => 0.80m,
                    "C" => 0.60m,
                    "D" => 0.40m,
                    "E" => 0.20m,
                    "F" => 0.00m,
                    _ => 0.00m
                };

                totalScore += scoreValue * weight;
                maxScore += weight;
            }

            decimal finalScore = maxScore > 0 ? totalScore / maxScore * 100 : 0;

            result.Add(new SpbuViewModel
            {
                Id = item.Audit.id,
                NoSpbu = item.Spbu.spbu_no,
                Rayon = "I",
                Alamat = item.Spbu.address,
                TipeSpbu = item.Spbu.type,
                Tahun = item.Audit.created_date.ToString("yyyy"),
                Audit = "DAE",
                Score = finalScore,
                Good = "certified",
                Excelent = "certified",
                Provinsi = item.Spbu.province_name,
                Kota = item.Spbu.city_name,
                NamaAuditor = item.AuditorName,
                Report = item.Audit.report_no,
                TanggalSubmit = (DateTime)item.Audit.audit_execution_time,
                Status = item.Audit.status,
                Komplain = item.Audit.status == "FAIL" ? "ADA" : "Tidak Ada",
                Banding = item.Audit.audit_level == "Re-Audit" ? "ADA" : "Tidak Ada",
                Type = item.Audit.audit_type
            });
        }

        return View(result);
    }

    [HttpGet("Audit/Detail/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        using var conn = _context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var audit = await (from ta in _context.trx_audits
                           join au in _context.app_users on ta.app_user_id equals au.id
                           join s in _context.spbus on ta.spbu_id equals s.id
                           where ta.id == id
                           select new DetailAuditViewModel
                           {
                               ReportNo = ta.report_prefix + ta.report_no,
                               NamaAuditor = au.name,
                               TanggalSubmit = ta.audit_execution_time,
                               Status = ta.status,
                               SpbuNo = s.spbu_no,
                               Provinsi = s.province_name,
                               Kota = s.city_name,
                               Alamat = s.address,
                               Notes = ta.audit_mom_intro,
                               AuditType = ta.audit_type
                           }).FirstOrDefaultAsync();

        if (audit == null) return NotFound();

        // --- Ambil media untuk Catatan Audit (QUESTION)
        var mediaQuestionSql = @"
        SELECT tam.media_path, tam.media_type
        FROM trx_audit_media tam
        WHERE tam.trx_audit_id = @id
          AND tam.type = 'QUESTION'
    ";

        var mediaQuestionList = (await conn.QueryAsync<(string media_path, string media_type)>(mediaQuestionSql, new { id })).ToList();

        if ((audit.AuditType == "Mystery Audit" || audit.AuditType == "Mystery Guest") && mediaQuestionList.Any())
        {
            audit.MediaNotes = mediaQuestionList.Select(m => new MediaItem
            {
                MediaType = m.media_type,
                MediaPath = $"https://epas.zarata.co.id{m.media_path}"
            }).ToList();
        }

        // --- Ambil checklist
        var checklistSql = @"
        SELECT 
            mqd.id,
            mqd.title,
            mqd.description,
            mqd.parent_id,
            mqd.type,
            mqd.weight,
            tac.score_input,
            tac.score_af
        FROM master_questioner_detail mqd
        LEFT JOIN trx_audit_checklist tac 
            ON tac.master_questioner_detail_id = mqd.id 
            AND tac.trx_audit_id = @id
        WHERE 
            mqd.master_questioner_id = (
                SELECT master_questioner_checklist_id 
                FROM trx_audit 
                WHERE id = @id
            )
        ORDER BY mqd.order_no
    ";

        var checklistData = (await conn.QueryAsync<ChecklistFlatItem>(checklistSql, new { id })).ToList();

        // --- Ambil media per checklist node
        var mediaSql = @"
        SELECT 
            master_questioner_detail_id,
            media_type,
            media_path
        FROM trx_audit_media
        WHERE trx_audit_id = @id
          AND master_questioner_detail_id IS NOT NULL
    ";

        var mediaList = (await conn.QueryAsync<(string master_questioner_detail_id, string media_type, string media_path)>(mediaSql, new { id }))
            .GroupBy(m => m.master_questioner_detail_id)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => new MediaItem
                {
                    MediaType = m.media_type,
                    MediaPath = $"https://epas.zarata.co.id{m.media_path}"
                }).ToList()
            );

        audit.Elements = BuildHierarchy(checklistData, mediaList);

        // --- Hitung skor audit
        var questions = checklistData.Where(x => x.type == "QUESTION" && !string.IsNullOrWhiteSpace(x.score_input));
        decimal totalScore = 0;
        decimal maxScore = 0;

        foreach (var q in questions)
        {
            decimal weight = q.weight ?? 0;
            decimal scoreValue = q.score_input switch
            {
                "A" => 1.00m,
                "B" => 0.80m,
                "C" => 0.60m,
                "D" => 0.40m,
                "E" => 0.20m,
                "F" => 0.00m,
                _ => 0.00m
            };

            totalScore += scoreValue * weight;
            maxScore += weight;
        }

        audit.TotalScore = totalScore;
        audit.MaxScore = maxScore;
        audit.FinalScore = maxScore > 0 ? totalScore / maxScore * 100 : 0;

        // --- Ambil data Q&Q
        var qqSql = @"
        SELECT
            nozzle_number AS NozzleNumber,
            du_make AS DuMake,
            du_serial_no AS DuSerialNo,
            product AS Product,
            mode AS Mode,
            quantity_variation_with_measure AS QuantityVariationWithMeasure,
            quantity_variation_in_percentage AS QuantityVariationInPercentage,
            observed_density AS ObservedDensity,
            observed_temp AS ObservedTemp,
            observed_density_15_degree AS ObservedDensity15Degree,
            reference_density_15_degree AS ReferenceDensity15Degree,
            tank_number AS TankNumber,
            density_variation AS DensityVariation
        FROM trx_audit_qq
        WHERE trx_audit_id = @id
    ";

        var qqCheckList = (await conn.QueryAsync<AuditQqCheckItem>(qqSql, new { id })).ToList();
        audit.QqChecks = qqCheckList;

        // --- Ambil media FINAL (Berita Acara)
        var finalMediaSql = @"
        SELECT 
            media_type,
            media_path
        FROM trx_audit_media
        WHERE trx_audit_id = @id
          AND type = 'FINAL'
    ";

        audit.FinalDocuments = (await conn.QueryAsync<(string media_type, string media_path)>(finalMediaSql, new { id }))
            .Select(m => new MediaItem
            {
                MediaType = m.media_type,
                MediaPath = $"https://epas.zarata.co.id{m.media_path}"
            }).ToList();

        return View(audit);
    }


    private List<AuditChecklistNode> BuildHierarchy(List<ChecklistFlatItem> flatList, Dictionary<string, List<MediaItem>> mediaList)
    {
        var lookup = flatList.ToLookup(x => x.parent_id);

        List<AuditChecklistNode> BuildChildren(string parentId)
        {
            return lookup[parentId]
                .OrderBy(x => x.weight)
                .Select(item => new AuditChecklistNode
                {
                    Id = item.id,
                    Title = item.title,
                    Description = item.description,
                    Type = item.type,
                    Weight = item.weight,
                    ScoreInput = item.score_input,
                    ScoreAF = item.score_af,
                    ScoreX = item.score_x,
                    MediaItems = mediaList.ContainsKey(item.id) ? mediaList[item.id] : new List<MediaItem>(), // << disini bro tempelin
                    Children = BuildChildren(item.id)
                })
                .ToList();
        }

        return BuildChildren(flatList.Any(x => x.parent_id == null) ? null : "");
    }

    [HttpPost("audit/approve/{id}")]
    public async Task<IActionResult> Approve(string id)
    {
        var audit = await _context.trx_audits.FirstOrDefaultAsync(x => x.id == id);
        if (audit == null)
            return NotFound();

        audit.status = "VERIFIED";
        _context.trx_audits.Update(audit);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Laporan audit telah disetujui.";
        return RedirectToAction("Detail", new { id });
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
}
