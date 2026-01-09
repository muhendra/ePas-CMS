using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text;

namespace YourApp.Controllers
{
    [Route("UploadLibrary")]
    [Authorize] // tambah policy/role kalau perlu
    [Route("[controller]/[action]")]
    public class UploadLibraryController : Controller
    {
        // base path FIX sesuai server kamu
        private const string BasePath = "/var/www/epas-asset/wwwroot/uploads";

        private static string NormalizeAndValidateRelative(string? rel)
        {
            rel ??= "";
            rel = rel.Replace('\\', '/').Trim();

            // biar konsisten: tidak boleh absolute
            if (rel.StartsWith("/")) rel = rel.TrimStart('/');

            // gabung + normalize
            var combined = Path.GetFullPath(Path.Combine(BasePath, rel));

            // pastikan masih di bawah BasePath
            var baseFull = Path.GetFullPath(BasePath);
            if (!combined.StartsWith(baseFull + Path.DirectorySeparatorChar) && combined != baseFull)
                throw new InvalidOperationException("Invalid path.");

            // kembalikan RELATIVE yang bersih (pakai separator '/')
            var cleanedRel = Path.GetRelativePath(baseFull, combined).Replace('\\', '/');
            return cleanedRel == "." ? "" : cleanedRel;
        }

        private static string ToPhysicalPathFromRel(string rel)
        {
            rel ??= "";
            var baseFull = Path.GetFullPath(BasePath);
            var full = Path.GetFullPath(Path.Combine(baseFull, rel));
            return full;
        }

        public class LibraryItemVm
        {
            public string Name { get; set; } = "";
            public string RelPath { get; set; } = "";     // path relatif dari uploads
            public bool IsDir { get; set; }
            public long? SizeBytes { get; set; }          // null untuk folder
            public DateTime LastWriteTime { get; set; }
        }

        public class LibraryVm
        {
            public string CurrentRel { get; set; } = "";
            public string? Query { get; set; }
            public List<(string name, string rel)> Breadcrumbs { get; set; } = new();
            public List<LibraryItemVm> Folders { get; set; } = new();
            public List<LibraryItemVm> Files { get; set; } = new();
        }

        [HttpGet("")]
        [HttpGet]
        public IActionResult Index(string? folder = "", string? q = "")
        {
            var rel = NormalizeAndValidateRelative(folder);
            var physical = ToPhysicalPathFromRel(rel);

            if (!Directory.Exists(physical))
            {
                // kalau folder tidak ada, fallback ke root uploads
                rel = "";
                physical = ToPhysicalPathFromRel(rel);
            }

            q = (q ?? "").Trim();

            var vm = new LibraryVm
            {
                CurrentRel = rel,
                Query = q
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

            // list folders + files
            var dirInfo = new DirectoryInfo(physical);

            IEnumerable<DirectoryInfo> dirs = dirInfo.EnumerateDirectories();
            IEnumerable<FileInfo> files = dirInfo.EnumerateFiles();

            if (!string.IsNullOrEmpty(q))
            {
                dirs = dirs.Where(d => d.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
                files = files.Where(f => f.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            vm.Folders = dirs
                .OrderBy(d => d.Name)
                .Select(d => new LibraryItemVm
                {
                    Name = d.Name,
                    IsDir = true,
                    RelPath = (string.IsNullOrEmpty(rel) ? d.Name : $"{rel}/{d.Name}"),
                    LastWriteTime = d.LastWriteTime
                })
                .ToList();

            vm.Files = files
                .OrderBy(f => f.Name)
                .Select(f => new LibraryItemVm
                {
                    Name = f.Name,
                    IsDir = false,
                    RelPath = (string.IsNullOrEmpty(rel) ? f.Name : $"{rel}/{f.Name}"),
                    SizeBytes = f.Length,
                    LastWriteTime = f.LastWriteTime
                })
                .ToList();

            return View(vm);
        }

        [HttpGet]
        public IActionResult Download(string path)
        {
            var rel = NormalizeAndValidateRelative(path);
            var physical = ToPhysicalPathFromRel(rel);

            if (!System.IO.File.Exists(physical))
                return NotFound();

            var fileName = Path.GetFileName(physical);
            var stream = new FileStream(physical, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Force download
            return File(stream, "application/octet-stream", fileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteFile(string path, string? returnFolder = "", string? q = "")
        {
            var rel = NormalizeAndValidateRelative(path);
            var physical = ToPhysicalPathFromRel(rel);

            if (System.IO.File.Exists(physical))
                System.IO.File.Delete(physical);

            return RedirectToAction(nameof(Index), new { folder = returnFolder, q });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteFolder(string path, string? returnFolder = "", string? q = "")
        {
            var rel = NormalizeAndValidateRelative(path);
            var physical = ToPhysicalPathFromRel(rel);

            if (Directory.Exists(physical))
            {
                // default: hanya hapus folder kosong (aman)
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

        // helper format size (opsional)
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
