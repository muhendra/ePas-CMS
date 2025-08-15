using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace e_Pas_CMS.Controllers
{
    [Authorize]
    public class ComplainController : Controller
    {
        private readonly EpasDbContext _context;
        private const int DefaultPageSize = 10;

        public ComplainController(EpasDbContext context)
        {
            _context = context;
        }

        // GET: /Complain?type=KOMPLAIN|BANDING
        [HttpGet]
        public async Task<IActionResult> Index(
    string type = "BANDING",
    string searchTerm = "",
    int pageNumber = 1,
    int pageSize = DefaultPageSize,
    string sortColumn = "TanggalAudit",
    string sortDirection = "desc")
        {
            ViewBag.Type = type;
            ViewBag.SearchTerm = searchTerm ?? "";
            ViewBag.SortColumn = sortColumn;
            ViewBag.SortDirection = sortDirection;

            var baseQuery =
                from fb in _context.TrxFeedbacks
                join ta in _context.trx_audits on fb.TrxAuditId equals ta.id
                join sp in _context.spbus on ta.spbu_id equals sp.id
                join au0 in _context.app_users on ta.app_user_id equals au0.id into aud1
                from au in aud1.DefaultIfEmpty()
                where fb.FeedbackType == type
                select new
                {
                    Fb = fb,
                    Ta = ta,
                    Sp = sp,
                    Auditor = au != null ? au.name : null
                };

            // SEARCH
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.ToLower().Trim();
                baseQuery = baseQuery.Where(x =>
                       (x.Fb.TicketNo ?? "").ToLower().Contains(term)
                    || (x.Sp.spbu_no ?? "").ToLower().Contains(term)
                    || (x.Sp.city_name ?? "").ToLower().Contains(term)
                    || (x.Sp.region ?? "").ToLower().Contains(term)
                    || ((x.Auditor ?? "").ToLower().Contains(term)));
            }

            var totalItems = await baseQuery.CountAsync();

            bool asc = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(sortColumn, "Score", StringComparison.OrdinalIgnoreCase))
            {
                // Ambil semua dulu agar bisa sort by skor
                var allItems = await baseQuery
                    .OrderByDescending(x => x.Ta.updated_date) // fallback
                    .ToListAsync();

                var connectionString = _context.Database.GetConnectionString();
                await using var connAll = new Npgsql.NpgsqlConnection(connectionString);
                await connAll.OpenAsync();

                const string sqlScore = @"
            SELECT 
                mqd.weight, 
                tac.score_input, 
                tac.score_x,
                mqd.is_relaksasi
            FROM master_questioner_detail mqd
            LEFT JOIN trx_audit_checklist tac 
              ON tac.master_questioner_detail_id = mqd.id
             AND tac.trx_audit_id = @id
            WHERE mqd.master_questioner_id = (
                SELECT master_questioner_checklist_id 
                FROM trx_audit 
                WHERE id = @id
            )
            AND mqd.type = 'QUESTION';";

                var scored = new List<(dynamic Row, decimal Score)>(allItems.Count);

                foreach (var it in allItems)
                {
                    var rows = (await connAll.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(
                        sqlScore, new { id = it.Ta.id })).ToList();

                    decimal sumAF = 0, sumW = 0, sumX = 0;
                    foreach (var r in rows)
                    {
                        var w = r.weight ?? 0;
                        var input = (r.score_input ?? "").Trim().ToUpperInvariant();

                        if (input == "X")
                        {
                            sumX += w;
                            sumAF += r.score_x ?? 0;
                        }
                        else if (input == "F" && r.is_relaksasi == true)
                        {
                            sumAF += 1.00m * w;
                        }
                        else
                        {
                            decimal af = input switch
                            {
                                "A" => 1.00m,
                                "B" => 0.80m,
                                "C" => 0.60m,
                                "D" => 0.40m,
                                "E" => 0.20m,
                                "F" => 0.00m,
                                _ => 0.00m
                            };
                            sumAF += af * w;
                        }
                        sumW += w;
                    }

                    var finalScore = (sumW - sumX) > 0 ? (sumAF / (sumW - sumX)) * 100m : 0m;
                    scored.Add((it, Math.Round(finalScore, 2)));
                }

                var ordered = asc
                    ? scored.OrderBy(x => x.Score).ThenByDescending(x => x.Row.Ta.updated_date)
                    : scored.OrderByDescending(x => x.Score).ThenByDescending(x => x.Row.Ta.updated_date);

                var paged = ordered
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var resultByScore = paged.Select(x =>
                {
                    // SAFE audit date (audit_execution_time: DateTime non-nullable)
                    DateTime auditDate = x.Row.Ta.audit_execution_time;
                    if (auditDate == default || auditDate == DateTime.MinValue)
                        auditDate = x.Row.Ta.updated_date ?? DateTime.Now;

                    return new ComplainListItemViewModel
                    {
                        FeedbackId = x.Row.Fb.Id,
                        TicketNo = x.Row.Fb.TicketNo,
                        AuditId = x.Row.Ta.id,
                        NoSpbu = x.Row.Sp.spbu_no,
                        Region = x.Row.Sp.region,
                        City = x.Row.Sp.city_name,
                        Auditor = x.Row.Auditor,
                        TanggalAudit = auditDate,
                        TipeAudit = x.Row.Ta.audit_type,
                        AuditLevel = x.Row.Ta.audit_level,
                        Score = x.Score
                    };
                }).ToList();

                var vmScore = new PaginationModel<ComplainListItemViewModel>
                {
                    Items = resultByScore,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalItems = totalItems
                };

                return View(vmScore);
            }
            else
            {
                // Sorting kolom non-Score di DB
                IOrderedQueryable<dynamic> ordered = sortColumn switch
                {
                    "TicketNo" => asc ? baseQuery.OrderBy(x => x.Fb.TicketNo) : baseQuery.OrderByDescending(x => x.Fb.TicketNo),
                    "NoSpbu" => asc ? baseQuery.OrderBy(x => x.Sp.spbu_no) : baseQuery.OrderByDescending(x => x.Sp.spbu_no),
                    "City" => asc ? baseQuery.OrderBy(x => x.Sp.city_name) : baseQuery.OrderByDescending(x => x.Sp.city_name),
                    "Rayon" => asc ? baseQuery.OrderBy(x => x.Sp.region) : baseQuery.OrderByDescending(x => x.Sp.region),
                    "Auditor" => asc ? baseQuery.OrderBy(x => x.Auditor) : baseQuery.OrderByDescending(x => x.Auditor),
                    "TanggalAudit" => asc ? baseQuery.OrderBy(x => x.Ta.audit_execution_time)
                                           : baseQuery.OrderByDescending(x => x.Ta.audit_execution_time),
                    "TipeAudit" => asc ? baseQuery.OrderBy(x => x.Ta.audit_type) : baseQuery.OrderByDescending(x => x.Ta.audit_type),
                    "AuditLevel" => asc ? baseQuery.OrderBy(x => x.Ta.audit_level) : baseQuery.OrderByDescending(x => x.Ta.audit_level),
                    _ => asc ? baseQuery.OrderBy(x => x.Ta.audit_execution_time)
                                           : baseQuery.OrderByDescending(x => x.Ta.audit_execution_time),
                };

                var pageItems = await ordered
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var connectionString = _context.Database.GetConnectionString();
                await using var conn = new Npgsql.NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                const string sql = @"
            SELECT 
                mqd.weight, 
                tac.score_input, 
                tac.score_x,
                mqd.is_relaksasi
            FROM master_questioner_detail mqd
            LEFT JOIN trx_audit_checklist tac 
              ON tac.master_questioner_detail_id = mqd.id
             AND tac.trx_audit_id = @id
            WHERE mqd.master_questioner_id = (
                SELECT master_questioner_checklist_id 
                FROM trx_audit 
                WHERE id = @id
            )
            AND mqd.type = 'QUESTION';";

                var result = new List<ComplainListItemViewModel>(pageItems.Count);
                foreach (var it in pageItems)
                {
                    var rows = (await conn.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(
                        sql, new { id = it.Ta.id })).ToList();

                    decimal sumAF = 0, sumW = 0, sumX = 0;
                    foreach (var r in rows)
                    {
                        var w = r.weight ?? 0;
                        var input = (r.score_input ?? "").Trim().ToUpperInvariant();

                        if (input == "X")
                        {
                            sumX += w;
                            sumAF += r.score_x ?? 0;
                        }
                        else if (input == "F" && r.is_relaksasi == true)
                        {
                            sumAF += 1.00m * w;
                        }
                        else
                        {
                            decimal af = input switch
                            {
                                "A" => 1.00m,
                                "B" => 0.80m,
                                "C" => 0.60m,
                                "D" => 0.40m,
                                "E" => 0.20m,
                                "F" => 0.00m,
                                _ => 0.00m
                            };
                            sumAF += af * w;
                        }
                        sumW += w;
                    }

                    var finalScore = (sumW - sumX) > 0 ? (sumAF / (sumW - sumX)) * 100m : 0m;

                    // SAFE audit date (audit_execution_time: DateTime non-nullable)
                    DateTime auditDate = it.Ta.audit_execution_time;
                    if (auditDate == default || auditDate == DateTime.MinValue)
                        auditDate = it.Ta.updated_date ?? DateTime.Now;

                    result.Add(new ComplainListItemViewModel
                    {
                        FeedbackId = it.Fb.Id,
                        TicketNo = it.Fb.TicketNo,
                        AuditId = it.Ta.id,
                        NoSpbu = it.Sp.spbu_no,
                        Region = it.Sp.region,
                        City = it.Sp.city_name,
                        Auditor = it.Auditor,
                        TanggalAudit = auditDate,
                        TipeAudit = it.Ta.audit_type,
                        AuditLevel = it.Ta.audit_level,
                        Score = Math.Round(finalScore, 2)
                    });
                }

                var vm = new PaginationModel<ComplainListItemViewModel>
                {
                    Items = result,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalItems = totalItems
                };

                return View(vm);
            }
        }

        [HttpGet("Complain/Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            string pid = "";

            var header =
                await (from tf in _context.TrxFeedbacks
                       join ta in _context.trx_audits on tf.TrxAuditId equals ta.id
                       join s in _context.spbus on ta.spbu_id equals s.id
                       join au in _context.app_users on ta.app_user_id equals au.id into aud1
                       from au in aud1.DefaultIfEmpty()
                       where tf.Id == id
                       select new
                       {
                           Tf = tf,
                           Ta = ta,
                           S = s,
                           Auditor1 = au != null ? au.name : "-"
                       }).FirstOrDefaultAsync();

            if (header == null) return NotFound();

            var vm = new ComplainDetailViewModel
            {
                // Status & jenis
                IsBanding = string.Equals(header.Tf.FeedbackType, "BANDING", StringComparison.OrdinalIgnoreCase),
                StatusCode = header.Tf.Status,
                StatusText = MapStatusText(header.Tf.Status),

                // SPBU
                NoSpbu = header.S.spbu_no,
                Region = header.S.region,
                City = header.S.city_name,
                Address = header.S.address,

                // Audit
                ReportNo = header.Ta.report_no,
                TanggalAudit = (header.Ta.audit_execution_time == null || header.Ta.audit_execution_time == DateTime.MinValue)
                                ? header.Ta.updated_date
                                : header.Ta.audit_execution_time,
                SentDate = header.Ta.updated_date,
                Verifikator = header.Ta.approval_by,
                Auditor1 = header.Auditor1,
                Auditor2 = "",
                TipeAudit = header.Ta.audit_type,
                NextAudit = header.Ta.audit_level,
                Koordinator = "Sabar Kembaren",

                // Tiket
                Id = header.Tf.Id,
                TicketNo = header.Tf.TicketNo,
                NomorBanding = header.Tf.TicketNo,
                CreatedDate = header.Tf.CreatedDate,

                // Body global (jika kosong akan diisi dari poin pertama)
                BodyText = null,

                Points = new List<PointItem>(),
                Attachments = new List<AttachmentItem>(),

                // Aksi
                CanApprove = string.Equals(header.Tf.Status, "IN_PROGRESS_SUBMIT", StringComparison.OrdinalIgnoreCase),
                CanReject = string.Equals(header.Tf.Status, "IN_PROGRESS_SUBMIT", StringComparison.OrdinalIgnoreCase),

                feedback_type = header.Tf.FeedbackType,

                // Tambahan utk galeri FINAL
                AuditId = header.Ta.id
            };

            var cs = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            // 1) Ambil poin + label + elemen yang dibanding (string_agg)
            var pointRows = await conn.QueryAsync<PointRow>(@"
            SELECT 
                p.id AS point_id,
                p.description AS description,
                COALESCE(e.number || '. ' || e.title, COALESCE(e.title, COALESCE(e.description, '-'))) AS element_label,
                COALESCE(se.number || '. ' || se.title, COALESCE(se.title, COALESCE(se.description, '-'))) AS sub_element_label,
                COALESCE(de.number || '. ' || de.title, COALESCE(de.title, '-')) AS detail_element_label,
                COALESCE((
                    SELECT string_agg(me.number, ', ' ORDER BY me.number)
                    FROM trx_feedback_point_element pe
                    JOIN master_questioner_detail me ON me.id = pe.master_questioner_detail_id
                    WHERE pe.trx_feedback_point_id = p.id
                ), '') AS compared_elements
            FROM trx_feedback_point p
            LEFT JOIN master_questioner_detail e  ON e.id  = p.element_master_questioner_detail_id
            LEFT JOIN master_questioner_detail se ON se.id = p.sub_element_master_questioner_detail_id
            LEFT JOIN master_questioner_detail de ON de.id = p.detail_element_master_questioner_detail_id
            WHERE p.trx_feedback_id = @fid
            ORDER BY p.created_date ASC;", new { fid = id });

            var pointList = pointRows.ToList();
            int idx = 1;

            foreach (var pr in pointList)
            {
                var pointVm = new PointItem
                {
                    Element = pr.element_label,
                    SubElement = pr.sub_element_label,
                    DetailElement = pr.detail_element_label,
                    DetailDibantah = string.IsNullOrWhiteSpace(pr.compared_elements) ? "-" : pr.compared_elements,
                    Description = pr.description,
                    Attachments = new List<AttachmentItem>()
                };

                if (idx == 1 && string.IsNullOrWhiteSpace(vm.BodyText))
                    vm.BodyText = pr.description;

                // 2) Media per poin (untuk preview & unduh)
                var medias = await conn.QueryAsync<MediaRow>(@"
            SELECT id, trx_feedback_point_id, media_type, media_path
            FROM trx_feedback_point_media
            WHERE trx_feedback_point_id = @pid
              AND media_path IS NOT NULL
              AND btrim(media_path) <> ''
            ORDER BY created_date ASC;",
                    new { pid = pr.point_id });

                pid = pr.point_id;

                int fileIdx = 1;
                foreach (var m in medias)
                {
                    // Tentukan ekstensi/type
                    var extName = NormalizeType(m.media_type);
                    if (string.IsNullOrWhiteSpace(extName))
                    {
                        var ext = Path.GetExtension(m.media_path);
                        if (!string.IsNullOrWhiteSpace(ext))
                            extName = ext.Trim('.').ToLowerInvariant();
                    }
                    if (string.IsNullOrWhiteSpace(extName))
                        continue;

                    // Nama file
                    var fileName = Path.GetFileName(m.media_path);
                    if (string.IsNullOrWhiteSpace(fileName))
                        fileName = $"Dokumen Pendukung {fileIdx}.{extName}";

                    // Skema baru: uploads/feedback/{trx_feedback_point_id},{media_id}/{filename}
                    var relativeNew = $"uploads/feedback/{m.trx_feedback_point_id},{m.id}/{fileName}";

                    // Fallback: pakai path lama dari media_path jika ada
                    var relativeOld = (m.media_path ?? string.Empty)
                                        .TrimStart('/', '\\')
                                        .Replace("\\", "/");

                    var physicalRoot = Path.Combine("/var/www/epas-asset", "wwwroot");
                    string? chosenRelative = null;

                    if (FileExists(physicalRoot, relativeNew))
                        chosenRelative = relativeNew;
                    else if (FileExists(physicalRoot, relativeOld))
                        chosenRelative = relativeOld;

                    // Jika dua2nya tidak ada, skip entry agar tidak menampilkan attachment “kosong”
                    if (string.IsNullOrWhiteSpace(chosenRelative))
                        continue;

                    pointVm.Attachments.Add(new AttachmentItem
                    {
                        Id = m.id,
                        FileName = fileName,
                        Url = "/" + chosenRelative,     // dipakai untuk preview (img/video/pdf) & "Buka di Tab Baru"
                        MediaType = extName,
                        SizeReadable = null
                    });

                    
                    fileIdx++;
                }

                vm.Points.Add(pointVm);
                idx++;
            }

            // 3) Isi vm.Attachments (GLOBAL) mengikuti pola GetMediaNotesAsync (tabel trx_audit_media)
            var notes = await GetMediaNotesAsync(conn, pid);
            vm.Attachments = notes
                .Where(n => !string.IsNullOrWhiteSpace(n.MediaPath))
                .Select(n => new AttachmentItem
                {
                    Id = null, // tidak ada id media per baris di trx_audit_media untuk keperluan ini
                    FileName = SafeGetFileName(n.MediaPath),
                    Url = n.MediaPath, // sudah full URL dari GetMediaNotesAsync
                    MediaType = NormalizeType(n.MediaType) ?? GetExtFromUrl(n.MediaPath),
                    SizeReadable = null
                })
                .ToList();

            return View(vm);
        }

        private async Task<List<MediaItem>> GetMediaNotesAsync(IDbConnection conn, string id)
        {
            const string sql = @"
            SELECT id, media_type, media_path
            FROM trx_feedback_point_media
            WHERE trx_feedback_point_id = @id
            ORDER BY created_date ASC;";

            var rows = await conn.QueryAsync<MediaRow>(sql, new { id });

            return rows.Select(x => new MediaItem
            {
                MediaType = x.media_type,
                MediaPath = "https://epas-assets.zarata.co.id" + x.media_path
            }).ToList();
        }

        // ===== Helper lokal yang dipakai di atas =====

        private static bool FileExists(string physicalRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return false;
            var fullPath = Path.Combine(physicalRoot, relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace("\\", Path.DirectorySeparatorChar.ToString()));
            return System.IO.File.Exists(fullPath);
        }

        private static string SafeGetFileName(string urlOrPath)
        {
            try
            {
                // Coba parse URL
                if (Uri.TryCreate(urlOrPath, UriKind.Absolute, out var u))
                    return Path.GetFileName(u.LocalPath);

                // Fallback path biasa
                return Path.GetFileName(urlOrPath);
            }
            catch
            {
                return "attachment";
            }
        }

        private static string? GetExtFromUrl(string url)
        {
            try
            {
                var name = SafeGetFileName(url);
                var ext = Path.GetExtension(name);
                return string.IsNullOrWhiteSpace(ext) ? null : ext.Trim('.').ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeType(string type)
        {
            var t = (type ?? "").Trim().ToLowerInvariant();
            // beberapa backend menyimpan "image/jpeg", "video/mp4", dsb
            if (t.Contains("/"))
            {
                // ambil sub-type
                t = t.Split('/').LastOrDefault() ?? t;
            }
            // samakan alias umum
            return t switch
            {
                "jpg" or "jpeg" => "jpg",
                "png" => "png",
                "gif" => "gif",
                "mp4" => "mp4",
                "webm" => "webm",
                "pdf" => "pdf",
                _ => t // biarkan apa adanya jika tipe lain
            };
        }

        private sealed class PointRow
        {
            public string point_id { get; set; } = default!;
            public string description { get; set; } = default!;
            public string element_label { get; set; } = default!;
            public string sub_element_label { get; set; } = default!;
            public string detail_element_label { get; set; } = default!;
            public string compared_elements { get; set; } = default!;
        }

        private sealed class MediaRow
        {
            public string id { get; set; } = default!;
            public string media_type { get; set; } = default!;
            public string media_path { get; set; } = default!;

            public string trx_feedback_point_id { get; set; }
        }

        private static string MapStatusText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Menunggu Persetujuan";
            var s = raw.Trim().ToUpperInvariant();
            return s switch
            {
                "REJECTED" => "Ditolak",
                "IN_PROGRESS_SUBMIT" => "Menunggu Persetujuan SBM",
                "APPROVE_SBM" => "Menunggu Persetujuan PPN",
                "APPROVE_PPN" => "Banding Disetujui",
                "APPROVE" => "Banding Disetujui",
                _ => raw
            };
        }

        // POST: /Complain/Approve
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var entity = await _context.TrxFeedbacks.FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null)
                return NotFound();

            // Tentukan status baru berdasarkan status saat ini
            if (entity.Status == "IN_PROGRESS_SUBMIT")
            {
                entity.Status = "APPROVE_SBM";
            }
            else if (entity.Status == "APPROVE_SBM")
            {
                entity.Status = "APPROVE_PPN";
            }
            else
            {
                // Default jika bukan kedua status di atas
                entity.Status = "APPROVED";
            }

            entity.ApprovalBy = User?.Identity?.Name ?? "system";
            entity.ApprovalDate = DateTime.Now;
            entity.UpdatedBy = entity.ApprovalBy;
            entity.UpdatedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["AlertSuccess"] = "Pengajuan telah disetujui.";
            return RedirectToAction(nameof(Detail), new { id });
        }


        [HttpGet]
        public async Task<IActionResult> Reject(string id, string reason = "")
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var entity = await _context.TrxFeedbacks.FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null) return NotFound();

            entity.Status = "REJECTED";
            entity.ApprovalBy = User?.Identity?.Name ?? "system";
            entity.ApprovalDate = DateTime.Now;
            entity.UpdatedBy = entity.ApprovalBy;
            entity.UpdatedDate = DateTime.Now;

            await _context.SaveChangesAsync();
            TempData["AlertSuccess"] = "Pengajuan telah ditolak.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        [HttpPost("Complain/Reassign/{id}")]
        public async Task<IActionResult> Reassign(string id)
        {
            var currentUser = User.Identity?.Name;

            // 1. Ambil trx_auditid dari trx_feedback
            var trxAuditId = await _context.TrxFeedbacks
                .Where(tf => tf.Id == id)
                .Select(tf => tf.TrxAuditId)
                .FirstOrDefaultAsync();

            if (trxAuditId == null)
                return NotFound();

            // 2. Update trx_audit
            string sqlAudit = @"
        UPDATE trx_audit
        SET approval_date = now(),
            approval_by = @p0,
            updated_date = now(),
            updated_by = @p0,
            status = 'UNDER_REVIEW'
        WHERE id = @p1";

            int affectedAudit = await _context.Database.ExecuteSqlRawAsync(sqlAudit, currentUser, trxAuditId);
            if (affectedAudit == 0)
                return NotFound();

            // 3. Update trx_feedback
            string sqlFeedback = @"
        UPDATE trx_feedback
        SET status = 'APPROVE',
            updated_date = now(),
            updated_by = @p0
        WHERE id = @p1";

            await _context.Database.ExecuteSqlRawAsync(sqlFeedback, currentUser, id);

            TempData["Success"] = "Laporan audit telah Reassign ke Review.";
            return RedirectToAction("Index");
        }


        private static string ToPublicUrl(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            // Jika path sudah absolute (http/https), langsung kembalikan
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return path;

            // Jika relatif, gabungkan dengan base asset host Anda
            const string AssetsBase = "https://epas-assets.zarata.co.id"; // sesuaikan bila perlu
            if (path.StartsWith("/"))
                return AssetsBase + path;

            return $"{AssetsBase}/{path}";
        }

        // GET: /Complain/Download/{idMedia}
        public async Task<IActionResult> Download(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var cs = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            var media = await conn.QueryFirstOrDefaultAsync<MediaRow>(@"
                SELECT id, media_type, media_path, trx_feedback_point_id
                FROM trx_feedback_point_media
                WHERE id = @id;", new { id });

            if (media == null) return NotFound();

            var fullPath = Path.Combine("/var/www/epas-asset", "wwwroot", media.media_path);

            if (!System.IO.File.Exists(fullPath))
                return NotFound();

            var fileName = Path.GetFileName(fullPath);
            var contentType = string.IsNullOrWhiteSpace(media.media_type) ? "application/octet-stream" : media.media_type;
            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return File(bytes, contentType, fileName);
        }
    }
}
