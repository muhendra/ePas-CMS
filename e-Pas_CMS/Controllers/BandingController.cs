using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.Models;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace e_Pas_CMS.Controllers
{
    [Authorize]
    public class BandingController : Controller
    {
        private readonly EpasDbContext _context;
        private const int DefaultPageSize = 10;

        public BandingController(EpasDbContext context)
        {
            _context = context;
        }

        // GET: /Complain?type=KOMPLAIN|BANDING
        [HttpGet]
        public async Task<IActionResult> Index(
        string type = "BANDING",
        int pageNumber = 1,
        int pageSize = 10,
        string searchTerm = "",
        string sortColumn = "",
        string sortDirection = "asc",
        string statusFilter = ""   // NEW
        )
        {
        // Opsi status (urut sesuai alur)
        var statusOptions = new List<(string Code, string Text)>
        {
            ("UNDER_REVIEW", "Menunggu Persetujuan SBM"),
            ("APPROVE_SBM",  "Menunggu Persetujuan PPN"),
            ("APPROVE_PPN",  "Menunggu Persetujuan CBI"),
            ("APPROVE_CBI",  "Menunggu Persetujuan Pertamina"),
            ("APPROVED",     "Banding Disetujui"),
            ("REJECTED",     "Ditolak"),
            ("REJECT_CBI",     "Ditolak"),
        };

            ViewBag.StatusOptions = statusOptions;
            ViewBag.StatusFilter = statusFilter ?? "";

            // baseQuery kamu yang sekarang
            var baseQuery =
                from fb in _context.TrxFeedbacks
                join ta in _context.trx_audits on fb.TrxAuditId equals ta.id
                join sp in _context.spbus on ta.spbu_id equals sp.id
                join au0 in _context.app_users on ta.app_user_id equals au0.id into aud1
                from au in aud1.DefaultIfEmpty()
                where fb.FeedbackType == type && fb.Status != "IN_PROGRESS_SUBMIT"
                select new { Fb = fb, Ta = ta, Sp = sp, Auditor = au != null ? au.name : null };

            // FILTER: status (berdasarkan KODE)
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                baseQuery = baseQuery.Where(x => x.Fb.Status == statusFilter);
            }

            // FILTER: search (contoh sederhana; sesuaikan punyamu)
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                baseQuery = baseQuery.Where(x =>
                    x.Fb.TicketNo.ToLower().Contains(term) ||
                    x.Sp.spbu_no.ToLower().Contains(term) ||
                    (x.Sp.city_name ?? "").ToLower().Contains(term) ||
                    (x.Sp.region ?? "").ToLower().Contains(term) ||
                    (x.Auditor ?? "").ToLower().Contains(term) ||
                    (x.Fb.Status ?? "").ToLower().Contains(term)
                );
            }

            var totalItems = await baseQuery.CountAsync();
            bool asc = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

            // ===== Sorting non-Score di DB
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

            // Hitung score per baris (tetap seperti logic kamu)
            var connectionString = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connectionString);
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

            var result = new List<BandingListItemViewModel>(pageItems.Count);
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

                DateTime auditDate = it.Ta.audit_execution_time;
                if (auditDate == default || auditDate == DateTime.MinValue)
                    auditDate = it.Ta.updated_date ?? DateTime.Now;

                result.Add(new BandingListItemViewModel
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
                    Score = it.Ta.score,
                    TanggalPengajuan = it.Fb.CreatedDate,
                    StatusCode = it.Fb.Status,
                    StatusText = (it.Fb.Status ?? "").ToUpperInvariant() switch
                    {
                        "REJECTED" => "Ditolak",
                        "REJECT_CBI" => "Ditolak",
                        "UNDER_REVIEW" => "Menunggu Persetujuan SBM",
                        "APPROVE_SBM" => "Menunggu Persetujuan PPN",
                        "APPROVE_PPN" => "Menunggu Persetujuan CBI",
                        "APPROVE_CBI" => "Menunggu Persetujuan Pertamina",
                        "APPROVED" => "Banding Disetujui",
                        _ => it.Fb.Status ?? "-"
                    }
                });
            }

            var vm = new PaginationModel<BandingListItemViewModel>
            {
                Items = result,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            return View(vm);
        }

        [HttpGet("Banding/Detail/{id}")]
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

                BodyText = null,
                Points = new List<PointItem>(),
                Attachments = new List<AttachmentItem>(),

                CanApprove = string.Equals(header.Tf.Status, "IN_PROGRESS_SUBMIT", StringComparison.OrdinalIgnoreCase),
                CanReject = string.Equals(header.Tf.Status, "IN_PROGRESS_SUBMIT", StringComparison.OrdinalIgnoreCase),
                feedback_type = header.Tf.FeedbackType,
                AuditId = header.Ta.id,
                Klarifikasi = header.Tf.Klarifikasi
            };

            var cs = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            var pointRows = await conn.QueryAsync<PointRow>(@"
            SELECT 
                p.id AS point_id,
                p.description AS description,
                COALESCE(e.number || '. ' || e.title, COALESCE(e.title, COALESCE(e.description, '-'))) AS element_label,
                COALESCE(se.number || '. ' || se.title, COALESCE(se.title, COALESCE(se.description, '-'))) AS sub_element_label,
                COALESCE(de.number || '. ' || de.title, COALESCE(de.title, '-')) AS detail_element_label,
                COALESCE((
                    SELECT string_agg(me.number || ' ' || me.title, E'\n' ORDER BY me.number)
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
            bool allApproved = true;
            int idx = 1;

            foreach (var pr in pointList)
            {
                var pointId = pr.point_id;

                pid = pr.point_id;

                var pointVm = new PointItem
                {
                    PointId = pointId,
                    Element = pr.element_label,
                    SubElement = pr.sub_element_label,
                    DetailElement = pr.detail_element_label,
                    DetailDibantah = string.IsNullOrWhiteSpace(pr.compared_elements) ? "-" : pr.compared_elements,
                    Description = pr.description,
                    Attachments = new List<AttachmentItem>(),
                    History = new List<PointApprovalHistory>()
                };

                if (idx == 1 && string.IsNullOrWhiteSpace(vm.BodyText))
                    vm.BodyText = pr.description;

                // 1. Get media
                var medias = await conn.QueryAsync<MediaRow>(@"
            SELECT id, trx_feedback_point_id, media_type, media_path
            FROM trx_feedback_point_media
            WHERE trx_feedback_point_id = @pid
              AND media_path IS NOT NULL
              AND btrim(media_path) <> ''
            ORDER BY created_date ASC;", new { pid = pointId });

                int fileIdx = 1;
                foreach (var m in medias)
                {
                    var extName = NormalizeType(m.media_type);
                    if (string.IsNullOrWhiteSpace(extName))
                    {
                        var ext = Path.GetExtension(m.media_path);
                        if (!string.IsNullOrWhiteSpace(ext))
                            extName = ext.Trim('.').ToLowerInvariant();
                    }

                    var fileName = Path.GetFileName(m.media_path) ?? $"Dokumen Pendukung {fileIdx}.{extName}";
                    var relativeNew = $"uploads/feedback/{m.trx_feedback_point_id},{m.id}/{fileName}";
                    var relativeOld = (m.media_path ?? string.Empty).TrimStart('/', '\\').Replace("\\", "/");

                    var physicalRoot = Path.Combine("/var/www/epas-asset", "wwwroot");
                    string? chosenRelative = FileExists(physicalRoot, relativeNew) ? relativeNew :
                                             FileExists(physicalRoot, relativeOld) ? relativeOld : null;

                    if (!string.IsNullOrWhiteSpace(chosenRelative))
                    {
                        pointVm.Attachments.Add(new AttachmentItem
                        {
                            Id = m.id,
                            FileName = fileName,
                            Url = "/" + chosenRelative,
                            MediaType = extName,
                            SizeReadable = null
                        });
                    }

                    fileIdx++;
                }

                // 2. Get approval history
                var approvals = await conn.QueryAsync<(string status, string approved_by, DateTime approved_date, string feedback_status)>(
                @"SELECT status, approved_by, approved_date, feedback_status 
                  FROM trx_feedback_point_approval 
                  WHERE trx_feedback_point_id = @pid 
                  ORDER BY approved_date ASC", new { pid = pointId });

                pointVm.History = approvals.Select(a => new PointApprovalHistory
                {
                    Status = a.status,
                    ApprovedBy = a.approved_by,
                    ApprovedDate = a.approved_date,
                    StatusCode = a.feedback_status
                }).ToList();

                // 3. Check if current point is approved
                if (!approvals.Any(h => h.status == "APPROVED"))
                    allApproved = false;

                vm.Points.Add(pointVm);
                idx++;
            }

            // Global media note (optional)
            var notes = await GetMediaNotesAsync(conn, pid);
            vm.Attachments = notes
                .Where(n => !string.IsNullOrWhiteSpace(n.MediaPath))
                .Select(n => new AttachmentItem
                {
                    Id = null,
                    FileName = SafeGetFileName(n.MediaPath),
                    Url = n.MediaPath,
                    MediaType = NormalizeType(n.MediaType) ?? GetExtFromUrl(n.MediaPath),
                    SizeReadable = null
                }).ToList();

            // Serialize for gallery
            ViewBag.AttachmentsJson = JsonConvert.SerializeObject(vm.Attachments);

            // Flag ke View
            ViewBag.AllPointsApproved = allApproved;

            var history = await _context.TrxFeedbackPointApprovals
            .FromSqlInterpolated($@"
                select tfpa.* from trx_feedback_point_approval tfpa 
                join trx_feedback_point tfp on tfp.id = tfpa.trx_feedback_point_id 
                join trx_feedback tf on tfp.trx_feedback_id = tf.id
                where tf.id = {id}
            ")
            .OrderByDescending(x => x.ApprovedDate)
            .ToListAsync();

            ViewBag.ApprovalHistory = history;

            return View(vm);
        }

        [HttpPost("Banding/ApprovePoint/{id}")]
        public async Task<IActionResult> ApprovePoint(string id)
        {
            var username = User.Identity?.Name ?? "SYSTEM";
            var cs = _context.Database.GetConnectionString();

            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            // Ambil status aktif dari trx_feedback
            var status = await conn.ExecuteScalarAsync<string>(@"
        SELECT tf.status 
        FROM trx_feedback_point p
        JOIN trx_feedback tf ON tf.id = p.trx_feedback_id
        WHERE p.id = @id", new { id });

            // Simpan approval
            await conn.ExecuteAsync(@"
        INSERT INTO trx_feedback_point_approval 
        (id, trx_feedback_point_id, status, approved_by, approved_date, feedback_status)
        VALUES (uuid_generate_v4(), @id, 'APPROVED', @user, NOW(), @feedback_status)",
                new { id, user = username, feedback_status = status });

            return RedirectToAction("Detail", new { id = await GetFeedbackIdByPointId(id) });
        }

        [HttpPost("Banding/RejectPoint/{id}")]
        public async Task<IActionResult> RejectPoint(string id)
        {
            var username = User.Identity?.Name ?? "SYSTEM";
            var cs = _context.Database.GetConnectionString();

            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            // Ambil id feedback + status aktif dari trx_feedback
            var feedback = await conn.QueryFirstOrDefaultAsync<(string FeedbackId, string Status)>(@"
            SELECT tf.id AS FeedbackId, tf.status 
            FROM trx_feedback_point p
            JOIN trx_feedback tf ON tf.id = p.trx_feedback_id
            WHERE p.id = @id", new { id });

            if (feedback.FeedbackId == null)
                return NotFound();

            var now = DateTime.Now;

            // Tentukan status reject header
            var rejectStatus = feedback.Status == "APPROVE_PPN" ? "REJECT_CBI" : "REJECTED";

            // Simpan rejection point
            await conn.ExecuteAsync(@"
            INSERT INTO trx_feedback_point_approval 
            (id, trx_feedback_point_id, status, approved_by, approved_date, feedback_status)
            VALUES (uuid_generate_v4(), @id, @status, @user, @now, @feedback_status)",
            new { id, status = rejectStatus, user = username, now, feedback_status = feedback.Status });
            
            // Update status header trx_feedback
            await conn.ExecuteAsync(@"
            UPDATE trx_feedback 
            SET status = @status, 
                updated_by = @user,
                updated_date = @now
            WHERE id = @fid",
                new { fid = feedback.FeedbackId, status = rejectStatus, user = username, now });

            return RedirectToAction("Detail", new { id = feedback.FeedbackId });
        }


        [HttpPost("Banding/SubmitFinalApproval/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitFinalApproval(string id)
        {
            var tf = await _context.TrxFeedbacks.FindAsync(id);
            if (tf == null)
                return NotFound();

            var username = User.Identity?.Name ?? "SYSTEM";
            var status = (tf.Status ?? "").Trim().ToUpperInvariant();

            // Hanya ketika naik dari APPROVE_PPN atau APPROVE_CBI → insert ke trx_feedback_point_approval
            if (tf.Status == "APPROVE_PPN" || tf.Status == "APPROVE_CBI")
            {
                username = tf.ApprovalBy;
                var now = DateTime.Now;

                var pointIdFromApproval = await (
                    from tfpa in _context.TrxFeedbackPointApprovals
                    join tfp in _context.TrxFeedbackPoints on tfpa.TrxFeedbackPointId equals tfp.Id
                    where tfp.TrxFeedbackId == id
                    select tfpa.TrxFeedbackPointId
                ).FirstOrDefaultAsync();

                // Fallback: kalau belum ada satupun approval terdahulu, ambil 1 point dari feedback ini
                if (string.IsNullOrEmpty(pointIdFromApproval))
                {
                    pointIdFromApproval = await _context.TrxFeedbackPoints
                        .Where(p => p.TrxFeedbackId == id)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();
                }

                // Jika tetap tidak ada point, lewati insert agar tidak error (opsional: bisa kamu buat NotFound/BadRequest)
                if (!string.IsNullOrEmpty(pointIdFromApproval))
                {
                    var approval = new TrxFeedbackPointApproval
                    {
                        Id = Guid.NewGuid().ToString(),
                        TrxFeedbackPointId = pointIdFromApproval,
                        Status = "APPROVED",
                        Notes = "",
                        ApprovedBy = username,
                        ApprovedDate = now,
                        feedback_status = tf.Status
                    };

                    _context.TrxFeedbackPointApprovals.Add(approval);
                }
            }

            // Naikkan status sesuai current step
            switch (status)
            {
                case "UNDER_REVIEW":
                    tf.Status = "APPROVE_SBM";
                    break;
                case "APPROVE_SBM":
                    tf.Status = "APPROVE_PPN";
                    break;
                case "APPROVE_PPN":
                    tf.Status = "APPROVE_CBI";
                    break;
                case "APPROVE_CBI":
                    tf.Status = "APPROVED";
                    break;
                default:
                    tf.Status = "APPROVED";
                    break;
            }

            tf.UpdatedBy = username;
            tf.UpdatedDate = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Status berhasil diperbarui.";
            return RedirectToAction("Index");
        }

        public async Task<bool> CheckAllPointsApprovedAsync(string feedbackId, string statusCode)
        {
            var cs = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            var query = @"
        SELECT COUNT(*) FILTER (WHERE latest.status != 'APPROVED') AS not_approved
        FROM trx_feedback_point p
        LEFT JOIN LATERAL (
            SELECT status
            FROM trx_feedback_point_approval a
            WHERE a.trx_feedback_point_id = p.id AND a.status_code = @statusCode
            ORDER BY approved_date DESC
            LIMIT 1
        ) latest ON true
        WHERE p.trx_feedback_id = @feedbackId";

            var notApproved = await conn.ExecuteScalarAsync<int>(query, new { feedbackId, statusCode });
            return notApproved == 0;
        }


        // Helper
        private async Task<string> GetFeedbackIdByPointId(string pointId)
        {
            var cs = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            return await conn.ExecuteScalarAsync<string>(@"
        SELECT trx_feedback_id 
        FROM trx_feedback_point 
        WHERE id = @id", new { id = pointId });
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

        private static string MapStatusText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Menunggu Persetujuan";
            var s = raw.Trim().ToUpperInvariant();
            return s switch
            {
                "REJECTED" => "Ditolak",
                "REJECT_CBI" => "Ditolak",
                "UNDER_REVIEW" => "Menunggu Persetujuan SBM",
                "APPROVE_SBM" => "Menunggu Persetujuan PPN",
                "APPROVE_PPN" => "Menunggu Persetujuan CBI",
                "APPROVE_CBI" => "Menunggu Persetujuan Pertamina",
                "APPROVED" => "Banding Disetujui",
                _ => raw
            };
        }

        // ===== Helper: ambil role user (SBM/PPN/CBI/PERTAMINA) =====
        private async Task<HashSet<string>> GetUserRoleSetAsync(string userId)
        {
            var wanted = new[] { "SBM", "PPN", "CBI", "PERTAMINA" };
            var set = new HashSet<string>(
                await (from ur in _context.app_user_roles
                       join r in _context.app_roles on ur.app_role_id equals r.id
                       where ur.app_user_id == userId && wanted.Contains(r.name.ToUpper())
                       select r.name.ToUpper()).ToListAsync()
            );
            return set;
        }

        // ===== Helper: cek otorisasi sesuai tahap/status =====
        private static bool IsAuthorizedForCurrentStage(string currentStatus, HashSet<string> roleSet)
        {
            var s = (currentStatus ?? "").Trim().ToUpperInvariant();
            return (s == "UNDER_REVIEW" && roleSet.Contains("SBM"))
                || (s == "APPROVE_SBM" && roleSet.Contains("PPN"))
                || (s == "APPROVE_PPN" && roleSet.Contains("CBI"))
                || (s == "APPROVE_CBI" && roleSet.Contains("PERTAMINA"));
        }

        // POST: /Complain/Approve
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(string id, [FromForm] string klarifikasi = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var entity = await _context.TrxFeedbacks.FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null)
                return NotFound();

            // Hanya ketika naik dari APPROVE_PPN atau APPROVE_CBI → insert ke trx_feedback_point_approval
            if (entity.Status == "APPROVE_PPN" || entity.Status == "APPROVE_CBI")
            {
                var username = entity.ApprovalBy;
                var now = DateTime.Now;

                var pointIdFromApproval = await (
                    from tfpa in _context.TrxFeedbackPointApprovals
                    join tfp in _context.TrxFeedbackPoints on tfpa.TrxFeedbackPointId equals tfp.Id
                    where tfp.TrxFeedbackId == id
                    select tfpa.TrxFeedbackPointId
                ).FirstOrDefaultAsync();

                // Fallback: kalau belum ada satupun approval terdahulu, ambil 1 point dari feedback ini
                if (string.IsNullOrEmpty(pointIdFromApproval))
                {
                    pointIdFromApproval = await _context.TrxFeedbackPoints
                        .Where(p => p.TrxFeedbackId == id)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();
                }

                // Jika tetap tidak ada point, lewati insert agar tidak error (opsional: bisa kamu buat NotFound/BadRequest)
                if (!string.IsNullOrEmpty(pointIdFromApproval))
                {
                    var approval = new TrxFeedbackPointApproval
                    {
                        Id = Guid.NewGuid().ToString(),
                        TrxFeedbackPointId = pointIdFromApproval,
                        Status = "APPROVED",
                        Notes = "",
                        ApprovedBy = username,
                        ApprovedDate = now,
                        feedback_status = entity.Status
                    };

                    _context.TrxFeedbackPointApprovals.Add(approval);
                }
            }

            // Tentukan status baru berdasarkan status saat ini
            if (entity.Status == "UNDER_REVIEW")
            {
                entity.Status = "APPROVE_SBM";
            }
            else if (entity.Status == "APPROVE_SBM")
            {
                entity.Status = "APPROVE_PPN";
            }
            else if (entity.Status == "APPROVE_PPN")
            {
                entity.Status = "APPROVE_CBI";
            }
            else if (entity.Status == "APPROVE_CBI")
            {
                entity.Status = "APPROVED";
            }
            else
            {
                entity.Status = "APPROVED";
            }

            entity.ApprovalBy = User?.Identity?.Name ?? "system";
            entity.ApprovalDate = DateTime.Now;
            entity.UpdatedBy = entity.ApprovalBy;
            entity.UpdatedDate = DateTime.Now;
            entity.Klarifikasi = klarifikasi;

            await _context.SaveChangesAsync();

            TempData["AlertSuccess"] = "Pengajuan telah disetujui.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        // POST: /Complain/Reject
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(string id, string reason = "", [FromForm] string klarifikasi = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var entity = await _context.TrxFeedbacks.FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null) return NotFound();

            // Hanya ketika naik dari APPROVE_PPN atau APPROVE_CBI → insert ke trx_feedback_point_approval
            if (entity.Status == "APPROVE_PPN" || entity.Status == "APPROVE_CBI")
            {
                var username = entity.ApprovalBy;
                var now = DateTime.Now;

                var pointIdFromApproval = await (
                    from tfpa in _context.TrxFeedbackPointApprovals
                    join tfp in _context.TrxFeedbackPoints on tfpa.TrxFeedbackPointId equals tfp.Id
                    where tfp.TrxFeedbackId == id
                    select tfpa.TrxFeedbackPointId
                ).FirstOrDefaultAsync();

                // Fallback: kalau belum ada satupun approval terdahulu, ambil 1 point dari feedback ini
                if (string.IsNullOrEmpty(pointIdFromApproval))
                {
                    pointIdFromApproval = await _context.TrxFeedbackPoints
                        .Where(p => p.TrxFeedbackId == id)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();
                }

                // Jika tetap tidak ada point, lewati insert agar tidak error (opsional: bisa kamu buat NotFound/BadRequest)
                if (!string.IsNullOrEmpty(pointIdFromApproval))
                {
                    var approval = new TrxFeedbackPointApproval
                    {
                        Id = Guid.NewGuid().ToString(),
                        TrxFeedbackPointId = pointIdFromApproval,
                        Status = "REJECTED",
                        Notes = "",
                        ApprovedBy = username,
                        ApprovedDate = now,
                        feedback_status = entity.Status
                    };

                    _context.TrxFeedbackPointApprovals.Add(approval);
                }
            }

            var rejectStatus = entity.Status == "APPROVE_PPN" ? "REJECT_CBI" : "REJECTED";
            entity.Status = rejectStatus;
            entity.ApprovalBy = User?.Identity?.Name ?? "system";
            entity.ApprovalDate = DateTime.Now;
            entity.UpdatedBy = entity.ApprovalBy;
            entity.UpdatedDate = DateTime.Now;
            entity.Klarifikasi = klarifikasi;

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
