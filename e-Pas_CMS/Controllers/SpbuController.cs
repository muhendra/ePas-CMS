using e_Pas_CMS.Data;
using e_Pas_CMS.Models;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

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

    public async Task<IActionResult> Export()
    {
        var data = await _context.spbus
            .AsNoTracking()
            .OrderBy(x => x.spbu_no)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("SPBU");

        var headers = GetSpbuHeaders();

        for (int i = 0; i < headers.Count; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#111827");
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }

        int row = 2;

        foreach (var item in data)
        {
            ws.Cell(row, 1).Value = item.spbu_no;
            ws.Cell(row, 2).Value = item.region;
            ws.Cell(row, 3).Value = item.province_name;
            ws.Cell(row, 4).Value = item.city_name;
            ws.Cell(row, 5).Value = item.address;
            ws.Cell(row, 6).Value = item.owner_name;
            ws.Cell(row, 7).Value = item.manager_name;
            ws.Cell(row, 8).Value = item.owner_type;
            ws.Cell(row, 9).Value = item.quater;
            ws.Cell(row, 10).Value = item.year;
            ws.Cell(row, 11).Value = item.mor;
            ws.Cell(row, 12).Value = item.sales_area;
            ws.Cell(row, 13).Value = item.sbm;
            ws.Cell(row, 14).Value = item.sam;
            ws.Cell(row, 15).Value = item.type;
            ws.Cell(row, 16).Value = item.phone_number_1;
            ws.Cell(row, 17).Value = item.phone_number_2;
            ws.Cell(row, 18).Value = item.level;
            ws.Cell(row, 19).Value = item.latitude;
            ws.Cell(row, 20).Value = item.longitude;
            ws.Cell(row, 21).Value = item.audit_current;
            ws.Cell(row, 22).Value = item.audit_next;
            ws.Cell(row, 23).Value = item.status_good;
            ws.Cell(row, 24).Value = item.status_excellent;
            ws.Cell(row, 25).Value = item.audit_current_score;
            ws.Cell(row, 26).Value = item.audit_current_time;
            ws.Cell(row, 27).Value = item.status;
            ws.Cell(row, 28).Value = item.wtms;
            ws.Cell(row, 29).Value = item.qq;
            ws.Cell(row, 30).Value = item.wmef;
            ws.Cell(row, 31).Value = item.format_fisik;
            ws.Cell(row, 32).Value = item.cpo;

            row++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"SPBU_Export_{DateTime.Now:yyyyMMddHHmmss}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName
        );
    }

    public IActionResult DownloadTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Template Import SPBU");

        var headers = GetSpbuHeaders();

        for (int i = 0; i < headers.Count; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#111827");
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }

        ws.Cell(2, 1).Value = "34.12345";
        ws.Cell(2, 2).Value = "1";
        ws.Cell(2, 3).Value = "Jawa Barat";
        ws.Cell(2, 4).Value = "Kota Bogor";
        ws.Cell(2, 5).Value = "Jl. Contoh Alamat";
        ws.Cell(2, 8).Value = "COCO";
        ws.Cell(2, 9).Value = 1;
        ws.Cell(2, 10).Value = DateTime.Now.Year;
        ws.Cell(2, 18).Value = "GOOD";
        ws.Cell(2, 19).Value = -6.200000;
        ws.Cell(2, 20).Value = 106.800000;
        ws.Cell(2, 25).Value = 95.50;
        ws.Cell(2, 26).Value = DateTime.Now;
        ws.Cell(2, 27).Value = "ACTIVE";

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Template_Import_SPBU.xlsx"
        );
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "File import belum dipilih.";
            return RedirectToAction(nameof(Index));
        }

        var username = User.Identity?.Name ?? "system";

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);

        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        var rows = ws.RowsUsed().Skip(1);

        int inserted = 0;
        int updated = 0;

        foreach (var row in rows)
        {
            var spbuNo = GetString(row.Cell(1));

            if (string.IsNullOrWhiteSpace(spbuNo))
                continue;

            var entity = await _context.spbus
                .FirstOrDefaultAsync(x => x.spbu_no == spbuNo);

            bool isNew = entity == null;

            if (isNew)
            {
                entity = new spbu
                {
                    id = Guid.NewGuid().ToString(),
                    spbu_no = spbuNo,
                    created_by = username,
                    created_date = DateTime.Now
                };

                _context.spbus.Add(entity);
                inserted++;
            }
            else
            {
                updated++;
            }

            entity.region = GetString(row.Cell(2));
            entity.province_name = GetString(row.Cell(3));
            entity.city_name = GetString(row.Cell(4));
            entity.address = GetString(row.Cell(5));
            entity.owner_name = GetString(row.Cell(6));
            entity.manager_name = GetString(row.Cell(7));
            entity.owner_type = GetString(row.Cell(8));
            entity.quater = GetInt(row.Cell(9));
            entity.year = GetInt(row.Cell(10));
            entity.mor = GetString(row.Cell(11));
            entity.sales_area = GetString(row.Cell(12));
            entity.sbm = GetString(row.Cell(13));
            entity.sam = GetString(row.Cell(14));
            entity.type = GetString(row.Cell(15));
            entity.phone_number_1 = GetString(row.Cell(16));
            entity.phone_number_2 = GetString(row.Cell(17));
            entity.level = GetString(row.Cell(18));
            entity.latitude = GetDouble(row.Cell(19));
            entity.longitude = GetDouble(row.Cell(20));
            entity.audit_current = GetString(row.Cell(21));
            entity.audit_next = GetString(row.Cell(22));
            entity.status_good = GetString(row.Cell(23));
            entity.status_excellent = GetString(row.Cell(24));
            entity.audit_current_score = GetDecimal(row.Cell(25));
            entity.audit_current_time = GetDateTime(row.Cell(26));
            entity.status = GetString(row.Cell(27)) ?? "ACTIVE";
            entity.wtms = GetDecimal(row.Cell(28)) ?? 0;
            entity.qq = GetDecimal(row.Cell(29)) ?? 0;
            entity.wmef = GetDecimal(row.Cell(30)) ?? 0;
            entity.format_fisik = GetDecimal(row.Cell(31)) ?? 0;
            entity.cpo = GetDecimal(row.Cell(32)) ?? 0;

            entity.updated_by = username;
            entity.updated_date = DateTime.Now;
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Import berhasil. Insert: {inserted}, Update: {updated}.";
        return RedirectToAction(nameof(Index));
    }

    private static List<string> GetSpbuHeaders()
    {
        return new List<string>
    {
        "spbu_no",
        "region",
        "province_name",
        "city_name",
        "address",
        "owner_name",
        "manager_name",
        "owner_type",
        "quater",
        "year",
        "mor",
        "sales_area",
        "sbm",
        "sam",
        "type",
        "phone_number_1",
        "phone_number_2",
        "level",
        "latitude",
        "longitude",
        "audit_current",
        "audit_next",
        "status_good",
        "status_excellent",
        "audit_current_score",
        "audit_current_time",
        "status",
        "wtms",
        "qq",
        "wmef",
        "format_fisik",
        "cpo"
    };
    }

    private static string? GetString(IXLCell cell)
    {
        var value = cell.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? GetInt(IXLCell cell)
    {
        var value = GetString(cell);
        return int.TryParse(value, out var result) ? result : null;
    }

    private static double? GetDouble(IXLCell cell)
    {
        var value = GetString(cell)?.Replace(",", ".");
        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static decimal? GetDecimal(IXLCell cell)
    {
        var value = GetString(cell)?.Replace(",", ".");
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static DateTime? GetDateTime(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime();

        var value = GetString(cell);

        if (DateTime.TryParse(value, out var result))
            return result;

        return null;
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
