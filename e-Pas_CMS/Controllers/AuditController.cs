using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static NpgsqlTypes.NpgsqlTsQuery;

namespace e_Pas_CMS.Controllers
{
    [Authorize]
    public class AuditController : Controller
    {
        private readonly EpasDbContext _context;
        private const int DefaultPageSize = 10;
        private readonly ILogger<AuditController> _logger;

        public AuditController(EpasDbContext context, ILogger<AuditController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = DefaultPageSize, string searchTerm = "", string sortColumn = "TanggalAudit", string sortDirection = "desc")
        {
            try
            {
                var currentUser = User.Identity?.Name;
                bool isReadonlyUser = currentUser == "usermanagement1";

                // Ambil region user (jika ada)
                //var query = from a in _context.trx_audits
                //            join s in _context.spbus on a.spbu_id equals s.id
                //            join u in _context.app_users on a.app_user_id equals u.id
                //            join aur in _context.app_user_roles on s.region equals aur.region
                //            join au in _context.app_users on aur.app_user_id equals au.id
                //            where a.status == "UNDER_REVIEW"
                //               && au.username == currentUser
                //            orderby a.created_date descending
                //            select new
                //            {
                //                Audit = a,
                //                Spbu = s,
                //                AuditorName = u.name
                //            };

                var userRegion = await (from aur in _context.app_user_roles
                                        join au in _context.app_users on aur.app_user_id equals au.id
                                        where au.username == currentUser
                                        select aur.region)
                       .Distinct()
                       .Where(r => r != null)
                       .ToListAsync();

                var query = from a in _context.trx_audits
                            join s in _context.spbus on a.spbu_id equals s.id
                            join u in _context.app_users on a.app_user_id equals u.id into aud
                            from u in aud.DefaultIfEmpty()
                            where a.status == "UNDER_REVIEW"
                            select new
                            {
                                Audit = a,
                                Spbu = s,
                                AuditorName = u.name
                            };

                if (userRegion.Any())
                {
                    query = query.Where(x => userRegion.Contains(x.Spbu.region));
                }

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    searchTerm = searchTerm.ToLower();
                    query = query.Where(x =>
                        x.Spbu.spbu_no.ToLower().Contains(searchTerm) ||
                        (x.AuditorName != null && x.AuditorName.ToLower().Contains(searchTerm)) ||
                        x.Audit.status.ToLower().Contains(searchTerm) ||
                        (x.Spbu.address != null && x.Spbu.address.ToLower().Contains(searchTerm)) ||
                        (x.Spbu.province_name != null && x.Spbu.province_name.ToLower().Contains(searchTerm)) ||
                        (x.Spbu.city_name != null && x.Spbu.city_name.ToLower().Contains(searchTerm))
                    );
                }

                query = query.OrderBy(x => x.Audit.created_date);

                // Apply sorting
                query = sortColumn switch
                {
                    "Auditor" => sortDirection == "asc" ? query.OrderBy(q => q.AuditorName) : query.OrderByDescending(q => q.AuditorName),
                    "TanggalAudit" => sortDirection == "asc" ? query.OrderBy(q => q.Audit.updated_date) : query.OrderByDescending(q => q.Audit.updated_date),
                    "Status" => sortDirection == "asc" ? query.OrderBy(q => q.Audit.status) : query.OrderByDescending(q => q.Audit.status),
                    "Tahun" => sortDirection == "asc" ? query.OrderBy(q => q.Audit.created_date) : query.OrderByDescending(q => q.Audit.created_date),
                    "NoSpbu" => sortDirection == "asc" ? query.OrderBy(q => q.Spbu.spbu_no) : query.OrderByDescending(q => q.Spbu.spbu_no),
                    "Rayon" => sortDirection == "asc" ? query.OrderBy(q => q.Spbu.region) : query.OrderByDescending(q => q.Spbu.region),
                    "Kota" => sortDirection == "asc" ? query.OrderBy(q => q.Spbu.city_name) : query.OrderByDescending(q => q.Spbu.city_name),
                    "Alamat" => sortDirection == "asc" ? query.OrderBy(q => q.Spbu.address) : query.OrderByDescending(q => q.Spbu.address),
                    "Audit" => sortDirection == "asc" ? query.OrderBy(q => q.Audit.audit_level) : query.OrderByDescending(q => q.Audit.audit_level),
                    _ => query.OrderByDescending(q => q.Audit.updated_date)
                };

                var totalItems = await query.CountAsync();
                var items = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                using var conn = _context.Database.GetDbConnection();

                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                var result = new List<SpbuViewModel>();

                foreach (var item in items)
                {
                    var sql = @"
                SELECT mqd.weight, tac.score_input
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac 
                    ON tac.master_questioner_detail_id = mqd.id 
                    AND tac.trx_audit_id = @id
                WHERE mqd.master_questioner_id = (
                    SELECT master_questioner_checklist_id 
                    FROM trx_audit 
                    WHERE id = @id
                )
                AND mqd.type = 'QUESTION'";

                    var checklist = (await conn.QueryAsync<(decimal? weight, string score_input)>(sql, new { id = item.Audit.id })).ToList();

                    decimal totalScore = 0, maxScore = 0;

                    foreach (var q in checklist)
                    {
                        decimal w = q.weight ?? 0;
                        decimal v = q.score_input switch
                        {
                            "A" => 1.00m,
                            "B" => 0.80m,
                            "C" => 0.60m,
                            "D" => 0.40m,
                            "E" => 0.20m,
                            "F" => 0.00m,
                            _ => 0.00m
                        };
                        totalScore += v * w;
                        maxScore += w;
                    }

                    decimal finalScore = maxScore > 0 ? totalScore / maxScore * 100 : 0m;

                    var penaltyQuery = @"
                SELECT string_agg(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac 
                INNER JOIN master_questioner_detail mqd 
                    ON mqd.id = tac.master_questioner_detail_id 
                WHERE tac.trx_audit_id = @id 
                  AND tac.score_input = 'F' 
                  AND mqd.is_penalty = true;";

                    var penaltyResult = await conn.ExecuteScalarAsync<string>(penaltyQuery, new { id = item.Audit.id });

                    bool hasPenalty = !string.IsNullOrEmpty(penaltyResult);

                    string goodStatus = "NOT CERTIFIED";

                    string excellentStatus = "NO CERTIFIED";

                    if (finalScore >= 80)
                    {
                        goodStatus = "CERTIFIED";

                        if (!hasPenalty)
                        {
                            excellentStatus = "EXCELLENT";
                        }
                    }

                    result.Add(new SpbuViewModel
                    {
                        Id = item.Audit.id,
                        NoSpbu = item.Spbu.spbu_no,
                        Rayon = item.Spbu.region,
                        Alamat = item.Spbu.address,
                        TipeSpbu = item.Spbu.type,
                        Tahun = item.Audit.created_date.ToString("yyyy"),
                        Audit = item.Audit.audit_level,
                        Score = finalScore,
                        Good = goodStatus,
                        Excelent = excellentStatus,
                        Provinsi = item.Spbu.province_name,
                        Kota = item.Spbu.city_name,
                        NamaAuditor = item.AuditorName,
                        Report = item.Audit.report_no,
                        TanggalSubmit = (item.Audit.audit_execution_time == null || item.Audit.audit_execution_time.Value == DateTime.MinValue)
                            ? item.Audit.updated_date.Value
                            : item.Audit.audit_execution_time.Value,
                        Status = item.Audit.status,
                        Komplain = item.Audit.status == "FAIL" ? "ADA" : "TIDAK ADA",
                        Banding = item.Audit.audit_level == "Re-Audit" ? "ADA" : "TIDAK ADA",
                        Type = item.Audit.audit_type
                    });
                }

                // Sorting untuk hasil final list

                result = sortColumn switch
                {
                    "Score" => sortDirection == "asc" ? result.OrderBy(r => r.Score).ToList() : result.OrderByDescending(r => r.Score).ToList(),
                    "Good" => sortDirection == "asc" ? result.OrderBy(r => r.Good).ToList() : result.OrderByDescending(r => r.Good).ToList(),
                    "Excellent" => sortDirection == "asc" ? result.OrderBy(r => r.Excelent).ToList() : result.OrderByDescending(r => r.Excelent).ToList(),
                    "Komplain" => sortDirection == "asc" ? result.OrderBy(r => r.Komplain).ToList() : result.OrderByDescending(r => r.Komplain).ToList(),
                    "Banding" => sortDirection == "asc" ? result.OrderBy(r => r.Banding).ToList() : result.OrderByDescending(r => r.Banding).ToList(),
                    _ => result
                };

                var paginationModel = new PaginationModel<SpbuViewModel>
                {
                    Items = result,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalItems = totalItems
                };
                ViewBag.SearchTerm = searchTerm;
                ViewBag.SortColumn = sortColumn;
                ViewBag.SortDirection = sortDirection;

                return View(paginationModel);
            }

            catch (Exception ex)
            {
                TempData["Error"] = "Terjadi kesalahan saat memuat data. Silakan coba lagi.";
                return View(new PaginationModel<SpbuViewModel>
                {
                    Items = new List<SpbuViewModel>(),
                    PageNumber = 1,
                    PageSize = DefaultPageSize,
                    TotalItems = 0
                });
            }
        }

        //public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = DefaultPageSize, string searchTerm = "")
        //{
        //    try
        //    {
        //        var currentUser = User.Identity?.Name;
        //        bool isReadonlyUser = currentUser == "usermanagement1";

        //        // Ambil region user (jika ada)
        //        var query = from a in _context.trx_audits
        //                    join s in _context.spbus on a.spbu_id equals s.id
        //                    join u in _context.app_users on a.app_user_id equals u.id
        //                    join aur in _context.app_user_roles on s.region equals aur.region
        //                    join au in _context.app_users on aur.app_user_id equals au.id
        //                    where a.status == "UNDER_REVIEW"
        //                       && au.username == currentUser
        //                    orderby a.created_date descending
        //                    select new
        //                    {
        //                        Audit = a,
        //                        Spbu = s,
        //                        AuditorName = u.name
        //                    };


        //        if (!string.IsNullOrWhiteSpace(searchTerm))
        //        {
        //            searchTerm = searchTerm.ToLower();
        //            query = query.Where(x =>
        //                x.Spbu.spbu_no.ToLower().Contains(searchTerm) ||
        //                (x.AuditorName != null && x.AuditorName.ToLower().Contains(searchTerm)) ||
        //                x.Audit.status.ToLower().Contains(searchTerm) ||
        //                (x.Spbu.address != null && x.Spbu.address.ToLower().Contains(searchTerm)) ||
        //                (x.Spbu.province_name != null && x.Spbu.province_name.ToLower().Contains(searchTerm)) ||
        //                (x.Spbu.city_name != null && x.Spbu.city_name.ToLower().Contains(searchTerm))
        //            );
        //        }

        //        query = query.OrderBy(x => x.Audit.created_date);

        //        var totalItems = await query.CountAsync();

        //        var items = await query
        //            .Skip((pageNumber - 1) * pageSize)
        //            .Take(pageSize)
        //            .ToListAsync();

        //        using var conn = _context.Database.GetDbConnection();
        //        if (conn.State != ConnectionState.Open)
        //            await conn.OpenAsync();

        //        var result = new List<SpbuViewModel>();

        //        foreach (var item in items)
        //        {
        //            var sql = @"
        //                SELECT mqd.weight, tac.score_input
        //                FROM master_questioner_detail mqd
        //                LEFT JOIN trx_audit_checklist tac 
        //                    ON tac.master_questioner_detail_id = mqd.id 
        //                    AND tac.trx_audit_id = @id
        //                WHERE mqd.master_questioner_id = (
        //                    SELECT master_questioner_checklist_id 
        //                    FROM trx_audit 
        //                    WHERE id = @id
        //                )
        //                AND mqd.type = 'QUESTION'";

        //            var checklist = (await conn.QueryAsync<(decimal? weight, string score_input)>(sql, new { id = item.Audit.id }))
        //                               .ToList();

        //            decimal totalScore = 0, maxScore = 0;
        //            foreach (var q in checklist)
        //            {
        //                decimal w = q.weight ?? 0;
        //                decimal v = q.score_input switch
        //                {
        //                    "A" => 1.00m,
        //                    "B" => 0.80m,
        //                    "C" => 0.60m,
        //                    "D" => 0.40m,
        //                    "E" => 0.20m,
        //                    "F" => 0.00m,
        //                    _ => 0.00m
        //                };
        //                totalScore += v * w;
        //                maxScore += w;
        //            }

        //            decimal finalScore = maxScore > 0
        //                ? totalScore / maxScore * 100
        //                : 0m;

        //            result.Add(new SpbuViewModel
        //            {
        //                Id = item.Audit.id,
        //                NoSpbu = item.Spbu.spbu_no,
        //                Rayon = item.Spbu.region,
        //                Alamat = item.Spbu.address,
        //                TipeSpbu = item.Spbu.type,
        //                Tahun = item.Audit.created_date.ToString("yyyy"),
        //                Audit = "DAE",
        //                Score = finalScore,
        //                Good = "certified",
        //                Excelent = "certified",
        //                Provinsi = item.Spbu.province_name,
        //                Kota = item.Spbu.city_name,
        //                NamaAuditor = item.AuditorName,
        //                Report = item.Audit.report_no,
        //                TanggalSubmit = (item.Audit.audit_execution_time == null
        //                               || item.Audit.audit_execution_time.Value == DateTime.MinValue)
        //                              ? item.Audit.updated_date.Value
        //                              : item.Audit.audit_execution_time.Value,
        //                Status = item.Audit.status,
        //                Komplain = item.Audit.status == "FAIL" ? "ADA" : "Tidak Ada",
        //                Banding = item.Audit.audit_level == "Re-Audit" ? "ADA" : "Tidak Ada",
        //                Type = item.Audit.audit_type
        //            });
        //        }

        //        var paginationModel = new PaginationModel<SpbuViewModel>
        //        {
        //            Items = result,
        //            PageNumber = pageNumber,
        //            PageSize = pageSize,
        //            TotalItems = totalItems
        //        };

        //        ViewBag.SearchTerm = searchTerm;
        //        return View(paginationModel);
        //    }
        //    catch (Exception ex)
        //    {
        //        // Log the exception here
        //        TempData["Error"] = "Terjadi kesalahan saat memuat data. Silakan coba lagi.";
        //        return View(new PaginationModel<SpbuViewModel>
        //        {
        //            Items = new List<SpbuViewModel>(),
        //            PageNumber = 1,
        //            PageSize = DefaultPageSize,
        //            TotalItems = 0
        //        });
        //    }
        //}

        [HttpGet("Audit/Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            // Pull the main audit header
            var audit = await (
                from ta in _context.trx_audits
                join au in _context.app_users on ta.app_user_id equals au.id
                join s in _context.spbus on ta.spbu_id equals s.id
                where ta.id == id
                select new DetailAuditViewModel
                {
                    ReportNo = ta.report_no,
                    NamaAuditor = au.name,
                    TanggalSubmit = (ta.audit_execution_time == null
                                    || ta.audit_execution_time.Value == DateTime.MinValue)
                                    ? ta.updated_date.Value
                                    : ta.audit_execution_time.Value,
                    Status = ta.status,
                    SpbuNo = s.spbu_no,
                    Provinsi = s.province_name,
                    Kota = s.city_name,
                    Alamat = s.address,
                    Notes = ta.audit_mom_final,
                    AuditType = ta.audit_type
                }
            ).FirstOrDefaultAsync();

            if (audit == null)
                return NotFound();

            // Load QUESTION-type media notes
            var mediaQuestionSql = @"
                SELECT media_path, media_type
                FROM trx_audit_media
                WHERE trx_audit_id = @id
                  AND type = 'QUESTION'";
            var mq = (await conn.QueryAsync<(string media_path, string media_type)>(mediaQuestionSql, new { id }))
                       .ToList();
            if ((audit.AuditType == "Mystery Audit" || audit.AuditType == "Mystery Guest") && mq.Any())
            {
                audit.MediaNotes = mq
                    .Select(x => new MediaItem
                    {
                        MediaType = x.media_type,
                        MediaPath = "https://epas.zarata.co.id" + x.media_path
                    })
                    .ToList();
            }

            // Load the flat checklist
            var checklistSql = @"
                SELECT
                  mqd.id,
                  mqd.title,
                  mqd.description,
                  mqd.parent_id,
                  mqd.type,
                  mqd.weight,
                  mqd.score_option,
                  tac.score_input,
                  tac.score_af,
                  tac.score_x,
                  tac.comment,
                  mqd.is_penalty,
                  mqd.order_no,
                  mqd.is_relaksasi,
                  (
                    SELECT string_agg(mqd2.penalty_alert, ', ')
                    FROM trx_audit_checklist tac2
                    INNER JOIN master_questioner_detail mqd2 ON mqd2.id = tac2.master_questioner_detail_id
                    WHERE 
                      tac2.trx_audit_id = @id
                      AND tac2.score_input = 'F'
                      AND mqd2.is_penalty = true
                  ) AS penalty_alert
                FROM master_questioner_detail mqd
                LEFT JOIN trx_audit_checklist tac
                  ON tac.master_questioner_detail_id = mqd.id
                 AND tac.trx_audit_id = @id
                WHERE mqd.master_questioner_id = (
                  SELECT master_questioner_checklist_id
                  FROM trx_audit
                  WHERE id = @id
                )
                ORDER BY mqd.order_no;";

            var checklistData = (await conn.QueryAsync<ChecklistFlatItem>(checklistSql, new { id }))
                                    .ToList();

            // Load any media per-node
            var mediaSql = @"
                SELECT master_questioner_detail_id, media_type, media_path
                FROM trx_audit_media
                WHERE trx_audit_id = @id
                  AND master_questioner_detail_id IS NOT NULL";
            var mediaList = (await conn.QueryAsync<(string master_questioner_detail_id, string media_type, string media_path)>(mediaSql, new { id }))
                .GroupBy(x => x.master_questioner_detail_id)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(m => new MediaItem
                    {
                        MediaType = m.media_type,
                        MediaPath = "https://epas.zarata.co.id" + m.media_path
                    }).ToList()
                );

            // Build tree
            audit.Elements = BuildHierarchy(checklistData, mediaList);

            var validAF = new Dictionary<string, decimal>
            {
                ["A"] = 1.00m,
                ["B"] = 0.80m,
                ["C"] = 0.60m,
                ["D"] = 0.40m,
                ["E"] = 0.20m,
                ["F"] = 0.00m
            };

            decimal sumAF = 0m;
            decimal sumX = 0m;
            decimal sumWeight = 0m;

            foreach (var q in checklistData.Where(x => x.type == "QUESTION"))
            {
                string input = q.score_input?.Trim();
                decimal weight = q.weight ?? 0m;

                // Expand A-F into [A,B,C,D,E,F], then ensure X is in the list
                var allowed = (q.score_option ?? "")
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .SelectMany(opt =>
                        opt == "A-F"
                          ? new[] { "A", "B", "C", "D", "E", "F" }
                          : new[] { opt.Trim() })
                    .Concat(new[] { "X" })
                    .Select(x => x.Trim())
                    .Distinct()
                    .ToList();

                // skip if empty or not in allowed
                if (string.IsNullOrWhiteSpace(input) || !allowed.Contains(input))
                    continue;

                if (input == "X")
                {
                    sumX += weight;
                }
                else
                {
                    // Override input jadi F kalau relaksasi
                    if (q.is_relaksasi == true)
                        input = "F";

                    if (validAF.TryGetValue(input, out var af))
                    {
                        sumAF += af * weight;
                        sumWeight += weight;
                    }
                }
            }

            audit.TotalScore = sumAF;
            audit.MaxScore = sumWeight;
            audit.FinalScore = (sumWeight - sumX) > 0
                ? (sumAF / (sumWeight - sumX)) * 100m
                : 0m;

            var qqSql = @"
                SELECT nozzle_number AS NozzleNumber,
                       du_make  AS DuMake,
                       du_serial_no AS DuSerialNo,
                       product  AS Product,
                       mode     AS Mode,
                       quantity_variation_with_measure AS QuantityVariationWithMeasure,
                       quantity_variation_in_percentage  AS QuantityVariationInPercentage,
                       observed_density      AS ObservedDensity,
                       observed_temp         AS ObservedTemp,
                       observed_density_15_degree   AS ObservedDensity15Degree,
                       reference_density_15_degree  AS ReferenceDensity15Degree,
                       tank_number  AS TankNumber,
                       density_variation AS DensityVariation
                FROM trx_audit_qq
                WHERE trx_audit_id = @id";
            audit.QqChecks = (await conn.QueryAsync<AuditQqCheckItem>(qqSql, new { id })).ToList();

            var finalMediaSql = @"
                SELECT media_type, media_path
                FROM trx_audit_media
                WHERE trx_audit_id = @id
                  AND type = 'FINAL'";
            audit.FinalDocuments = (await conn.QueryAsync<(string media_type, string media_path)>(finalMediaSql, new { id }))
                .Select(x => new MediaItem
                {
                    MediaType = x.media_type,
                    MediaPath = "https://epas.zarata.co.id" + x.media_path
                })
                .ToList();

            ViewBag.AuditId = id;
            return View(audit);
        }

        [HttpPost("audit/approve/{id}")]
        public async Task<IActionResult> Approve(string id)
        {
            var audit = await _context.trx_audits.FirstOrDefaultAsync(x => x.id == id);
            if (audit == null)
                return NotFound();

            audit.status = "VERIFIED";
            _context.trx_audits.Update(audit);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Laporan audit telah disetujui.";
            return RedirectToAction("Detail", new { id });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateScore([FromBody] UpdateScoreRequest request)
        {
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

                var sql = @"
                    UPDATE trx_audit_checklist
                    SET score_input = @score
                    WHERE master_questioner_detail_id = @nodeId
                      AND trx_audit_id = @auditId";

                var affected = await conn.ExecuteAsync(sql, new
                {
                score = request.Score,
                nodeId = request.NodeId,
                auditId = request.AuditId
                });

            return affected > 0 ? Ok() : BadRequest("Tidak berhasil update");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBeritaAcaraText(string id, string notes)
        {
            var audit = await _context.trx_audits.FirstOrDefaultAsync(x => x.id == id);
            if (audit == null) return NotFound();

            audit.audit_mom_final = notes;
            audit.updated_by = User.Identity?.Name;
            audit.updated_date = DateTime.Now;

            await _context.SaveChangesAsync();
            TempData["Success"] = "Teks Berita Acara berhasil diperbarui";
            return RedirectToAction("Detail", new { id });
        }

        [HttpPost("Audit/UploadBeritaAcaraMedia")]
        public async Task<IActionResult> UploadBeritaAcaraMedia(IFormFile file, string auditId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File tidak ditemukan atau kosong.");

            var generatedNodeId = Guid.NewGuid().ToString();
            var uploadsPath = Path.Combine("/var/www/epas-api", "wwwroot", "uploads", auditId, generatedNodeId);

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
INSERT INTO trx_audit_media 
    (id, trx_audit_id, master_questioner_detail_id, media_type, media_path, type, status, created_date, created_by, updated_date, updated_by)
VALUES 
    (uuid_generate_v4(), @auditId, null, @mediaType, @mediaPath, 'FINAL', 'ACTIVE', NOW(), @createdBy, NOW(), @createdBy)";


            await conn.ExecuteAsync(insertSql, new
            {
                auditId,
                mediaType = Path.GetExtension(fileName).Trim('.').ToLower(),
                mediaPath = $"/uploads/{auditId}/{generatedNodeId}/{fileName}",
                createdBy = User.Identity?.Name ?? "anonymous"
            });

            return RedirectToAction("Detail", new { id = auditId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateComment([FromBody] UpdateCommentRequest request)
        {
            if (string.IsNullOrEmpty(request.AuditId) || string.IsNullOrEmpty(request.NodeId))
                return BadRequest("Data tidak lengkap");

            var entity = await _context.trx_audit_checklists
                .FirstOrDefaultAsync(x => x.trx_audit_id == request.AuditId && x.master_questioner_detail_id == request.NodeId);

            if (entity == null)
                return NotFound("Data tidak ditemukan");

            entity.comment = request.Comment;
            entity.updated_by = User.Identity?.Name;
            entity.updated_date = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok();
        }

        private List<AuditChecklistNode> BuildHierarchy(
            List<ChecklistFlatItem> flatList,
            Dictionary<string, List<MediaItem>> mediaList)
        {
            var lookup = flatList.ToLookup(x => x.parent_id);

            List<AuditChecklistNode> BuildChildren(string parentId) =>
                lookup[parentId]
                .OrderBy(x => x.order_no)
                .Select(item => new AuditChecklistNode
                {
                    Id = item.id,
                    Title = item.title,
                    Description = item.description,
                    Type = item.type,
                    Weight = item.weight,
                    ScoreOption = item.score_option,
                    ScoreInput = item.score_input,
                    ScoreAF = item.score_af,
                    ScoreX = item.score_x,
                    Comment = item.comment,
                    IsPenalty = item.is_penalty,
                    PenaltyAlert = item.penalty_alert,
                    IsRelaksasi = item.is_relaksasi,
                    MediaItems = mediaList.ContainsKey(item.id)
                                  ? mediaList[item.id]
                                  : new List<MediaItem>(),
                    Children = BuildChildren(item.id)
                })
                .ToList();

            return BuildChildren(
                flatList.Any(x => x.parent_id == null) ? null : ""
            );
        }

        [HttpPost("Audit/UploadDocument")]
        public async Task<IActionResult> UploadDocument(IFormFile file, string nodeId, string auditId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File tidak ditemukan atau kosong.");

            // Direktori penyimpanan
            var uploadsPath = Path.Combine("/var/www/epas-api", "wwwroot", "uploads", auditId, nodeId);
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
INSERT INTO trx_audit_media 
    (id, trx_audit_id, master_questioner_detail_id, media_type, media_path, type, status, created_date, created_by, updated_date, updated_by)
VALUES 
    (uuid_generate_v4(), @auditId, @nodeId, @mediaType, @mediaPath, 'QUESTION', 'ACTIVE', NOW(), @createdBy, NOW(), @createdBy)";


            await conn.ExecuteAsync(insertSql, new
            {
                auditId,
                nodeId,
                mediaType = Path.GetExtension(fileName).Trim('.').ToLower(),
                mediaPath = $"/uploads/{auditId}/{nodeId}/{fileName}",
                createdBy = User.Identity?.Name ?? "anonymous"
            });

            return RedirectToAction("Detail", new { id = auditId });
        }

        [HttpGet("Audit/ViewDocument/{auditId}/{id}")]
        public async Task<IActionResult> ViewDocument(string auditId, string id)
        {
            using var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            var querySql = @"
        SELECT media_path, media_type
        FROM trx_audit_media
        WHERE trx_audit_id = @auditId AND master_questioner_detail_id = @id
        ORDER BY created_date DESC
        LIMIT 1";

            var media = await conn.QueryFirstOrDefaultAsync<(string media_path, string media_type)>(querySql, new { auditId, id });

            if (string.IsNullOrEmpty(media.media_path))
                return NotFound("Dokumen tidak ditemukan.");

            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", media.media_path.TrimStart('/'));

            if (!System.IO.File.Exists(fullPath))
                return NotFound("File fisik tidak ditemukan di server.");

            var mimeType = media.media_type?.ToLower() switch
            {
                "pdf" => "application/pdf",
                "jpg" or "jpeg" => "image/jpeg",
                "png" => "image/png",
                _ => "application/octet-stream"
            };

            return PhysicalFile(fullPath, mimeType);
        }

        [HttpGet]
        public IActionResult GetLibraryMedia(int page = 1, int pageSize = 10, string search = "")
        {
            //var libraryDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "library");

            //var libraryDir = "/var/www/epas-api/uploads/library";

            var libraryDir = Path.Combine("/var/www/epas-api", "wwwroot", "uploads", "library");

            _logger.LogInformation("Gallery requested. Page: {Page}, PageSize: {PageSize}, Search: {Search}", page, pageSize, search);

            if (!Directory.Exists(libraryDir))
            {
                _logger.LogWarning("Library directory not found: {Dir}", libraryDir);
                return Json(new { total = 0, data = new List<object>() });
            }

            var allFiles = Directory.GetFiles(libraryDir)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => System.IO.File.GetCreationTime(f));

            // Apply search filter if search term is provided
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();
                allFiles = allFiles.Where(f => 
                    Path.GetFileName(f).ToLower().Contains(search) ||
                    Path.GetFileNameWithoutExtension(f).ToLower().Contains(search)
                ).OrderByDescending(f => System.IO.File.GetCreationTime(f));
            }

            var total = allFiles.Count();
            _logger.LogInformation("Total media files found: {Total}", total);

            var pagedFiles = allFiles
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new
                {
                    MediaType = f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "VIDEO" : "IMAGE",
                    MediaPath = $"/uploads/library/{Path.GetFileName(f)}"
                })
                .ToList();

            _logger.LogInformation("Returning {Count} media files for page {Page}", pagedFiles.Count, page);

            return Json(new { total, data = pagedFiles });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMediaPath([FromBody] UpdateMediaPathRequest request)
        {
            if (string.IsNullOrEmpty(request.NodeId) || string.IsNullOrEmpty(request.MediaPath))
            {
                _logger.LogWarning("UpdateMediaPath: Invalid request - NodeId or MediaPath is empty");
                return BadRequest("Data tidak lengkap");
            }

            if (request.AuditId.Contains("..") || request.NodeId.Contains(".."))
            {
                _logger.LogWarning("UpdateMediaPath: Invalid audit ID or node ID - contains '..'");
                return BadRequest("Invalid audit ID or node ID");
            }

            _logger.LogInformation("UpdateMediaPath: Searching for media entity with AuditId: {AuditId}, NodeId: {NodeId}", request.AuditId, request.NodeId);
            var entity = await _context.trx_audit_media
                .FirstOrDefaultAsync(x => x.trx_audit_id == request.AuditId && x.master_questioner_detail_id == request.NodeId);

            if (entity == null)
            {
                _logger.LogWarning("UpdateMediaPath: Media entity not found for AuditId: {AuditId}, NodeId: {NodeId}", request.AuditId, request.NodeId);
                return NotFound("Data tidak ditemukan");
            }

            try
            {
                var fileName = Path.GetFileName(request.MediaPath);
                _logger.LogInformation("UpdateMediaPath: Processing file: {FileName}", fileName);
                
                var destinationDir = Path.Combine("/var/www/epas-api", "wwwroot", "uploads", request.AuditId, request.NodeId);
                _logger.LogInformation("UpdateMediaPath: Creating destination directory: {DestinationDir}", destinationDir);
                Directory.CreateDirectory(destinationDir);
                
                // var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", request.MediaPath.TrimStart('/'));
                var sourcePath = Path.Combine("/var/www/epas-api", "wwwroot", "uploads", "library", fileName);
                var destinationPath = Path.Combine(destinationDir, fileName);
                _logger.LogInformation("UpdateMediaPath: Source path: {SourcePath}, Destination path: {DestinationPath}", sourcePath, destinationPath);

                if (!System.IO.File.Exists(sourcePath))
                {
                    _logger.LogWarning("UpdateMediaPath: Source file not found: {SourcePath}", sourcePath);
                    return BadRequest("Source file not found");
                }

                if (System.IO.File.Exists(destinationPath))
                {
                    _logger.LogInformation("UpdateMediaPath: Destination file exists, computing hashes for comparison");
                    var sourceHash = ComputeFileHash(sourcePath);
                    var destHash = ComputeFileHash(destinationPath);
                    
                    if (sourceHash == destHash)
                    {
                        _logger.LogInformation("UpdateMediaPath: File already exists and is identical, skipping copy");
                        return Ok();
                    }
                    _logger.LogInformation("UpdateMediaPath: File exists but is different, proceeding with copy");
                }

                System.IO.File.Copy(sourcePath, destinationPath, true);
                _logger.LogInformation("UpdateMediaPath: File copied successfully from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);

                var newMediaPath = $"/uploads/{request.AuditId}/{request.NodeId}/{fileName}";
                _logger.LogInformation("UpdateMediaPath: Updating entity with new media path: {NewMediaPath}", newMediaPath);
                entity.media_path = newMediaPath;
                entity.media_type = request.MediaType;
                entity.updated_by = User.Identity?.Name;
                entity.updated_date = DateTime.Now;

                await _context.SaveChangesAsync();
                _logger.LogInformation("UpdateMediaPath: Successfully updated media entity in database");
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying file for audit {AuditId}, node {NodeId}", request.AuditId, request.NodeId);
                return BadRequest("Error copying file: " + ex.Message);
            }
        }

        private string ComputeFileHash(string filePath)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = System.IO.File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

    }
}