using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        public async Task<IActionResult> Index(
            string type = "BANDING",
            int pageNumber = 1,
            int pageSize = DefaultPageSize,
            string search = "")
        {
            var baseQuery =
                from fb in _context.TrxFeedbacks
                join ta in _context.trx_audits on fb.TrxAuditId equals ta.id
                join sp in _context.spbus on ta.spbu_id equals sp.id
                join au in _context.app_users on ta.app_user_id equals au.id
                where fb.FeedbackType == type
                select new
                {
                    Fb = fb,
                    Ta = ta,
                    Sp = sp,
                    Auditor = au.name
                };

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower().Trim();
                baseQuery = baseQuery.Where(x =>
                       x.Fb.TicketNo.ToLower().Contains(term)
                    || x.Sp.spbu_no.ToLower().Contains(term)
                    || (x.Sp.city_name != null && x.Sp.city_name.ToLower().Contains(term))
                    || (x.Auditor != null && x.Auditor.ToLower().Contains(term)));
            }

            var totalItems = await baseQuery.CountAsync();
            var pageItems = await baseQuery
                .OrderByDescending(x => x.Ta.updated_date)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var connectionString = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var result = new List<ComplainListItemViewModel>();
            foreach (var it in pageItems)
            {
                var sql = @"
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

                var rows = (await conn.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(sql, new { id = it.Ta.id })).ToList();

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

                result.Add(new ComplainListItemViewModel
                {
                    FeedbackId = it.Fb.Id,
                    TicketNo = it.Fb.TicketNo,
                    AuditId = it.Ta.id,
                    NoSpbu = it.Sp.spbu_no,
                    Region = it.Sp.region,
                    City = it.Sp.city_name,
                    Auditor = it.Auditor,
                    TanggalAudit = (it.Ta.audit_execution_time == null || it.Ta.audit_execution_time == DateTime.MinValue)
                        ? it.Ta.updated_date ?? DateTime.Now
                        : it.Ta.audit_execution_time.Value,
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

            ViewBag.Search = search;
            ViewBag.Type = type;

            return View(vm);
        }

        [HttpGet("Complain/Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

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

                // Tambahan utk galeri FINAL
                AuditId = header.Ta.id  // <-- penting: simpan id audit
            };

            var cs = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            // =========================
            // 1) MUAT MEDIA FINAL (BA)
            // =========================
            var finalRows = await conn.QueryAsync<MediaRow>(@"
        SELECT media_type, media_path
        FROM trx_audit_media
        WHERE trx_audit_id = @aid
          AND type = 'FINAL'
          AND (status IS NULL OR status <> 'DELETED')
        ORDER BY created_date ASC;", new { aid = vm.AuditId });

            vm.FinalDocuments = finalRows.Select(m => new MediaItem
            {
                MediaPath = ToPublicUrl(m.media_path),
                MediaType = NormalizeType(m.media_type),
                FileName = Path.GetFileName(m.media_path),
                SizeReadable = null
            }).ToList();

            // ============================================================
            // 2) Ambil poin + label + elemen yang dibanding (string_agg)
            //    PERBAIKI point_id -> gunakan p.id AS point_id
            // ============================================================
            var pointRows = await conn.QueryAsync<PointRow>(@"
        SELECT 
            p.id AS point_id,  -- <-- FIX di sini
            p.description AS description,
            COALESCE(e.title || '. ' || e.description, '-')  AS element_label,
            COALESCE(se.title || '. ' || se.description, '-') AS sub_element_label,
            COALESCE(de.number || '. ' || de.title, '-') AS detail_element_label,
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

                // ============================
                // 3) Media per poin (preview)
                // ============================
                var medias = await conn.QueryAsync<MediaRow>(@"
            SELECT id, media_type, media_path
            FROM trx_feedback_point_media
            WHERE trx_feedback_point_id = @pid
              AND (status IS NULL OR status <> 'DELETED')
            ORDER BY created_date ASC;", new { pid = pr.point_id });

                int fileIdx = 1;
                foreach (var m in medias)
                {
                    var extName = NormalizeType(m.media_type);
                    var fileName = Path.GetFileName(m.media_path);
                    if (string.IsNullOrWhiteSpace(fileName))
                        fileName = $"Dokumen Pendukung {fileIdx}.{(extName == "pdf" ? "pdf" : extName)}";

                    pointVm.Attachments.Add(new AttachmentItem
                    {
                        Id = m.id,
                        FileName = fileName,
                        SizeReadable = null,
                        // Tambahan agar bisa preview di cshtml:
                        Url = ToPublicUrl(m.media_path),
                        MediaType = extName
                    });
                    fileIdx++;
                }

                vm.Points.Add(pointVm);
                idx++;
            }

            return View(vm);
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
                "APPROVED" => "Disetujui",
                "REJECTED" => "Ditolak",
                "IN_PROGRESS_SUBMIT" => "Menunggu Persetujuan",
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
            if (entity == null) return NotFound();

            entity.Status = "APPROVED";
            entity.ApprovalBy = User?.Identity?.Name ?? "system";
            entity.ApprovalDate = DateTime.UtcNow;
            entity.UpdatedBy = entity.ApprovalBy;
            entity.UpdatedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            TempData["AlertSuccess"] = "Pengajuan telah disetujui.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        // GET: /Complain/Reject?id=...&reason=...
        [HttpGet]
        public async Task<IActionResult> Reject(string id, string reason = "")
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var entity = await _context.TrxFeedbacks.FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null) return NotFound();

            entity.Status = "REJECTED";
            entity.ApprovalBy = User?.Identity?.Name ?? "system";
            entity.ApprovalDate = DateTime.UtcNow;
            entity.UpdatedBy = entity.ApprovalBy;
            entity.UpdatedDate = DateTime.UtcNow;

            // Jika ada kolom alasan penolakan:
            // entity.reject_reason = reason;

            await _context.SaveChangesAsync();
            TempData["AlertSuccess"] = "Pengajuan telah ditolak.";
            return RedirectToAction(nameof(Detail), new { id });
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

            var baseUploadRoot = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", "feedback" , media.trx_feedback_point_id);
            var fullPath = media.media_path;
            if (!Path.IsPathRooted(fullPath))
                fullPath = Path.Combine(baseUploadRoot, media.media_path.TrimStart('/', '\\'));

            if (!System.IO.File.Exists(fullPath))
                return NotFound();

            var fileName = Path.GetFileName(fullPath);
            var contentType = string.IsNullOrWhiteSpace(media.media_type) ? "application/octet-stream" : media.media_type;
            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return File(bytes, contentType, fileName);
        }
    }
}
