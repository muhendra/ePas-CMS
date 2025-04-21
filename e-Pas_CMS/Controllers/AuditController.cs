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
        var list = await (from s in _context.spbus
                          join a in _context.trx_audits on s.id equals a.spbu_id
                          join u in _context.app_users on a.app_user_id equals u.id into aud
                          from u in aud.DefaultIfEmpty()
                          where a.status == "UNDER_REVIEW" || a.status == "VERIFIED"
                          select new SpbuViewModel
                          {
                              Id = a.id,
                              NoSpbu = s.spbu_no,
                              Rayon = "I",
                              Alamat = s.address,
                              TipeSpbu = s.type,
                              Tahun = "2022",
                              Audit = "DAE",
                              Score = 0,
                              Good = "certified",
                              Excelent = "certified",
                              Provinsi = s.province_name,
                              Kota = s.city_name,
                              NamaAuditor = u.name,
                              Report = a.report_no,
                              TanggalSubmit = (DateTime)a.audit_execution_time,
                              Status = a.status,
                              Komplain = a.status == "FAIL" ? "ADA" : "Tidak Ada",
                              Banding = a.audit_level == "Re-Audit" ? "ADA" : "Tidak Ada",
                              Type = a.audit_type
                          }).Distinct().ToListAsync();
        return View(list);
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

    //public async Task<IActionResult> Detail(string id)
    //{
    //    var audit = await (from ta in _context.trx_audits
    //                       join au in _context.app_users on ta.app_user_id equals au.id
    //                       join s in _context.spbus on ta.spbu_id equals s.id
    //                       where ta.id == id
    //                       select new DetailAuditViewModel
    //                       {
    //                           ReportNo = ta.report_prefix + ta.report_no,
    //                           NamaAuditor = au.name,
    //                           TanggalSubmit = ta.audit_execution_time,
    //                           Status = ta.status,
    //                           SpbuNo = s.spbu_no,
    //                           Provinsi = s.province_name,
    //                           Kota = s.city_name,
    //                           Alamat = s.address
    //                       }).FirstOrDefaultAsync();

    //    if (audit == null) return NotFound();

    //    var checklistData = await (from mqd in _context.master_questioner_details
    //                               join tac in _context.trx_audit_checklists
    //                                   on new { K1 = mqd.id, K2 = id } equals new { K1 = tac.master_questioner_detail_id, K2 = tac.trx_audit_id } into checklistJoin
    //                               from tac in checklistJoin.DefaultIfEmpty()
    //                               where mqd.master_questioner_id == (
    //                                   _context.trx_audits
    //                                       .Where(ta => ta.id == id)
    //                                       .Select(ta => ta.master_questioner_checklist_id)
    //                                       .FirstOrDefault())
    //                               orderby mqd.order_no
    //                               select new
    //                               {
    //                                   mqd.id,
    //                                   mqd.title,
    //                                   mqd.parent_id,
    //                                   mqd.type,
    //                                   mqd.weight,
    //                                   tac.score_input,
    //                                   tac.score_af
    //                               }).ToListAsync();

    //    var detailMap = checklistData
    //    .GroupBy(x => x.id)
    //    .Select(g => g.First()) // ambil 1 per id
    //    .ToDictionary(x => x.id);

    //    //foreach (var item in checklistData)
    //    //{
    //    //    Console.WriteLine($"id={item.id}, parent_id={(item.parent_id == null ? "NULL" : item.parent_id)}, title={item.title}, type={item.type}");
    //    //}

    //    foreach (var x in checklistData)
    //    {
    //        Console.WriteLine($"id={x.id}, parent_id={(x.parent_id == null ? "NULL" : $"'{x.parent_id}'")}, title='{x.title}', type='{x.type}', weight={x.weight}");
    //    }


    //    var elements = checklistData
    //.Where(x => string.IsNullOrWhiteSpace(x.parent_id) &&
    //            !string.IsNullOrWhiteSpace(x.title) &&
    //            x.title.Trim().ToUpper().Contains("ELEMENT"))
    //.OrderBy(x => x.weight)
    //.Select(elem => new AuditElementViewModel
    //{
    //    Title = elem.title,
    //    TotalScore = elem.score_af ?? 0,
    //    SubElements = checklistData
    //        .Where(sub =>
    //            !string.IsNullOrWhiteSpace(sub.parent_id) &&
    //            sub.parent_id.Trim() == elem.id.Trim() &&
    //            !string.IsNullOrWhiteSpace(sub.title) &&
    //            sub.title.Trim().ToUpper().StartsWith("SUB-ELEMEN"))
    //        .OrderBy(sub => sub.weight)
    //        .Select(sub => new AuditSubElementViewModel
    //        {
    //            Title = sub.title,
    //            TotalScore = sub.score_af ?? 0,
    //            Questions = checklistData
    //                .Where(q =>
    //                    q.parent_id?.Trim() == sub.id?.Trim() &&
    //                    q.type != null &&
    //                    q.type.Trim().ToUpper() == "QUESTION")
    //                .OrderBy(q => q.weight)
    //                .Select(q => new AuditQuestionViewModel
    //                {
    //                    Title = q.title,
    //                    Score = q.score_af ?? 0,
    //                    Answer = 0,
    //                    Options = new List<string> { "A (2,00)", "B (1,50)", "C (1,00)", "D (0,50)", "F (0,00)" },
    //                    MediaUrl = "/upload/sample.jpg"
    //                })
    //                .ToList()
    //        })
    //        .ToList()
    //})
    //.ToList();


    //    audit.Elements = elements;

    //    return View(audit);
    //}
}