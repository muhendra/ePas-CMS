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

        var result = new List<SpbuViewModel>();

        foreach (var item in audits)
        {
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
            ) AND mqd.type = 'QUESTION'";

            using var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

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
                Tahun = "2022",
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


    [HttpGet("audit/detail/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
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
                               Alamat = s.address
                           }).FirstOrDefaultAsync();

        if (audit == null) return NotFound();

        var sql = @"
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
                SELECT ta.master_questioner_checklist_id 
                FROM trx_audit ta 
                WHERE ta.id = @id
            )
        ORDER BY mqd.order_no";

        using var conn = _context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var checklistData = (await conn.QueryAsync<ChecklistFlatItem>(sql, new { id })).ToList();

        audit.Elements = BuildHierarchy(checklistData);

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

        return View(audit);
    }

    private List<AuditChecklistNode> BuildHierarchy(List<ChecklistFlatItem> flatList)
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
}