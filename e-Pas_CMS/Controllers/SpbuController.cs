using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.Models;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

[Authorize]
public class SpbuController : Controller
{
    private readonly EpasDbContext _context;

    public SpbuController(EpasDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, string searchTerm = "", string sortColumn = "NoSpbu", string sortDirection = "asc")
    {
        try
        {
            var query = _context.spbus.AsQueryable();

            // Filtering
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.ToLower();
                query = query.Where(s =>
                    s.spbu_no.ToLower().Contains(searchTerm) ||
                    s.province_name.ToLower().Contains(searchTerm) ||
                    s.city_name.ToLower().Contains(searchTerm) ||
                    s.region.ToLower().Contains(searchTerm) ||
                    s.address.ToLower().Contains(searchTerm));
            }

            // Sorting
            query = sortColumn switch
            {
                "Kota" => sortDirection == "asc" ? query.OrderBy(s => s.city_name) : query.OrderByDescending(s => s.city_name),
                "Provinsi" => sortDirection == "asc" ? query.OrderBy(s => s.province_name) : query.OrderByDescending(s => s.province_name),
                "Rayon" => sortDirection == "asc" ? query.OrderBy(s => s.region) : query.OrderByDescending(s => s.region),
                "NoSpbu" => sortDirection == "asc" ? query.OrderBy(s => s.spbu_no) : query.OrderByDescending(s => s.spbu_no),
                _ => query.OrderBy(s => s.spbu_no)
            };

            var totalItems = await query.CountAsync();

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = items.Select(s =>
            {
                DateTime? parsedAuditCurrent = null;
                DateTime? parsedAuditNext = null;

                if (DateTime.TryParse(s.audit_current, out var dtCurrent) && dtCurrent.Year >= 1000)
                    parsedAuditCurrent = dtCurrent;

                if (DateTime.TryParse(s.audit_next, out var dtNext) && dtNext.Year >= 1000)
                    parsedAuditNext = dtNext;

                return new MasterSpbuViewModel
                {
                    Id = s.id,
                    SpbuNo = s.spbu_no,
                    Region = s.region,
                    ProvinceName = s.province_name,
                    CityName = s.city_name,
                    Address = s.address,
                    OwnerName = s.owner_name,
                    ManagerName = s.manager_name,
                    OwnerType = s.owner_type,
                    Quater = s.quater,
                    Year = s.year,
                    Mor = s.mor,
                    SalesArea = s.sales_area,
                    Sbm = s.sbm,
                    Sam = s.sam,
                    Type = s.type,
                    PhoneNumber1 = s.phone_number_1,
                    PhoneNumber2 = s.phone_number_2,
                    Level = s.level,
                    Latitude = s.latitude,
                    Longitude = s.longitude,
                    AuditCurrent = s.audit_current,
                    AuditNext = s.audit_next,
                    StatusGood = s.status_good,
                    StatusExcellent = s.status_excellent,
                    AuditCurrentScore = s.audit_current_score,
                    AuditCurrentTime = s.audit_current_time,
                    Status = s.status,
                    CreatedBy = s.created_by,
                    CreatedDate = s.created_date,
                    UpdatedBy = s.updated_by,
                    UpdatedDate = s.updated_date,
                    Wtms = s.wtms,
                    Qq = s.qq,
                    Wmef = s.wmef,
                    FormatFisik = s.format_fisik,
                    Cpo = s.cpo
                };
            }).ToList();

            var pagination = new PaginationModel<MasterSpbuViewModel>
            {
                Items = result,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            ViewBag.SearchTerm = searchTerm;
            ViewBag.SortColumn = sortColumn;
            ViewBag.SortDirection = sortDirection;

            return View(pagination);
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Gagal memuat data SPBU.";
            return View(new PaginationModel<MasterSpbuViewModel>
            {
                Items = new List<MasterSpbuViewModel>(),
                PageNumber = 1,
                PageSize = pageSize,
                TotalItems = 0
            });
        }
    }

    [HttpGet]
    public IActionResult Create()
    {
        var auditLevels = _context.master_audit_flows
            .Select(m => m.audit_level)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        ViewBag.AuditLevels = new SelectList(auditLevels);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(spbu model)
    {
        model.id = Guid.NewGuid().ToString();
        model.created_date = DateTime.Now;
        model.updated_date = DateTime.Now;
        model.created_by = User.Identity?.Name ?? "system";
        model.updated_by = User.Identity?.Name ?? "system";

        ModelState.Clear();
        TryValidateModel(model);

        if (ModelState.IsValid)
        {
            _context.spbus.Add(model);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Data SPBU berhasil ditambahkan.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var entry in ModelState)
        {
            foreach (var error in entry.Value.Errors)
            {
                System.Diagnostics.Debug.WriteLine($"Field: {entry.Key} - Error: {error.ErrorMessage}");
            }
        }

        return View(model);
    }

    //[HttpGet("{id}")]
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var spbu = await _context.spbus.FindAsync(id);
        if (spbu == null) return NotFound();

        var auditLevels = await _context.master_audit_flows
            .Select(m => m.audit_level)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        ViewBag.AuditLevels = new SelectList(auditLevels, spbu.audit_next); // auto select current value
        return View(spbu);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, spbu spbu)
    {
        spbu.id = id;

        // Ambil langsung dari DB, termasuk created_by & created_date
        var existingdb = await _context.spbus
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.id == id);


        // Assign ulang untuk update
        spbu.created_by = existingdb.created_by;
        spbu.created_date = existingdb.created_date;
        spbu.updated_by = existingdb.updated_by;
        spbu.updated_date = existingdb.updated_date;

        ModelState.Clear();
        TryValidateModel(spbu);

        if (ModelState.IsValid)
        {
            try
            {
                var existing = await _context.spbus.FindAsync(id);
                if (existing == null)
                    return NotFound();

                // Update hanya field yang bisa diedit dari form
                existing.spbu_no = spbu.spbu_no;
                existing.region = spbu.region;
                existing.province_name = spbu.province_name;
                existing.city_name = spbu.city_name;
                existing.address = spbu.address;
                existing.owner_name = spbu.owner_name;
                existing.manager_name = spbu.manager_name;
                existing.owner_type = spbu.owner_type;
                existing.quater = spbu.quater;
                existing.year = spbu.year;
                existing.mor = spbu.mor;
                existing.sales_area = spbu.sales_area;
                existing.sbm = spbu.sbm;
                existing.sam = spbu.sam;
                existing.type = spbu.type;
                existing.phone_number_1 = spbu.phone_number_1;
                existing.phone_number_2 = spbu.phone_number_2;
                existing.level = spbu.level;
                existing.latitude = spbu.latitude;
                existing.longitude = spbu.longitude;
                existing.audit_current = spbu.audit_current;
                existing.audit_next = spbu.audit_next;
                existing.status_good = spbu.status_good;
                existing.status_excellent = spbu.status_excellent;
                existing.audit_current_score = spbu.audit_current_score;
                existing.audit_current_time = spbu.audit_current_time;
                existing.status = spbu.status;
                existing.wtms = spbu.wtms;
                existing.qq = spbu.qq;
                existing.wmef = spbu.wmef;
                existing.format_fisik = spbu.format_fisik;
                existing.cpo = spbu.cpo;
                existing.created_by = spbu.created_by;
                existing.created_date = spbu.created_date;

                // Update info audit
                existing.updated_by = User.Identity?.Name ?? "system";
                existing.updated_date = DateTime.Now;

                await _context.SaveChangesAsync();

                TempData["Success"] = "Data SPBU berhasil diperbarui.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.spbus.Any(e => e.id == id))
                    return NotFound();
                throw;
            }
        }

        foreach (var kvp in ModelState)
        {
            var key = kvp.Key;
            var errors = kvp.Value.Errors;
            foreach (var error in errors)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Field '{key}': {error.ErrorMessage}");
            }
        }

        return View(spbu);
    }

    public async Task<IActionResult> Delete(string id)
    {
        var spbu = await _context.spbus.FindAsync(id);
        if (spbu == null) return NotFound();
        return View(spbu);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        var spbu = await _context.spbus.FindAsync(id);
        if (spbu != null)
        {
            _context.spbus.Remove(spbu);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(string id)
    {
        var spbu = await _context.spbus.FindAsync(id);
        if (spbu == null) return NotFound();
        return View(spbu);
    }
}
