using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class SpbuController : Controller
{
    private readonly EpasDbContext _context;

    public SpbuController(EpasDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var list = await (from s in _context.spbus
                          join a in _context.trx_audits on s.id equals a.spbu_id
                          join u in _context.app_users on a.app_user_id equals u.id into aud
                          from u in aud.DefaultIfEmpty()
                          select new SpbuViewModel
                          {
                              Id = a.id,
                              NoSpbu = s.spbu_no,
                              Provinsi = s.province_name,
                              Kota = s.city_name,
                              NamaAuditor = u.name,
                              Report = a.report_no,
                              TanggalSubmit = (DateTime)a.audit_execution_time,
                              Status = a.status,
                              Komplain = a.status == "FAIL" ? "ADA" : "Tidak Ada", // contoh logic dummy
                              Banding = a.audit_level == "Re-Audit" ? "ADA" : "Tidak Ada",
                              Type = a.audit_type
                          }).Distinct().ToListAsync();

        return View(list);
    }

    //public async Task<IActionResult> Index()
    //{
    //    var list = await (
    //        from a in _context.trx_audits
    //        join s in _context.spbus on a.spbu_id equals s.id
    //        join u in _context.app_users on a.app_user_id equals u.id into aud
    //        from u in aud.DefaultIfEmpty()
    //        orderby a.audit_execution_time descending
    //        group new { a, s, u } by s.id into g
    //        select new SpbuViewModel
    //        {
    //            Id = g.First().a.id,
    //            NoSpbu = g.First().s.spbu_no,
    //            Provinsi = g.First().s.province_name,
    //            Kota = g.First().s.city_name,
    //            NamaAuditor = g.First().u != null ? g.First().u.name : "-",
    //            Report = g.First().a.report_no,
    //            TanggalSubmit = g.First().a.audit_execution_time ?? DateTime.MinValue,
    //            Status = g.First().a.status,
    //            Komplain = g.First().a.status == "FAIL" ? "ADA" : "Tidak Ada",
    //            Banding = g.First().a.audit_level == "Re-Audit" ? "ADA" : "Tidak Ada",
    //        }).ToListAsync();

    //    return View(list);
    //}
}
