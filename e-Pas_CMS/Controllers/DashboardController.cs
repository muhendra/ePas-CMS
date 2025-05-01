using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace e_Pas_CMS.Controllers
{
    public class DashboardController : Controller
    {
        private readonly EpasDbContext _context;

        public DashboardController(EpasDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Nasional()
        {
            var audits = await (from s in _context.spbus
                                join a in _context.trx_audits on s.id equals a.spbu_id
                                join u in _context.app_users on a.app_user_id equals u.id into aud
                                from u in aud.DefaultIfEmpty()
                                where a.status == "UNDER_REVIEW" || a.status == "VERIFIED"
                                select new SpbuViewModel
                                {
                                    Id = a.id,
                                    NoSpbu = s.spbu_no,
                                    Alamat = s.address,
                                    TipeSpbu = s.type,
                                    Tahun = a.created_date.ToString("yyyy"),
                                    Audit = "DAE",
                                    Score = 0, // isi 0 atau hitung jika ingin
                                    Good = "certified",
                                    Excelent = "certified",
                                    Provinsi = s.province_name,
                                    Kota = s.city_name,
                                    NamaAuditor = u.name,
                                    Report = a.report_no,
                                    TanggalSubmit = a.audit_execution_time ?? DateTime.MinValue,
                                    Status = a.status,
                                    Komplain = a.status == "FAIL" ? "ADA" : "Tidak Ada",
                                    Banding = a.audit_level == "Re-Audit" ? "ADA" : "Tidak Ada",
                                    Type = a.audit_type,
                                    Rayon = "I"
                                }).ToListAsync();

            return View(audits);
        }

        public async Task<IActionResult> Province(string province = null)
        {
            var audits = await (from s in _context.spbus
                                join a in _context.trx_audits on s.id equals a.spbu_id
                                join u in _context.app_users on a.app_user_id equals u.id into aud
                                from u in aud.DefaultIfEmpty()
                                where (a.status == "UNDER_REVIEW" || a.status == "VERIFIED") &&
                                      (string.IsNullOrEmpty(province) || s.province_name == province)
                                select new SpbuViewModel
                                {
                                    Id = a.id,
                                    NoSpbu = s.spbu_no,
                                    Alamat = s.address,
                                    TipeSpbu = s.type,
                                    Tahun = a.created_date.ToString("yyyy"),
                                    Audit = "DAE",
                                    Score = 0, // bisa dihitung jika sudah ada rumus
                                    Good = "certified",
                                    Excelent = "certified",
                                    Provinsi = s.province_name,
                                    Kota = s.city_name,
                                    NamaAuditor = u.name,
                                    Report = a.report_no,
                                    TanggalSubmit = a.audit_execution_time ?? DateTime.MinValue,
                                    Status = a.status,
                                    Komplain = a.status == "FAIL" ? "ADA" : "Tidak Ada",
                                    Banding = a.audit_level == "Re-Audit" ? "ADA" : "Tidak Ada",
                                    Type = a.audit_type,
                                    Rayon = "I"
                                }).ToListAsync();

            ViewBag.SelectedProvince = province;
            ViewBag.ProvinceList = await _context.spbus
                .Where(x => !string.IsNullOrEmpty(x.province_name))
                .Select(x => x.province_name)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return View(audits); // Views/Dashboard/Province.cshtml
        }

        public async Task<IActionResult> Regional(string region = null)
        {
            var audits = await (from s in _context.spbus
                                join a in _context.trx_audits on s.id equals a.spbu_id
                                join u in _context.app_users on a.app_user_id equals u.id into aud
                                from u in aud.DefaultIfEmpty()
                                where (a.status == "UNDER_REVIEW" || a.status == "VERIFIED") &&
                                      (string.IsNullOrEmpty(region) || s.region == region)
                                select new SpbuViewModel
                                {
                                    Id = a.id,
                                    NoSpbu = s.spbu_no,
                                    Alamat = s.address,
                                    TipeSpbu = s.type,
                                    Tahun = a.created_date.ToString("yyyy"),
                                    Audit = "DAE",
                                    Score = 0,
                                    Good = "certified",
                                    Excelent = "certified",
                                    Provinsi = s.province_name,
                                    Kota = s.city_name,
                                    NamaAuditor = u.name,
                                    Report = a.report_no,
                                    TanggalSubmit = a.audit_execution_time ?? DateTime.MinValue,
                                    Status = a.status,
                                    Komplain = a.status == "FAIL" ? "ADA" : "Tidak Ada",
                                    Banding = a.audit_level == "Re-Audit" ? "ADA" : "Tidak Ada",
                                    Type = a.audit_type,
                                    Rayon = "I"
                                }).ToListAsync();

            ViewBag.SelectedRegion = region;
            ViewBag.RegionList = await _context.spbus
                .Where(x => !string.IsNullOrEmpty(x.region))
                .Select(x => x.region)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            return View("Regional", audits);
        }

        public async Task<IActionResult> Spbu(string search = null)
        {
            var query = _context.spbus.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                query = query.Where(s =>
                    s.spbu_no.ToLower().Contains(search) ||
                    s.province_name.ToLower().Contains(search) ||
                    s.city_name.ToLower().Contains(search) ||
                    s.owner_name.ToLower().Contains(search) ||
                    s.manager_name.ToLower().Contains(search)
                );
            }

            var spbuList = await query
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
                }).ToListAsync();

            ViewBag.Search = search;
            return View(spbuList);
        }


    }
}
