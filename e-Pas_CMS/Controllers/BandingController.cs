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

        //Complain? type = KOMPLAIN   atau   /Complain? type = BANDING
        public async Task<IActionResult> Index(
            string type = "BANDING",
            int pageNumber = 1,
            int pageSize = DefaultPageSize,
            string search = "")
        {
            // Filter akses region/sbm seperti di AuditController (opsional, aktifkan kalau perlu)
            var currentUser = User.Identity?.Name;

            var baseQuery =
                from fb in _context.TrxFeedbacks
                join ta in _context.trx_audits on fb.TrxAuditId equals ta.id
                join sp in _context.spbus on ta.spbu_id equals sp.id
                join au in _context.app_users on ta.app_user_id equals au.id
                where fb.FeedbackType == type // "KOMPLAIN" / "BANDING"
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

            // buka koneksi Dapper untuk hitung skor (mengikuti pola di AuditController)
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
            ViewBag.Type = type; // untuk tab aktif

            return View(vm);
        }

        [HttpGet("Complain/Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var q =
                from tf in _context.TrxFeedbacks
                join ta in _context.trx_audits on tf.TrxAuditId equals ta.id
                join s in _context.spbus on ta.spbu_id equals s.id
                join au in _context.app_users on ta.app_user_id equals au.id into aud1
                join tfp in _context.TrxFeedbackPoints on tf.Id equals tfp.TrxFeedbackId
                from au in aud1.DefaultIfEmpty()
                where tf.Id == id
                select new
                {
                    Tf = tf,
                    Ta = ta,
                    S = s,
                    tfp = tfp,
                    Auditor1 = au != null ? au.name : "-"
                    // kalau ada kolom auditor2/koordinator/verifikator lain, bisa di-join tambahan di sini
                };

            var data = await q.FirstOrDefaultAsync();
            if (data == null) return NotFound();

            var vm = new ComplainDetailViewModel
            {
                // Flag halaman
                IsBanding = string.Equals(data.Tf.FeedbackType, "BANDING", StringComparison.OrdinalIgnoreCase),
                StatusCode = data.Tf.Status,
                StatusText = MapStatusText(data.Tf.Status),

                // SPBU
                NoSpbu = data.S.spbu_no,
                Region = data.S.region,
                City = data.S.city_name,
                Address = data.S.address,

                // AUDIT – ngikut AuditController.Detail
                ReportNo = data.Ta.report_no,                                       // No. Report
                TanggalAudit = (data.Ta.audit_execution_time == null ||
                                data.Ta.audit_execution_time == DateTime.MinValue)
                                ? data.Ta.updated_date
                                : data.Ta.audit_execution_time,                           // Tanggal Audit
                SentDate = data.Ta.updated_date,                                    // “Sent Date” (pakai updated_date seperti di Audit)
                Verifikator = data.Ta.approval_by,                                     // kalau kolom ada
                Auditor1 = data.Auditor1,
                Auditor2 = "",                                   // kalau kolom ada
                TipeAudit = data.Ta.audit_type,
                NextAudit = data.Ta.audit_level,                                     // label next/level yang ditampilkan di screenshot
                Koordinator = "Sabar Kembaren",                                     // kalau kolom ada

                // Info Komplain/Banding
                Id = data.Tf.Id,
                TicketNo = data.Tf.TicketNo,
                NomorBanding = data.Tf.TicketNo,                                   // kalau kolom ada
                CreatedDate = data.Tf.CreatedDate,

                // Opsional – kalau isi komplain disimpan di trx_feedback
                BodyText = data.tfp.Description,                                     // ganti ke kolom sebenarnya kalau beda

                // Dokumen & poin – isi dari tabel terpisah kalau ada
                Points = new List<PointItem>(),
                Attachments = new List<AttachmentItem>(),

                // Aksi
                CanApprove = string.Equals(data.Tf.Status, "MENUNGGU", StringComparison.OrdinalIgnoreCase),
                CanReject = string.Equals(data.Tf.Status, "MENUNGGU", StringComparison.OrdinalIgnoreCase),
            };

            // ==== contoh ambil lampiran & poin kalau tabelnya ada ====
            // vm.Attachments = await _context.trx_feedback_files
            //     .Where(f => f.trx_feedback_id == id)
            //     .Select(f => new AttachmentItem { Id = f.id, FileName = f.file_name, SizeReadable = f.size_readable })
            //     .ToListAsync();
            //
            // vm.Points = await _context.trx_feedback_points
            //     .Where(p => p.trx_feedback_id == id)
            //     .Select(p => new PointItem { Element = p.element, SubElement = p.sub_element, DetailElement = p.detail_element, DetailDibantah = p.detail_dibantah })
            //     .ToListAsync();

            return View(vm); // Views/Complain/Detail.cshtml
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

        // GET (atau POST) /Complain/Reject?id=...&reason=...
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

            // simpan reason bila kamu punya kolom reason di trx_feedback (mis: reject_reason)
            // entity.reject_reason = reason;

            await _context.SaveChangesAsync();
            TempData["AlertSuccess"] = "Pengajuan telah ditolak.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        // GET: /Complain/Download/{idFile}
        // Sesuaikan dengan storage/lampiran kamu.
        public async Task<IActionResult> Download(string id)
        {
            // Contoh implementasi jika punya tabel file:
            // var f = await _context.trx_feedback_files.FirstOrDefaultAsync(x => x.id == id);
            // if (f == null) return NotFound();
            // var bytes = System.IO.File.ReadAllBytes(Path.Combine(_env.WebRootPath, "uploads", f.storage_name));
            // return File(bytes, f.content_type ?? "application/octet-stream", f.file_name ?? "lampiran");

            return NotFound(); // hapus setelah kamu isi implementasi download
        }

    }
}