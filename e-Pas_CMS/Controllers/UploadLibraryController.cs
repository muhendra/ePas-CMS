using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YourApp.Controllers
{
    [Authorize]
    [Route("UploadLibrary")]
    public class UploadLibraryController : Controller
    {
        // ✅ base path FIX sesuai server
        private const string BasePath = "/var/www/epas-asset/wwwroot/uploads";

        private static string NormalizeAndValidateRelative(string? rel)
        {
            rel ??= "";
            rel = rel.Replace('\\', '/').Trim();

            // tidak boleh absolute
            if (rel.StartsWith("/")) rel = rel.TrimStart('/');

            var baseFull = Path.GetFullPath(BasePath);

            // gabung + normalize
            var combined = Path.GetFullPath(Path.Combine(baseFull, rel));

            // pastikan masih di bawah BasePath (anti path traversal)
            if (!combined.StartsWith(baseFull + Path.DirectorySeparatorChar) && combined != baseFull)
                throw new InvalidOperationException("Invalid path.");

            // kembalikan RELATIVE yang bersih (pakai '/')
            var cleanedRel = Path.GetRelativePath(baseFull, combined).Replace('\\', '/');
            return cleanedRel == "." ? "" : cleanedRel;
        }

        private static string ToPhysicalPathFromRel(string rel)
        {
            var baseFull = Path.GetFullPath(BasePath);
            return Path.GetFullPath(Path.Combine(baseFull, rel ?? ""));
        }

        public class LibraryItemVm
        {
            public string Name { get; set; } = "";
            public string RelPath { get; set; } = "";
            public bool IsDir { get; set; }
            public long? SizeBytes { get; set; }
            public DateTime LastWriteTime { get; set; }
        }

        public class LibraryVm
        {
            public string CurrentRel { get; set; } = "";
            public string? Query { get; set; }

            public List<(string name, string rel)> Breadcrumbs { get; set; } = new();

            // ✅ hasil (yang sudah dipaginasi) - biar view gampang
            public List<LibraryItemVm> Items { get; set; } = new();

            // ✅ pagination info
            public int Page { get; set; }
            public int PageSize { get; set; }
            public int TotalItems { get; set; }
            public int TotalPages { get; set; }

            // optional: untuk dropdown
            public int[] AllowedPageSizes { get; set; } = new[] { 20, 50, 100, 200 };
        }


        // ✅ Browse root: /UploadLibrary  (juga /UploadLibrary/Index)
        [HttpGet("")]
        [HttpGet("Index")]
        public IActionResult Index(string? folder = "", string? q = "", int page = 1, int pageSize = 50)
        {
            if (!Directory.Exists(BasePath))
                return Problem($"BasePath not found: {BasePath}", statusCode: 500);

            // guard page/pageSize
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

            // gabung (folder dulu), mapping ke VM
            var folderItems = dirs
                .OrderBy(d => d.Name)
                .Select(d => new LibraryItemVm
                {
                    Name = d.Name,
                    IsDir = true,
                    RelPath = string.IsNullOrEmpty(rel) ? d.Name : $"{rel}/{d.Name}",
                    LastWriteTime = d.LastWriteTime,
                    SizeBytes = null
                });

            var fileItems = files
                .OrderBy(f => f.Name)
                .Select(f => new LibraryItemVm
                {
                    Name = f.Name,
                    IsDir = false,
                    RelPath = string.IsNullOrEmpty(rel) ? f.Name : $"{rel}/{f.Name}",
                    LastWriteTime = f.LastWriteTime,
                    SizeBytes = f.Length
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


        // ✅ /UploadLibrary/Download?path=....
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

        // ✅ /UploadLibrary/DeleteFile (POST)
        [HttpPost("DeleteFile")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteFile(string path, string? returnFolder = "", string? q = "")
        {
            var rel = NormalizeAndValidateRelative(path);
            var physical = ToPhysicalPathFromRel(rel);

            if (System.IO.File.Exists(physical))
                System.IO.File.Delete(physical);

            return RedirectToAction(nameof(Index), new { folder = returnFolder, q });
        }

        // ✅ /UploadLibrary/DeleteFolder (POST) - hanya folder kosong
        [HttpPost("DeleteFolder")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteFolder(string path, string? returnFolder = "", string? q = "")
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

            return RedirectToAction(nameof(Index), new { folder = returnFolder, q });
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
