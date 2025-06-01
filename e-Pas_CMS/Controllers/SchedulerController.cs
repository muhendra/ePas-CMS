using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using e_Pas_CMS.ViewModels;
using e_Pas_CMS.Data;
using Microsoft.EntityFrameworkCore;
using e_Pas_CMS.Models;
using System.Data;
using Dapper;

namespace e_Pas_CMS.Controllers
{
    [Route("Scheduler")]
    [Authorize]
    public class SchedulerController : Controller
    {
        private readonly EpasDbContext _context;

        public SchedulerController(EpasDbContext context)
        {
            _context = context;
        }

        public IActionResult Add()
        {
            return View();
        }

        // POST: /Scheduler/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Add(SchedulerViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // TODO: Simpan data scheduler ke database
            // contoh simpan:
            // _context.Schedulers.Add(new Scheduler { ... });

            TempData["Success"] = "Scheduler berhasil ditambahkan.";
            return RedirectToAction("Add");
        }

        [HttpGet("GetAuditorList")]
        public async Task<IActionResult> GetAuditorList(int page = 1, int pageSize = 10, string search = "")
        {
            var query = _context.app_users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();
                query = query.Where(x => x.name.ToLower().Contains(search));
            }

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var items = await query
                .OrderBy(x => x.name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    id = x.id,
                    text = x.name
                })
                .ToListAsync();

            return Json(new
            {
                currentPage = page,
                totalPages = totalPages,
                items
            });
        }


        [HttpGet("GetSpbuList")]
        public async Task<IActionResult> GetSpbuList(int page = 1, int pageSize = 10, string search = "")
        {
            var query = _context.spbus
                .Where(x => x.status == "ACTIVE");

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();

                query = query.Where(x =>
                    x.spbu_no.ToLower().Contains(search) ||
                    (x.address ?? "").ToLower().Contains(search) ||
                    (x.city_name ?? "").ToLower().Contains(search) ||
                    (x.province_name ?? "").ToLower().Contains(search)
                );
            }

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var items = await query
                .OrderBy(x => x.spbu_no)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.id,
                    x.spbu_no,
                    x.region,
                    x.province_name,
                    x.city_name,
                    x.address
                })
                .ToListAsync();

            return Json(new
            {
                currentPage = page,
                totalPages = totalPages,
                items = items
            });
        }

        [HttpPost("AddScheduler")]
        public async Task<IActionResult> AddScheduler(string AuditorId, string selectedSpbuIds, string TipeAudit, DateTime TanggalAudit)
        {
            if (string.IsNullOrEmpty(AuditorId) || string.IsNullOrEmpty(selectedSpbuIds) || string.IsNullOrEmpty(TipeAudit))
            {
                return BadRequest("Data tidak lengkap.");
            }

            var spbuIdList = selectedSpbuIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var userId = AuditorId;
            var currentUser = User.Identity?.Name ?? "system";
            var currentTime = DateTime.Now;

            foreach (var spbuId in spbuIdList)
            {
                string tid = "";
                string audit_level = "";

                // Tambahkan LINQ query ke database
                var existingAudit = await _context.trx_audits
                    .Where(x => x.spbu_id == spbuId)
                    .OrderByDescending(x => x.created_date)
                    .FirstOrDefaultAsync();

                if (existingAudit != null)
                {
                    tid = existingAudit.id;
                    audit_level = existingAudit.audit_level;
                }

                var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

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
                AND mqd.type = 'QUESTION'";

                var checklist = (await conn.QueryAsync<(decimal? weight, string score_input, decimal? score_x, bool? is_relaksasi)>(sql, new { id = tid }))
                    .ToList();

                decimal sumAF = 0, sumWeight = 0, sumX = 0;

                foreach (var item in checklist)
                {
                    decimal w = item.weight ?? 0;
                    string input = item.score_input?.Trim().ToUpperInvariant() ?? "";

                    if (input == "X")
                    {
                        sumX += w;
                        sumAF += item.score_x ?? 0;
                    }
                    else if (input == "F" && item.is_relaksasi == true)
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

                    sumWeight += w;
                }

                decimal finalScore = (sumWeight - sumX) > 0
                    ? (sumAF / (sumWeight - sumX)) * sumWeight
                    : 0m;

                var penaltyExcellentQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                WHERE 
                    tac.trx_audit_id = @id and
                    ((mqd.penalty_excellent_criteria = 'LT_1' and tac.score_input <> 'A') or
                    (mqd.penalty_excellent_criteria = 'EQ_0' and tac.score_input = 'F')) and
                    (mqd.is_relaksasi = false or mqd.is_relaksasi is null) and
                    mqd.is_penalty = true;";

                var penaltyGoodQuery = @"SELECT STRING_AGG(mqd.penalty_alert, ', ') AS penalty_alerts
                FROM trx_audit_checklist tac
                INNER JOIN master_questioner_detail mqd ON mqd.id = tac.master_questioner_detail_id
                WHERE tac.trx_audit_id = @id AND
                      tac.score_input = 'F' AND
                      mqd.is_penalty = true AND 
                      (mqd.is_relaksasi = false OR mqd.is_relaksasi IS NULL);";

                var penaltyExcellentResult = await conn.ExecuteScalarAsync<string>(penaltyExcellentQuery, new { id = tid });
                var penaltyGoodResult = await conn.ExecuteScalarAsync<string>(penaltyGoodQuery, new { id = tid });

                bool hasExcellentPenalty = !string.IsNullOrEmpty(penaltyExcellentResult);
                bool hasGoodPenalty = !string.IsNullOrEmpty(penaltyGoodResult);

                string goodStatus = (finalScore >= 75 && !hasGoodPenalty) ? "CERTIFIED" : "NOT CERTIFIED";
                string excellentStatus = (finalScore >= 80 && !hasExcellentPenalty) ? "CERTIFIED" : "NOT CERTIFIED";

                // === Ambil audit_next ===
                string auditNext = null;
                string levelspbu = null;

                var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = audit_level });

                if (auditFlow != null)
                {
                    string passedGood = auditFlow.passed_good;
                    string passedExcellent = auditFlow.passed_excellent;
                    string passedAuditLevel = auditFlow.passed_audit_level;
                    string failed_audit_level = auditFlow.failed_audit_level;

                    if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                    {
                        auditNext = passedAuditLevel;
                    }
                    else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                    {
                        auditNext = passedAuditLevel;
                    }
                    else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                    {
                        auditNext = failed_audit_level;
                    }
                    else if (goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                    {
                        auditNext = passedGood;
                    }
                    else if (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                    {
                        auditNext = passedExcellent;
                    }
                    else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && finalScore >= 75)
                    {
                        auditNext = passedAuditLevel;
                    }
                    else
                    {
                        auditNext = failed_audit_level;
                    }

                    var auditlevelClassSql = @"SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                    var auditlevelClass = await conn.QueryFirstOrDefaultAsync<dynamic>(auditlevelClassSql, new { level = auditNext });
                    levelspbu = auditlevelClass != null
                    ? (auditlevelClass.audit_level_class ?? "")
                    : "";

                }

                var trxAudit = new trx_audit
                {
                    id = Guid.NewGuid().ToString(),
                    report_prefix = null,
                    report_no = null,                          
                    spbu_id = spbuId,
                    app_user_id = userId,
                    master_questioner_intro_id = "7e3dca2d-2d99-4a8d-9fc0-9b80cb4c3a79",        
                    master_questioner_checklist_id = "16d4f8e1-360a-47b0-86b7-8ac55a1a6f75",     
                    audit_level = auditNext.ToString() ?? "IA",
                    audit_type = TipeAudit,
                    audit_schedule_date = DateOnly.FromDateTime(TanggalAudit),
                    audit_execution_time = null,
                    audit_media_upload = 0,
                    audit_media_total = 0,
                    audit_mom_intro = null,
                    audit_mom_final = null,
                    status = "NOT STARTED",
                    created_by = currentUser,
                    created_date = currentTime,
                    updated_by = currentUser,
                    updated_date = currentTime,
                    approval_by = null,
                    approval_date = null
                };

                _context.trx_audits.Add(trxAudit);
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Logging jika perlu
                Console.WriteLine($"Error saat menyimpan data audit: {ex.Message}");
                return StatusCode(500, "Terjadi kesalahan saat menyimpan data.");
            }

            TempData["Success"] = "Scheduler berhasil ditambahkan.";
            return RedirectToAction("Add");
        }


    }
}
