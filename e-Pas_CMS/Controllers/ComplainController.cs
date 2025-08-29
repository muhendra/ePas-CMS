using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;

namespace e_Pas_CMS.Controllers
{
    [Route("[controller]")]
    public class ComplainController : Controller
    {
        private readonly EpasDbContext _context;
        private const int DefaultPageSize = 25;

        public ComplainController(EpasDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // LIST (COMPLAINT ONLY)
        // ============================================================
        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(
            string searchTerm = "",
            int pageNumber = 1,
            int pageSize = DefaultPageSize,
            string sortColumn = "TanggalAudit",
            string sortDirection = "desc")
        {
            ViewBag.SearchTerm = searchTerm ?? "";
            ViewBag.SortColumn = sortColumn;
            ViewBag.SortDirection = sortDirection;

            var baseQuery =
                from fb in _context.TrxFeedbacks
                join ta in _context.trx_audits on fb.TrxAuditId equals ta.id
                join sp in _context.spbus on ta.spbu_id equals sp.id
                join au0 in _context.app_users on ta.app_user_id equals au0.id into aud1
                from au in aud1.DefaultIfEmpty()
                where fb.FeedbackType == "COMPLAINT"
                orderby fb.CreatedDate descending
                select new
                {
                    Fb = fb,
                    Ta = ta,
                    Sp = sp,
                    Auditor = au != null ? au.name : null
                };

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

            // --- Sort by Score (butuh kalkulasi manual) ---
            if (string.Equals(sortColumn, "Score", StringComparison.OrdinalIgnoreCase))
            {
                var allItems = await baseQuery
                    .OrderByDescending(x => x.Ta.updated_date)
                    .ToListAsync();

                var connectionString = _context.Database.GetConnectionString();
                await using var connAll = new NpgsqlConnection(connectionString);
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
                    DateTime auditDate = x.Row.Ta.audit_execution_time;
                    if (auditDate == default || auditDate == DateTime.MinValue)
                        auditDate = x.Row.Ta.updated_date ?? DateTime.Now;

                    return new BandingListItemViewModel
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
                        Score = x.Score,
                        TanggalPengajuan = x.Row.Fb.CreatedDate
                    };
                }).ToList();

                var vmScore = new PaginationModel<BandingListItemViewModel>
                {
                    Items = resultByScore,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalItems = totalItems
                };

                return View(vmScore);
            }

            // --- Sort kolom lain langsung di DB ---
            IOrderedQueryable<dynamic> orderedDb = sortColumn switch
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
                "TanggalPengajuan" => asc ? baseQuery.OrderBy(x => x.Fb.CreatedDate) : baseQuery.OrderByDescending(x => x.Fb.CreatedDate),
                _ => asc ? baseQuery.OrderBy(x => x.Ta.audit_execution_time)
                                         : baseQuery.OrderByDescending(x => x.Ta.audit_execution_time),
            };

            var pageItems = await orderedDb
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var cs = _context.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
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
                    Score = Math.Round(finalScore, 2),
                    TanggalPengajuan = it.Fb.CreatedDate
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

        // ============================================================
        // DETAIL (COMPLAINT ONLY)
        // ============================================================
        [HttpGet("Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            // Header COMPLAINT saja
            var header =
                await (from tf in _context.TrxFeedbacks
                       join ta in _context.trx_audits on tf.TrxAuditId equals ta.id
                       join s in _context.spbus on ta.spbu_id equals s.id
                       join au in _context.app_users on ta.app_user_id equals au.id into aud1
                       from au in aud1.DefaultIfEmpty()
                       where tf.Id == id && tf.FeedbackType == "COMPLAINT"
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
                // jenis & status
                IsBanding = false,
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
                CreatedDate = header.Tf.CreatedDate,

                // Konten
                Description = null,                 // <-- akan diisi dari tfp.description
                Points = new List<PointItem>(),// kosong untuk complaint
                Attachments = new List<AttachmentItem>(),

                // Aksi (opsional sesuai status)
                CanApprove = string.Equals(header.Tf.Status, "IN_PROGRESS_SUBMIT", StringComparison.OrdinalIgnoreCase),
                CanReject = string.Equals(header.Tf.Status, "IN_PROGRESS_SUBMIT", StringComparison.OrdinalIgnoreCase),

                feedback_type = "COMPLAINT",
                AuditId = header.Ta.id
            };

            // Ambil semua description komplain (tanpa lookup elemen)
            var cs = _context.Database.GetConnectionString();
            await using var conn = new Npgsql.NpgsqlConnection(cs);
            await conn.OpenAsync();

            const string sqlComplaintDesc = @"
        SELECT tfp.description
        FROM trx_feedback tf
        JOIN trx_feedback_point tfp ON tf.id = tfp.trx_feedback_id
        WHERE tf.id = @fid
          AND tf.feedback_type = 'COMPLAINT'
          AND tfp.description IS NOT NULL
          AND btrim(tfp.description) <> ''
        ORDER BY tfp.created_date ASC;";

            var parts = (await conn.QueryAsync<string>(sqlComplaintDesc, new { fid = id }))
                        ?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();

            vm.Description = parts.Count > 0 ? string.Join("\n\n", parts) : "-";

            return View(vm);
        }

        private static string MapStatusText(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "-";
            switch (code.Trim().ToUpperInvariant())
            {
                case "IN_PROGRESS_SUBMIT": return "Proses Persetujuan PPN";
                case "APPROVE_SBM": return "Disetujui SBM";
                case "APPROVE_PPN": return "Disetujui PPN";
                case "APPROVE": return "Disetujui";
                case "REJECTED": return "Ditolak";
                default: return code;
            }
        }
    }
}
