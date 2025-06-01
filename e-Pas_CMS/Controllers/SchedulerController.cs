using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using e_Pas_CMS.ViewModels;
using e_Pas_CMS.Data;
using Microsoft.EntityFrameworkCore;

namespace e_Pas_CMS.Controllers
{
    [Route("Scheduler")]
    [Authorize]
    public class SchedulerController : Controller
    {
        private readonly EpasDbContext _context;

        public SchedulerController(EpasDbContext context)
        {
            _context = context;
        }

        public IActionResult Add()
        {
            return View();
        }

        // POST: /Scheduler/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Add(SchedulerViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // TODO: Simpan data scheduler ke database
            // contoh simpan:
            // _context.Schedulers.Add(new Scheduler { ... });

            TempData["Success"] = "Scheduler berhasil ditambahkan.";
            return RedirectToAction("Add");
        }

        [HttpGet("GetSpbuList")]
        public async Task<IActionResult> GetSpbuList(int page = 1, int pageSize = 10, string search = "")
        {
            var query = _context.spbus
                .Where(x => x.status == "ACTIVE");

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();

                query = query.Where(x =>
                    x.spbu_no.ToLower().Contains(search) ||
                    (x.address ?? "").ToLower().Contains(search) ||
                    (x.city_name ?? "").ToLower().Contains(search) ||
                    (x.province_name ?? "").ToLower().Contains(search)
                );
            }

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var items = await query
                .OrderBy(x => x.spbu_no)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.id,
                    x.spbu_no,
                    x.region,
                    x.province_name,
                    x.city_name,
                    x.address
                })
                .ToListAsync();

            return Json(new
            {
                currentPage = page,
                totalPages = totalPages,
                items = items
            });
        }

    }
}
