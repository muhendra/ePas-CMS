using e_Pas_CMS.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace YourApp.Controllers
{
    [Authorize]
    [Route("UploadLibrary")]
    public class UploadLibraryController : Controller
    {
        private const string BasePath = "/var/www/epas-asset/wwwroot/uploads";

        private readonly EpasDbContext _context; // 🔁 ganti sesuai DbContext kamu

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
            try
            {
                return Directory.EnumerateFileSystemEntries(dirPath).Count();
            }
            catch
            {
                return null;
            }
        }

        // ✅ ambil mapping auditId(uuid folder) -> spbu_no dalam 1 query
        private async Task<Dictionary<string, string>> GetAuditIdToSpbuNoMapAsync(List<string> auditIds)
        {
            // auditIds is folder names that are valid GUIDs
            // join trx_audit -> spbu
            var rows = await (
                from a in _context.trx_audits
                join s in _context.spbus on a.spbu_id equals s.id
                where auditIds.Contains(a.id)
                select new { a.id, s.spbu_no }
            ).ToListAsync();

            // kalau ada duplikat (harusnya tidak), ambil first
            return rows
                .GroupBy(x => x.id)
                .ToDictionary(g => g.Key, g => g.First().spbu_no);
        }

        public class LibraryItemVm
        {
            public string Name { get; set; } = "";          // folder/file name asli
            public string DisplayName { get; set; } = "";   // ✅ yang ditampilkan di UI
            public bool IsMappedFromAudit { get; set; }     // ✅ badge/info
            public string RelPath { get; set; } = "";
            public bool IsDir { get; set; }
            public long? SizeBytes { get; set; }            // file size
            public int? ChildCount { get; set; }            // folder: item count (non-recursive)
            public DateTime LastWriteTime { get; set; }
        }

        public class LibraryVm
        {
            public string CurrentRel { get; set; } = "";
            public string? Query { get; set; }

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
        public async Task<IActionResult> Index(string? folder = "", string? q = "", int page = 1, int pageSize = 50)
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

            var vm = new LibraryVm
            {
                CurrentRel = rel,
                Query = q,
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

            // materialize dulu supaya bisa ambil list audit IDs dan hitung count folder
            var dirList = dirs.OrderBy(d => d.Name).ToList();
            var fileList = files.OrderBy(f => f.Name).ToList();

            // ambil folder yang valid GUID (audit id)
            var auditIds = dirList
                .Select(d => d.Name)
                .Where(n => Guid.TryParse(n, out _))
                .ToList();

            var auditMap = auditIds.Any()
                ? await GetAuditIdToSpbuNoMapAsync(auditIds)
                : new Dictionary<string, string>();

            var folderItems = dirList.Select(d =>
            {
                var mapped = auditMap.TryGetValue(d.Name, out var spbuNo);

                return new LibraryItemVm
                {
                    Name = d.Name,
                    DisplayName = mapped ? spbuNo : d.Name, // ✅ tampil spbu_no kalau ada mapping
                    IsMappedFromAudit = mapped,
                    IsDir = true,
                    RelPath = string.IsNullOrEmpty(rel) ? d.Name : $"{rel}/{d.Name}",
                    LastWriteTime = d.LastWriteTime,
                    SizeBytes = null,
                    ChildCount = SafeCountEntries(d.FullName)
                };
            });

            var fileItems = fileList.Select(f => new LibraryItemVm
            {
                Name = f.Name,
                DisplayName = f.Name,
                IsMappedFromAudit = false,
                IsDir = false,
                RelPath = string.IsNullOrEmpty(rel) ? f.Name : $"{rel}/{f.Name}",
                LastWriteTime = f.LastWriteTime,
                SizeBytes = f.Length,
                ChildCount = null
            });

            var allItems = folderItems.Concat(fileItems).ToList();

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

        [HttpPost("DeleteFile")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteFile(
            string path,
            string? returnFolder = "",
            string? q = "",
            int page = 1,
            int pageSize = 50
        )
        {
            var rel = NormalizeAndValidateRelative(path);
            var physical = ToPhysicalPathFromRel(rel);

            if (System.IO.File.Exists(physical))
                System.IO.File.Delete(physical);

            return RedirectToAction(nameof(Index), new { folder = returnFolder, q, page, pageSize });
        }

        [HttpPost("DeleteFolder")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteFolder(
            string path,
            string? returnFolder = "",
            string? q = "",
            int page = 1,
            int pageSize = 50
        )
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

            return RedirectToAction(nameof(Index), new { folder = returnFolder, q, page, pageSize });
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
    }
}
