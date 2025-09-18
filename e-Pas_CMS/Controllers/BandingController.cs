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
using static Dapper.SqlMapper;

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
        ("APPROVE",     "Banding Disetujui"),
        ("BYPASS_APPROVE_PPN", "Bypass Approve PPN"),
        ("REJECT",     "Ditolak"),
        ("REJECT_CBI",   "Ditolak"),
    };

            ViewBag.StatusOptions = statusOptions;
            ViewBag.StatusFilter = statusFilter ?? "";
            ViewBag.Type = type;
            ViewBag.SortColumn = sortColumn;
            ViewBag.SortDirection = sortDirection;

            // baseQuery
            var baseQuery =
                from fb in _context.TrxFeedbacks
                join ta in _context.trx_audits on fb.TrxAuditId equals ta.id
                join sp in _context.spbus on ta.spbu_id equals sp.id
                join au0 in _context.app_users on ta.app_user_id equals au0.id into aud1
                from au in aud1.DefaultIfEmpty()
                where fb.FeedbackType == type && fb.Status != "IN_PROGRESS_SUBMIT"
                select new { Fb = fb, Ta = ta, Sp = sp, Auditor = au != null ? au.name : null };

            // FILTER: status
            if (!string.IsNullOrWhiteSpace(statusFilter))
                baseQuery = baseQuery.Where(x => x.Fb.Status == statusFilter);

            // FILTER: search
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

            // ===== Sorting di DB (tambahkan StatusText → pakai Fb.Status)
            IOrderedQueryable<dynamic> ordered = sortColumn switch
            {
                "TicketNo" => asc ? baseQuery.OrderBy(x => x.Fb.TicketNo) : baseQuery.OrderByDescending(x => x.Fb.TicketNo),
                "NoSpbu" => asc ? baseQuery.OrderBy(x => x.Sp.spbu_no) : baseQuery.OrderByDescending(x => x.Sp.spbu_no),
                "City" => asc ? baseQuery.OrderBy(x => x.Sp.city_name) : baseQuery.OrderByDescending(x => x.Sp.city_name),
                "Rayon" => asc ? baseQuery.OrderBy(x => x.Sp.region) : baseQuery.OrderByDescending(x => x.Sp.region),
                "Auditor" => asc ? baseQuery.OrderBy(x => x.Auditor) : baseQuery.OrderByDescending(x => x.Auditor),
                "TanggalAudit" => asc ? baseQuery.OrderBy(x => x.Ta.audit_execution_time) : baseQuery.OrderByDescending(x => x.Ta.audit_execution_time),
                "TipeAudit" => asc ? baseQuery.OrderBy(x => x.Ta.audit_type) : baseQuery.OrderByDescending(x => x.Ta.audit_type),
                "AuditLevel" => asc ? baseQuery.OrderBy(x => x.Ta.audit_level) : baseQuery.OrderByDescending(x => x.Ta.audit_level),
                "StatusText" => asc ? baseQuery.OrderBy(x => x.Fb.Status) : baseQuery.OrderByDescending(x => x.Fb.Status), // NEW
                                                                                                                           // "Score" dihitung di bawah; default pakai tanggal audit
                _ => asc ? baseQuery.OrderBy(x => x.Ta.audit_execution_time) : baseQuery.OrderByDescending(x => x.Ta.audit_execution_time),
            };

            var pageItems = await ordered
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Hitung score per baris (Dapper + Npgsql)
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

            var result = new List<e_Pas_CMS.ViewModels.BandingListItemViewModel>(pageItems.Count);
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

                result.Add(new e_Pas_CMS.ViewModels.BandingListItemViewModel
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
                    Score = it.Ta.score, // atau finalScore bila mau pakai hitungan di atas
                    TanggalPengajuan = it.Fb.CreatedDate,
                    StatusCode = it.Fb.Status,
                    StatusText = (it.Fb.Status ?? "").ToUpperInvariant() switch
                    {
                        "REJECT" => "Ditolak",
                        "REJECT_CBI" => "Ditolak",
                        "UNDER_REVIEW" => "Menunggu Persetujuan SBM",
                        "APPROVE_SBM" => "Menunggu Persetujuan PPN",
                        "APPROVE_PPN" => "Menunggu Persetujuan CBI",
                        "APPROVE_CBI" => "Menunggu Persetujuan Pertamina",
                        "APPROVE" => "Banding Disetujui",
                        "BYPASS_APPROVE_PPN" => "Bypass Approve PPN",
                        _ => it.Fb.Status ?? "-"
                    }
                });
            }

            var vm = new e_Pas_CMS.ViewModels.PaginationModel<e_Pas_CMS.ViewModels.BandingListItemViewModel>
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

            var connect = _context.Database.GetDbConnection();
            if (connect.State != ConnectionState.Open)
                await connect.OpenAsync();

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

            string beforerevised = "";
            string afterrevised = "";

            string status = header.Tf.Status;

            if (status != "APPROVE" || header.Tf.Next_audit_before == null)
            {
                beforerevised = header.S.audit_next;
                afterrevised = "-";
            }

            else
            {
                beforerevised = header.Tf.Next_audit_before;
                afterrevised = header.S.audit_next;
            }

            var klarifnotes = await GetMediaKlarifikasiAsync(connect, id);

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
                Klarifikasi = header.Tf.Klarifikasi,
                SebelumRevisi = beforerevised,
                SesudahRevisi = afterrevised
            };

            var codes = new[]
            {
        "BANDING_CLOSING_DATE_SPBU",
        "BANDING_CLOSING_DATE_SBM",
        "BANDING_CLOSING_DATE_PPN",
        "BANDING_CLOSING_DATE_CBI",
        "BANDING_CLOSING_DATE_ALL"
    };

            var paramDict = await _context.SysParameter
                .Where(p => p.Status == "ACTIVE" && codes.Contains(p.Code))
                .ToDictionaryAsync(p => p.Code, p => p.Value);

            int DayOr(string code, int def) =>
                (paramDict.TryGetValue(code, out var v) && int.TryParse(v, out var d) && d >= 1 && d <= 31)
                    ? d : def;

            // default fallback bila parameter kosong
            var daySPBU = DayOr("BANDING_CLOSING_DATE_SPBU", 5);
            var daySBM = DayOr("BANDING_CLOSING_DATE_SBM", 7);
            var dayPPN = DayOr("BANDING_CLOSING_DATE_PPN", 8);
            var dayCBI = DayOr("BANDING_CLOSING_DATE_CBI", 15);
            var dayALL = DayOr("BANDING_CLOSING_DATE_ALL", 15);

            // ====== Stage & deadline ======
            string StageNow(string status)
            {
                status = (status ?? "").Trim().ToUpperInvariant();
                if (status.StartsWith("APPROVE_CBI") || status.Contains("_CBI")) return "ALL";
                if (status.StartsWith("APPROVE_PPN") || status.Contains("_PPN")) return "CBI";
                if (status.StartsWith("APPROVE_SBM") || status.Contains("_SBM")) return "PPN";
                if (status.StartsWith("UNDER_REVIEW") || status.Contains("IN_PROGRESS")) return "SBM";
                if (status.Contains("SPBU")) return "SPBU";
                return "ALL";
            }

            string stage = StageNow(header.Tf.Status);
            DateTime NowJakarta() => DateTime.Now;

            DateTime ClampDay(int y, int m, int day) =>
                new DateTime(y, m, Math.Min(day, DateTime.DaysInMonth(y, m)), 23, 59, 59, DateTimeKind.Local);

            DateTime ComputeDeadline(int day, DateTime now, bool nextIfPassed)
            {
                var dl = ClampDay(now.Year, now.Month, day);
                if (nextIfPassed && now > dl)
                {
                    var next = now.AddMonths(1);
                    dl = ClampDay(next.Year, next.Month, day);
                }
                return dl;
            }

            var now = NowJakarta();
            int chosenDay = stage switch
            {
                "SPBU" => daySPBU,
                "SBM" => daySBM,
                "PPN" => dayPPN,
                "CBI" => dayCBI,
                _ => dayALL
            };

            var deadlineThisWindow = ClampDay(now.Year, now.Month, chosenDay);
            var isPassed = now > deadlineThisWindow;
            var nextDeadline = ComputeDeadline(chosenDay, now, true);

            ViewBag.BandingClosing = new
            {
                Stage = stage,
                Day = chosenDay,
                Deadline = deadlineThisWindow,
                NextDeadline = nextDeadline,
                IsPassed = isPassed,
                DaySPBU = daySPBU,
                DaySBM = daySBM,
                DayPPN = dayPPN,
                DayCBI = dayCBI,
                DayALL = dayALL
            };

            vm.MediaKlarifikasi = klarifnotes
                .Where(n => !string.IsNullOrWhiteSpace(n.MediaPath))
                .Select(n => new KlfAttachmentItem
                {
                    Id = null,
                    FileName = SafeGetFileName(n.MediaPath),
                    Url = n.MediaPath,
                    MediaType = NormalizeType(n.MediaType) ?? GetExtFromUrl(n.MediaPath),
                    SizeReadable = null
                }).ToList();

            ViewBag.KlfAttachmentsJson = JsonConvert.SerializeObject(vm.MediaKlarifikasi);

            var cs = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            var pointRows = await conn.QueryAsync<PointRow>(@"
SELECT 
    p.id AS point_id,
    fe.trx_audit_id,
    p.description AS description,
    CASE 
        WHEN EXISTS (
            SELECT 1
            FROM trx_feedback_point_approval tfpa
            WHERE tfpa.trx_feedback_point_id = p.id
              AND tfpa.status IN ('REJECT','REJECT_CBI')
        ) 
        THEN COALESCE(e.number || '. ' || e.title, e.title, e.description, '-') || ' (REJECT)'
        ELSE COALESCE(e.number || '. ' || e.title, e.title, e.description, '-')
    END AS element_label,
    COALESCE(se.number || '. ' || se.title, se.title, se.description, '-') AS sub_element_label,
    COALESCE(de.number || '. ' || de.title, de.title, '-')                 AS detail_element_label,
    COALESCE((
        SELECT string_agg(me.number || ' ' || me.title, E'\n' ORDER BY me.number)
        FROM trx_feedback_point_element pe
        JOIN master_questioner_detail me ON me.id = pe.master_questioner_detail_id
        WHERE pe.trx_feedback_point_id = p.id
    ), '') AS compared_elements,
    COALESCE((
        SELECT string_agg('https://epas-assets.zarata.co.id' || t.media_path, ',' 
                          ORDER BY t.created_date, t.media_path)
        FROM (
            SELECT DISTINCT tam.media_path, tam.created_date
            FROM trx_feedback_point_element pe
            JOIN master_questioner_detail me 
              ON me.id = pe.master_questioner_detail_id
            JOIN trx_audit_media tam 
              ON tam.master_questioner_detail_id = me.id
            WHERE pe.trx_feedback_point_id = p.id 
              AND tam.trx_audit_id = fe.trx_audit_id
              AND tam.media_path IS NOT NULL 
              AND tam.media_type = 'IMAGE'
        ) t
    ), '') AS media_elements,
    COALESCE((
        SELECT string_agg('https://epas-assets.zarata.co.id' || t.media_path, ',' 
                          ORDER BY t.created_date, t.media_path)
        FROM (
            SELECT DISTINCT tfpm.media_path, tfpm.created_date
            FROM trx_feedback_point_media tfpm
            WHERE tfpm.trx_feedback_point_id = p.id
              AND tfpm.media_path IS NOT NULL
        ) t
    ), '') AS point_media_elements
FROM trx_feedback_point p
LEFT JOIN trx_feedback fe ON p.trx_feedback_id = fe.id
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
                    mediaElement = string.IsNullOrWhiteSpace(pr.media_elements)
                    ? new List<string>()
                    : pr.media_elements.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim())
                                       .Where(s => !string.IsNullOrWhiteSpace(s))
                                       .ToList(),
                    pointMediaElements = string.IsNullOrWhiteSpace(pr.point_media_elements)
        ? new List<string>()
        : pr.point_media_elements
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList(),
                    Description = pr.description,
                    Attachments = new List<AttachmentItem>(),
                    History = new List<PointApprovalHistory>()
                };

                if (idx == 1 && string.IsNullOrWhiteSpace(vm.BodyText))
                    vm.BodyText = pr.description;

                // 1. Media
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

                // 2. History approval per poin
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

                if (!approvals.Any(h => h.status == "APPROVE"))
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

            ViewBag.AttachmentsJson = JsonConvert.SerializeObject(vm.Attachments);
            ViewBag.AllPointsApproved = allApproved;
            ViewBag.BandingId = id;

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

            // =========================
            // NEW: Riwayat Klarifikasi (typed)
            // =========================
            var klarHist = new List<KlarifikasiLogItem>();
            try
            {
                var logs = await connect.QueryAsync(@"
            SELECT created_date, created_by, klarifikasi_text
            FROM trx_feedback_klarifikasi_log
            WHERE trx_feedback_id = @fid
            ORDER BY created_date DESC;", new { fid = id });

                foreach (var row in logs)
                {
                    klarHist.Add(new KlarifikasiLogItem
                    {
                        CreatedDate = (DateTime)row.created_date,
                        CreatedBy = (string)row.created_by,
                        Text = (string?)row.klarifikasi_text ?? ""
                    });
                }
            }
            catch
            {
                // abaikan jika tabel belum ada
            }

            // Fallback jika tidak ada log: tampilkan minimal versi terakhir
            if (klarHist.Count == 0 && !string.IsNullOrWhiteSpace(header.Tf.Klarifikasi))
            {
                var lastDate = header.Tf.UpdatedDate != DateTime.MinValue
                ? header.Tf.UpdatedDate
                : (header.Tf.CreatedDate != DateTime.MinValue
                    ? header.Tf.CreatedDate
                    : DateTime.Now);

                var lastBy = header.Tf.UpdatedBy ?? header.Tf.CreatedBy ?? "-";
                klarHist.Add(new KlarifikasiLogItem
                {
                    CreatedDate = (DateTime)lastDate,
                    CreatedBy = lastBy,
                    Text = header.Tf.Klarifikasi ?? ""
                });
            }

            vm.KlarifikasiHistory = klarHist; // <<-- NEW

            // (opsional) jika masih dipakai di View lain
            // ViewBag.StatusNormUpper = (header.Tf.Status ?? "").ToUpperInvariant();

            return View(vm);
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BypassApprovePpn(string id, string? notes = null)
        {
            // id = trx_feedback.id
            var username = User?.Identity?.Name ?? "SYSTEM";
            var cs = _context.Database.GetConnectionString();

            await using var conn = new Npgsql.NpgsqlConnection(cs);
            await conn.OpenAsync();

            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                // Pastikan header ada & statusnya APPROVE_SBM
                var header = await conn.QueryFirstOrDefaultAsync<(string Id, string Status)>(@"
            SELECT id, status
            FROM trx_feedback
            WHERE id = @id
            FOR UPDATE
        ", new { id }, tx);

                if (header.Id == null)
                {
                    TempData["Error"] = "Data tidak ditemukan.";
                    await tx.RollbackAsync();
                    return RedirectToAction(nameof(Detail), new { id });
                }

                var currentStatus = (header.Status ?? "").Trim().ToUpperInvariant();
                if (currentStatus != "APPROVE_SBM")
                {
                    TempData["Error"] = "Bypass hanya dapat dilakukan saat status APPROVE_SBM.";
                    await tx.RollbackAsync();
                    return RedirectToAction(nameof(Detail), new { id });
                }

                // Ambil semua point id di tiket ini
                var pointIds = (await conn.QueryAsync<string>(@"
            SELECT p.id
            FROM trx_feedback_point p
            WHERE p.trx_feedback_id = @id
        ", new { id }, tx)).ToList();

                // Update header langsung ke APPROVE
                await conn.ExecuteAsync(@"
            UPDATE trx_feedback
            SET status = 'APPROVE', updated_by = @user, updated_date = NOW()
            WHERE id = @id
        ", new { id, user = username }, tx);

                // Insert riwayat BYPASS_APPROVE_PPN untuk setiap point
                if (pointIds.Count > 0)
                {
                    // gunakan COPY atau multi VALUES. Di sini multi VALUES sederhana:
                    var sql = @"
                INSERT INTO trx_feedback_point_approval
                    (id, trx_feedback_point_id, status, notes, approved_by, approved_date, feedback_status)
                VALUES
                    (@newid, @pid, 'BYPASS_APPROVE_PPN', @notes, @user, NOW(), 'APPROVE')
            ";

                    foreach (var pid in pointIds)
                    {
                        await conn.ExecuteAsync(sql, new
                        {
                            newid = Guid.NewGuid().ToString(),
                            pid,
                            notes,
                            user = username
                        }, tx);
                    }
                }

                await tx.CommitAsync();
                TempData["Success"] = "Bypass Approve (PPN) berhasil. Status tiket menjadi APPROVE.";
                return RedirectToAction(nameof(Detail), new { id });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _ = ex; // optionally log
                TempData["Error"] = "Terjadi kesalahan saat bypass approve.";
                return RedirectToAction(nameof(Detail), new { id });
            }
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
        VALUES (uuid_generate_v4(), @id, 'APPROVE', @user, NOW(), @feedback_status)",
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

            // --- Mulai transaksi ---
            await using var tx = await conn.BeginTransactionAsync();

            // 1) Ambil id feedback + status aktif
            var feedback = await conn.QueryFirstOrDefaultAsync<(string FeedbackId, string Status)>(@"
        SELECT tf.id AS FeedbackId, tf.status 
        FROM trx_feedback_point p
        JOIN trx_feedback tf ON tf.id = p.trx_feedback_id
        WHERE p.id = @id",
                new { id }, tx);

            if (feedback.FeedbackId == null)
            {
                await tx.RollbackAsync();
                return NotFound();
            }

            var now = DateTime.Now;

            // 2) Tentukan status header reject (berdasarkan stage saat ini)
            var rejectStatus = feedback.Status == "APPROVE_PPN" ? "REJECT_CBI" : "REJECT";

            // 3) Simpan rejection point
            //    Penting: tfpa.status = 'REJECTED' supaya cocok dengan query cek kamu.
            await conn.ExecuteAsync(@"
        INSERT INTO trx_feedback_point_approval 
        (id, trx_feedback_point_id, status, approved_by, approved_date, feedback_status)
        VALUES (uuid_generate_v4(), @id, 'REJECT', @user, @now, @feedback_status)",
                new { id, user = username, now, feedback_status = feedback.Status }, tx);

            // 4) Cek apakah masih ada point yang BELUM punya approval 'REJECTED'
            //    Jika count == 0 => semua point sudah di-reject => update header
            var remainingCount = await conn.ExecuteScalarAsync<int>(@"
        SELECT COUNT(*)
        FROM trx_feedback_point tfp
        WHERE tfp.trx_feedback_id = @fid
          AND NOT EXISTS (
              SELECT 1
              FROM trx_feedback_point_approval tfpa
              WHERE tfpa.trx_feedback_point_id = tfp.id
                AND tfpa.status = 'REJECT'
          )",
                new { fid = feedback.FeedbackId }, tx);

            if (remainingCount == 0)
            {
                await conn.ExecuteAsync(@"
            UPDATE trx_feedback 
            SET status = @status, 
                updated_by = @user,
                updated_date = @now
            WHERE id = @fid",
                    new { fid = feedback.FeedbackId, status = rejectStatus, user = username, now }, tx);
            }

            // 5) Commit transaksi
            await tx.CommitAsync();

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
                        Status = "APPROVE",
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
                    tf.Status = "APPROVE";
                    break;
                default:
                    tf.Status = "APPROVE";
                    break;
            }

            tf.UpdatedBy = username;
            tf.UpdatedDate = DateTime.Now;
            await _context.SaveChangesAsync();

            string sql = @"
                UPDATE trx_audit
                SET 
                    is_revision = @p0,
                    updated_by = @p1,
                    updated_date = now()
                WHERE id = @p2"
            ;

            int affected = await _context.Database.ExecuteSqlRawAsync(sql, true, User?.Identity?.Name ?? "system", tf.TrxAuditId);

            TempData["Success"] = "Status berhasil diperbarui.";
            return RedirectToAction("Index");
        }

        public async Task<bool> CheckAllPointsApprovedAsync(string feedbackId, string statusCode)
        {
            var cs = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            var query = @"
        SELECT COUNT(*) FILTER (WHERE latest.status != 'APPROVE') AS not_approved
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

        private async Task<List<MediaItem>> GetMediaKlarifikasiAsync(IDbConnection conn, string id)
        {
            const string sql = @"
            SELECT id, media_type, media_path
            FROM trx_feedback_clarification_media
            WHERE trx_feedback_id = @id
            ORDER BY created_date ASC;";

            var rows = await conn.QueryAsync<MediaRow>(sql, new { id });

            return rows.Select(x => new MediaItem
            {
                MediaType = x.media_type,
                MediaPath = "https://epas-assets.zarata.co.id" + x.media_path
            }).ToList();
        }

        private async Task<List<MediaItem>> GetMediaNotesAuditAsync(IDbConnection conn, string id)
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
                "REJECT" => "Ditolak",
                "REJECT_CBI" => "Ditolak",
                "UNDER_REVIEW" => "Menunggu Persetujuan SBM",
                "APPROVE_SBM" => "Menunggu Persetujuan PPN",
                "APPROVE_PPN" => "Menunggu Persetujuan CBI",
                "APPROVE_CBI" => "Menunggu Persetujuan Pertamina",
                "APPROVE" => "Banding Disetujui",
                "BYPASS_APPROVE_PPN" => "Bypass Approve PPN",
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateKlarifikasi([FromBody] UpdateKlarifikasiRequest model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.BandingId))
                return BadRequest("Data tidak valid");

            var currentUser = User.Identity?.Name ?? "system";

            string sql = @"
        UPDATE trx_feedback
        SET klarifikasi = @klarifikasi,
            updated_by = @updatedBy,
            updated_date = NOW()
        WHERE id = @id";

            var affected = await _context.Database.ExecuteSqlRawAsync(sql,
                new Npgsql.NpgsqlParameter("@klarifikasi", model.Text ?? (object)DBNull.Value),
                new Npgsql.NpgsqlParameter("@updatedBy", currentUser),
                new Npgsql.NpgsqlParameter("@id", model.BandingId));

            if (affected == 0)
                return NotFound("Data banding tidak ditemukan");

            return Json(new { success = true });
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
                        Status = "APPROVE",
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
                entity.Status = "APPROVE";

                string sql = @"
                UPDATE trx_audit
                SET 
                    is_revision = @p0,
                    updated_by = @p1,
                    updated_date = now()
                WHERE id = @p2";

                int affected = await _context.Database.ExecuteSqlRawAsync(sql, true, User?.Identity?.Name ?? "system", entity.TrxAuditId);

            }
            else
            {
                entity.Status = "APPROVE";
                string sql = @"
                UPDATE trx_audit
                SET 
                    is_revision = @p0,
                    updated_by = @p1,
                    updated_date = now()
                WHERE id = @p2";

                int affected = await _context.Database.ExecuteSqlRawAsync(sql, true, User?.Identity?.Name ?? "system", entity.TrxAuditId);
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
                        Status = "REJECT",
                        Notes = "",
                        ApprovedBy = username,
                        ApprovedDate = now,
                        feedback_status = entity.Status
                    };

                    _context.TrxFeedbackPointApprovals.Add(approval);
                }
            }

            var rejectStatus = entity.Status == "APPROVE_PPN" ? "REJECT_CBI" : "REJECT";
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

        [HttpPost("Banding/UploadKlarifikasiMedia")]
        public async Task<IActionResult> UploadKlarifikasiMedia(IFormFile file, string bandingId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File tidak ditemukan atau kosong.");

            var generatedNodeId = Guid.NewGuid().ToString();
            var uploadsPath = Path.Combine("/var/www/epas-asset", "wwwroot", "uploads", bandingId);

            Directory.CreateDirectory(uploadsPath);

            var fileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadsPath, fileName);

            // Simpan file fisik
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Simpan informasi file ke database
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var insertSql = @"
INSERT INTO trx_feedback_clarification_media 
    (id, trx_feedback_id, media_type, media_path, status, created_date, created_by, updated_date, updated_by)
VALUES 
    (uuid_generate_v4(), @bandingId, @mediaType, @mediaPath, 'ACTIVE', NOW(), @createdBy, NOW(), @createdBy)";

            await conn.ExecuteAsync(insertSql, new
            {
                bandingId,
                mediaType = Path.GetExtension(fileName).Trim('.').ToLower(),
                mediaPath = $"/uploads/{bandingId}/{fileName}",
                createdBy = User.Identity?.Name ?? "anonymous"
            });

            return RedirectToAction("Detail", new { id = bandingId });
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
