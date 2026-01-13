using e_Pas_CMS.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YourApp.Controllers
{
    [Authorize]
    [Route("UploadLibrary")]
    public class UploadLibraryController : Controller
    {
        private const string BasePath = "/var/www/epas-asset/wwwroot/uploads";
        private readonly EpasDbContext _context;

        public UploadLibraryController(EpasDbContext context)
        {
            _context = context;
        }

        private static string NormalizeAndValidateRelative(string? rel)
        {
            rel ??= "";
            rel = rel.Replace('\\', '/').Trim();

            if (rel.StartsWith("/")) rel = rel.TrimStart('/');

            var baseFull = Path.GetFullPath(BasePath);
            var combined = Path.GetFullPath(Path.Combine(baseFull, rel));

            if (!combined.StartsWith(baseFull + Path.DirectorySeparatorChar) && combined != baseFull)
                throw new InvalidOperationException("Invalid path.");

            var cleanedRel = Path.GetRelativePath(baseFull, combined).Replace('\\', '/');
            return cleanedRel == "." ? "" : cleanedRel;
        }

        private static string ToPhysicalPathFromRel(string rel)
        {
            var baseFull = Path.GetFullPath(BasePath);
            return Path.GetFullPath(Path.Combine(baseFull, rel ?? ""));
        }

        // ✅ count item langsung di folder tsb (tidak recursive) - ringan
        private static int? SafeCountEntries(string dirPath)
        {
            try { return Directory.EnumerateFileSystemEntries(dirPath).Count(); }
            catch { return null; }
        }

        private static bool TryParseMonthKey(string? ym, out int year, out int month)
        {
            year = 0; month = 0;
            if (string.IsNullOrWhiteSpace(ym)) return false;

            // expected: yyyy-MM
            var parts = ym.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out year)) return false;
            if (!int.TryParse(parts[1], out month)) return false;
            if (month < 1 || month > 12) return false;
            return true;
        }

        private static string MonthKey(DateTime dt) => dt.ToString("yyyy-MM");

        private sealed class AuditMeta
        {
            public string SpbuNo { get; set; } = "";
            public DateTime AuditDate { get; set; }
        }

        // ✅ mapping auditId(uuid folder) -> {spbu_no, audit_date} dalam 1 query
        private async Task<Dictionary<string, AuditMeta>> GetAuditMetaMapAsync(List<string> auditIds)
        {
            var rows = await (
                from a in _context.trx_audits
                join s in _context.spbus on a.spbu_id equals s.id
                where auditIds.Contains(a.id)
                select new
                {
                    AuditId = a.id,
                    SpbuNo = s.spbu_no,
                    AuditDate = (a.audit_execution_time ?? a.created_date)
                }
            ).ToListAsync();

            return rows
                .GroupBy(x => x.AuditId)
                .ToDictionary(
                    g => g.Key,
                    g => new AuditMeta
                    {
                        SpbuNo = g.First().SpbuNo,
                        AuditDate = g.First().AuditDate
                    }
                );
        }

        public class LibraryItemVm
        {
            public string Name { get; set; } = "";          // original file/folder name
            public string DisplayName { get; set; } = "";   // shown name (SPBU no)
            public bool IsMappedFromAudit { get; set; }

            public string RelPath { get; set; } = "";
            public bool IsDir { get; set; }

            public long? SizeBytes { get; set; }            // file size
            public int? ChildCount { get; set; }            // folder item count
            public DateTime LastWriteTime { get; set; }     // fs time

            // ✅ date used for month filter:
            // audit folder -> audit_date from DB; non-audit -> LastWriteTime
            public DateTime FilterDate { get; set; }
        }

        public class LibraryVm
        {
            public string CurrentRel { get; set; } = "";
            public string? Query { get; set; }

            // ✅ month filter: "yyyy-MM"
            public string? Month { get; set; }
            public List<string> AvailableMonths { get; set; } = new(); // from current folder listing

            public List<(string name, string rel)> Breadcrumbs { get; set; } = new();
            public List<LibraryItemVm> Items { get; set; } = new();

            public int Page { get; set; }
            public int PageSize { get; set; }
            public int TotalItems { get; set; }
            public int TotalPages { get; set; }

            public int[] AllowedPageSizes { get; set; } = new[] { 20, 50, 100, 200 };
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(
            string? folder = "",
            string? q = "",
            string? month = "",
            int page = 1,
            int pageSize = 50
        )
        {
            if (!Directory.Exists(BasePath))
                return Problem($"BasePath not found: {BasePath}", statusCode: 500);

            if (page < 1) page = 1;

            var allowedSizes = new[] { 20, 50, 100, 200 };
            if (!allowedSizes.Contains(pageSize)) pageSize = 50;

            var rel = NormalizeAndValidateRelative(folder);
            var physical = ToPhysicalPathFromRel(rel);

            if (!Directory.Exists(physical))
            {
                rel = "";
                physical = ToPhysicalPathFromRel(rel);
            }

            q = (q ?? "").Trim();
            month = (month ?? "").Trim();
            if (!TryParseMonthKey(month, out _, out _)) month = ""; // invalid -> reset

            var vm = new LibraryVm
            {
                CurrentRel = rel,
                Query = q,
                Month = string.IsNullOrEmpty(month) ? null : month,
                Page = page,
                PageSize = pageSize,
                AllowedPageSizes = allowedSizes
            };

            // breadcrumbs
            vm.Breadcrumbs.Add(("uploads", ""));
            if (!string.IsNullOrEmpty(rel))
            {
                var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var acc = new List<string>();
                foreach (var p in parts)
                {
                    acc.Add(p);
                    vm.Breadcrumbs.Add((p, string.Join('/', acc)));
                }
            }

            var dirInfo = new DirectoryInfo(physical);

            IEnumerable<DirectoryInfo> dirs = dirInfo.EnumerateDirectories();
            IEnumerable<FileInfo> files = dirInfo.EnumerateFiles();

            if (!string.IsNullOrEmpty(q))
            {
                dirs = dirs.Where(d => d.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
                files = files.Where(f => f.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var dirList = dirs.OrderBy(d => d.Name).ToList();
            var fileList = files.OrderBy(f => f.Name).ToList();

            // audit folder IDs (valid GUID)
            var auditIds = dirList
                .Select(d => d.Name)
                .Where(n => Guid.TryParse(n, out _))
                .ToList();

            var auditMetaMap = auditIds.Any()
                ? await GetAuditMetaMapAsync(auditIds)
                : new Dictionary<string, AuditMeta>();

            var folderItems = dirList.Select(d =>
            {
                var mapped = auditMetaMap.TryGetValue(d.Name, out var meta);

                var fsTime = d.LastWriteTime;
                var filterDate = mapped ? meta!.AuditDate : fsTime;

                return new LibraryItemVm
                {
                    Name = d.Name,
                    DisplayName = mapped ? meta!.SpbuNo : d.Name,
                    IsMappedFromAudit = mapped,
                    IsDir = true,
                    RelPath = string.IsNullOrEmpty(rel) ? d.Name : $"{rel}/{d.Name}",
                    LastWriteTime = fsTime,
                    SizeBytes = null,
                    ChildCount = SafeCountEntries(d.FullName),
                    FilterDate = filterDate
                };
            });

            var fileItems = fileList.Select(f =>
            {
                var fsTime = f.LastWriteTime;
                return new LibraryItemVm
                {
                    Name = f.Name,
                    DisplayName = f.Name,
                    IsMappedFromAudit = false,
                    IsDir = false,
                    RelPath = string.IsNullOrEmpty(rel) ? f.Name : $"{rel}/{f.Name}",
                    LastWriteTime = fsTime,
                    SizeBytes = f.Length,
                    ChildCount = null,
                    FilterDate = fsTime
                };
            });

            var allItems = folderItems.Concat(fileItems).ToList();

            // Available month options from listing (BEFORE filter)
            vm.AvailableMonths = allItems
                .Select(x => MonthKey(x.FilterDate))
                .Distinct()
                .OrderByDescending(x => x)
                .ToList();

            // Apply month filter
            if (!string.IsNullOrEmpty(month) && TryParseMonthKey(month, out int y, out int m))
            {
                allItems = allItems
                    .Where(x => x.FilterDate.Year == y && x.FilterDate.Month == m)
                    .ToList();
            }

            // Pagination
            vm.TotalItems = allItems.Count;
            vm.TotalPages = Math.Max(1, (int)Math.Ceiling(vm.TotalItems / (double)vm.PageSize));
            if (vm.Page > vm.TotalPages) vm.Page = vm.TotalPages;

            vm.Items = allItems
                .Skip((vm.Page - 1) * vm.PageSize)
                .Take(vm.PageSize)
                .ToList();

            return View(vm);
        }

        [HttpGet("Download")]
        public IActionResult Download(string path)
        {
            var rel = NormalizeAndValidateRelative(path);
            var physical = ToPhysicalPathFromRel(rel);

            if (!System.IO.File.Exists(physical))
                return NotFound();

            var fileName = Path.GetFileName(physical);
            var stream = new FileStream(physical, FileMode.Open, FileAccess.Read, FileShare.Read);

            return File(stream, "application/octet-stream", fileName);
        }

        // ✅ Download ZIP per bulan (streaming biar gak timeout)
        // /UploadLibrary/DownloadMonth?folder=&q=&month=2025-11
        [HttpGet("DownloadMonth")]
        public async Task<IActionResult> DownloadMonth(string? folder = "", string? q = "", string? month = "")
        {
            month = (month ?? "").Trim();
            if (!TryParseMonthKey(month, out int y, out int m))
                return BadRequest("Invalid month format. Use yyyy-MM.");

            var rel = NormalizeAndValidateRelative(folder);
            var physical = ToPhysicalPathFromRel(rel);
            if (!Directory.Exists(physical)) return NotFound("Folder not found.");

            q = (q ?? "").Trim();

            var dirInfo = new DirectoryInfo(physical);
            IEnumerable<DirectoryInfo> dirs = dirInfo.EnumerateDirectories();
            IEnumerable<FileInfo> files = dirInfo.EnumerateFiles();

            if (!string.IsNullOrEmpty(q))
            {
                dirs = dirs.Where(d => d.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
                files = files.Where(f => f.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var dirList = dirs.OrderBy(d => d.Name).ToList();
            var fileList = files.OrderBy(f => f.Name).ToList();

            var auditIds = dirList.Select(d => d.Name).Where(n => Guid.TryParse(n, out _)).ToList();
            var auditMetaMap = auditIds.Any()
                ? await GetAuditMetaMapAsync(auditIds)
                : new Dictionary<string, AuditMeta>();

            var zipFiles = new List<(string FullPath, string EntryName)>();

            // folders (recursive)
            foreach (var d in dirList)
            {
                var mapped = auditMetaMap.TryGetValue(d.Name, out var meta);
                var filterDate = mapped ? meta!.AuditDate : d.LastWriteTime;

                if (filterDate.Year != y || filterDate.Month != m)
                    continue;

                var top = mapped ? meta!.SpbuNo : d.Name;
                var baseDir = d.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var baseLen = baseDir.Length + 1;

                foreach (var file in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
                {
                    var relPath = file.Substring(baseLen).Replace('\\', '/');
                    var entryName = $"{top}/{relPath}";
                    zipFiles.Add((file, entryName));
                }
            }

            // files di current folder
            foreach (var f in fileList)
            {
                var filterDate = f.LastWriteTime;
                if (filterDate.Year != y || filterDate.Month != m)
                    continue;

                zipFiles.Add((f.FullName, f.Name));
            }

            if (zipFiles.Count == 0)
                return NotFound("No items in selected month.");

            var downloadName = $"uploads_{(string.IsNullOrEmpty(rel) ? "root" : rel.Replace('/', '_'))}_{month}.zip";
            return await StreamZipToResponseAsync(downloadName, zipFiles, HttpContext.RequestAborted);
        }

        // ✅ Download ZIP per SPBU/folder (FIX: no IOPath, + streaming-ish via temp zip)
        // /UploadLibrary/DownloadSpbu?path=xxxx&month=2026-01 (month optional)
        [HttpGet("DownloadSpbu")]
        public async Task<IActionResult> DownloadSpbu(string path, string? month = "")
        {
            int y = 0, m = 0;
            month = (month ?? "").Trim();
            var useMonthFilter = TryParseMonthKey(month, out y, out m);

            var rel = NormalizeAndValidateRelative(path);
            var physical = ToPhysicalPathFromRel(rel);

            if (!Directory.Exists(physical))
                return NotFound("Folder not found.");

            // ✅ FIX: pakai Path (bukan IOPath)
            var folderName = Path.GetFileName(
                physical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            );
            if (string.IsNullOrWhiteSpace(folderName))
                folderName = "folder";

            // kalau foldernya auditId GUID -> ambil spbu_no buat nama zip
            string displayTopFolder = folderName;
            if (Guid.TryParse(folderName, out _))
            {
                try
                {
                    var metaMap = await GetAuditMetaMapAsync(new List<string> { folderName });
                    if (metaMap.TryGetValue(folderName, out var meta) && !string.IsNullOrWhiteSpace(meta.SpbuNo))
                        displayTopFolder = meta.SpbuNo;
                }
                catch { /* ignore */ }
            }

            // create temp zip (lebih aman untuk browser) - tetap dihapus setelah selesai
            var tmp = Path.Combine(Path.GetTempPath(), $"spbu_{folderName}_{Guid.NewGuid():N}.zip");

            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                AddDirectoryToZipFiltered(zip, physical, displayTopFolder, folderName, useMonthFilter, y, m);
            }

            HttpContext.Response.OnCompleted(() =>
            {
                try { System.IO.File.Delete(tmp); } catch { }
                return Task.CompletedTask;
            });

            var monthSuffix = useMonthFilter ? $"_{month}" : "";
            var downloadName = $"spbu_{displayTopFolder}{monthSuffix}.zip";

            return PhysicalFile(tmp, "application/zip", downloadName);
        }

        // ✅ helper: zip folder recursive + optional filter by file LastWriteTime
        private static void AddDirectoryToZipFiltered(
            ZipArchive zip,
            string sourceDir,
            string displayTopFolder,
            string fallbackFolderName,
            bool useMonthFilter,
            int year,
            int month)
        {
            if (!Directory.Exists(sourceDir)) return;

            var top = string.IsNullOrWhiteSpace(displayTopFolder) ? fallbackFolderName : displayTopFolder;
            var baseLen = sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1;

            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (useMonthFilter)
                    {
                        var info = new FileInfo(file);
                        var dt = info.LastWriteTime;
                        if (dt.Year != year || dt.Month != month) continue;
                    }

                    var relPath = file.Substring(baseLen).Replace('\\', '/');
                    var entryName = $"{top}/{relPath}";
                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
                }
                catch
                {
                    // skip file yang sedang dipakai/locked
                }
            }
        }

        // ✅ Delete per bulan (recursive untuk folder)
        [HttpPost("DeleteMonth")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMonth(string? folder = "", string? q = "", string? month = "", int page = 1, int pageSize = 50)
        {
            month = (month ?? "").Trim();
            if (!TryParseMonthKey(month, out int y, out int m))
            {
                TempData["Error"] = "Invalid month format. Use yyyy-MM.";
                return RedirectToAction(nameof(Index), new { folder, q, month = "", page, pageSize });
            }

            var rel = NormalizeAndValidateRelative(folder);
            var physical = ToPhysicalPathFromRel(rel);
            if (!Directory.Exists(physical))
            {
                TempData["Error"] = "Folder tidak ditemukan.";
                return RedirectToAction(nameof(Index), new { folder = "", q, month, page, pageSize });
            }

            q = (q ?? "").Trim();

            var dirInfo = new DirectoryInfo(physical);
            IEnumerable<DirectoryInfo> dirs = dirInfo.EnumerateDirectories();
            IEnumerable<FileInfo> files = dirInfo.EnumerateFiles();

            if (!string.IsNullOrEmpty(q))
            {
                dirs = dirs.Where(d => d.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
                files = files.Where(f => f.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var dirList = dirs.ToList();
            var fileList = files.ToList();

            var auditIds = dirList.Select(d => d.Name).Where(n => Guid.TryParse(n, out _)).ToList();
            var auditMetaMap = auditIds.Any() ? await GetAuditMetaMapAsync(auditIds) : new Dictionary<string, AuditMeta>();

            int deletedCount = 0;

            // delete folders
            foreach (var d in dirList)
            {
                var mapped = auditMetaMap.TryGetValue(d.Name, out var meta);
                var filterDate = mapped ? meta!.AuditDate : d.LastWriteTime;

                if (filterDate.Year == y && filterDate.Month == m)
                {
                    try
                    {
                        Directory.Delete(d.FullName, recursive: true);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        TempData["Error"] = $"Gagal hapus folder {d.Name}: {ex.Message}";
                        break;
                    }
                }
            }

            // delete files
            foreach (var f in fileList)
            {
                var filterDate = f.LastWriteTime;
                if (filterDate.Year == y && filterDate.Month == m)
                {
                    try
                    {
                        System.IO.File.Delete(f.FullName);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        TempData["Error"] = $"Gagal hapus file {f.Name}: {ex.Message}";
                        break;
                    }
                }
            }

            TempData["Error"] = deletedCount > 0
                ? $"Berhasil delete {deletedCount} item untuk bulan {month}."
                : $"Tidak ada item untuk bulan {month}.";

            return RedirectToAction(nameof(Index), new { folder, q, month, page = 1, pageSize });
        }

        [HttpPost("DeleteFile")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteFile(string path, string? returnFolder = "", string? q = "", string? month = "", int page = 1, int pageSize = 50)
        {
            var rel = NormalizeAndValidateRelative(path);
            var physical = ToPhysicalPathFromRel(rel);

            if (System.IO.File.Exists(physical))
                System.IO.File.Delete(physical);

            return RedirectToAction(nameof(Index), new { folder = returnFolder, q, month, page, pageSize });
        }

        [HttpPost("DeleteFolder")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteFolder(string path, string? returnFolder = "", string? q = "", string? month = "", int page = 1, int pageSize = 50)
        {
            var rel = NormalizeAndValidateRelative(path);
            var physical = ToPhysicalPathFromRel(rel);

            if (Directory.Exists(physical))
            {
                if (Directory.EnumerateFileSystemEntries(physical).Any())
                {
                    TempData["Error"] = "Folder tidak kosong. Hapus isi folder dulu.";
                }
                else
                {
                    Directory.Delete(physical);
                }
            }

            return RedirectToAction(nameof(Index), new { folder = returnFolder, q, month, page, pageSize });
        }

        public static string HumanSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:0.##} {units[unit]}";
        }

        private async Task<IActionResult> StreamZipToResponseAsync(
            string downloadName,
            IEnumerable<(string FullPath, string EntryName)> files,
            CancellationToken ct)
        {
            // Disable buffering supaya response bisa langsung mengalir (penting kalau di-reverse proxy)
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            Response.StatusCode = 200;
            Response.ContentType = "application/zip";
            Response.Headers[HeaderNames.ContentDisposition] = $"attachment; filename=\"{downloadName}\"";
            Response.Headers[HeaderNames.CacheControl] = "no-store";

            // ZipArchive bisa nulis incremental ke response stream
            using (var zip = new ZipArchive(Response.Body, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (fullPath, entryName) in files)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!System.IO.File.Exists(fullPath))
                        continue;

                    var entry = zip.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Fastest);

                    await using var entryStream = entry.Open();
                    await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true);
                    await fs.CopyToAsync(entryStream, 1024 * 128, ct);

                    // flush berkala agar koneksi tetap aktif (dan progress download terasa)
                    await Response.Body.FlushAsync(ct);
                }
            }

            return new EmptyResult();
        }
    }
}
