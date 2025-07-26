﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using e_Pas_CMS.ViewModels;
using e_Pas_CMS.Data;
using Microsoft.EntityFrameworkCore;
using e_Pas_CMS.Models;
using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc.Rendering;

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

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, string searchTerm = "")
        {
            var query = _context.trx_audits
                .Include(a => a.app_user)
                .Include(a => a.spbu)
                .Where(a => a.status != "DELETED");

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.ToLower();
                query = query.Where(a =>
                    a.app_user.name.ToLower().Contains(searchTerm) ||
                    a.spbu.spbu_no.ToLower().Contains(searchTerm));
            }

            query = query.OrderByDescending(a => a.created_date);

            var totalItems = await query.CountAsync();

            var rawItems = await query
                .OrderByDescending(x => x.audit_schedule_date)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = rawItems.Select(a => new SchedulerItemViewModel
            {
                Id = a.id,
                Status = MapStatus(a.status),
                AppUserName = a.app_user.name,
                AuditDate = (a.audit_execution_time == null || a.audit_execution_time.Value == DateTime.MinValue) ? a.audit_schedule_date.HasValue ? a.audit_schedule_date.Value.ToDateTime(new TimeOnly()) : DateTime.MinValue : a.audit_execution_time.Value,
                AuditType = a.audit_type,
                AuditLevel = a.audit_level,
                SpbuNo = a.spbu.spbu_no,
                ReportNo = a.report_no
            }).ToList();

            var vm = new SchedulerIndexViewModel
            {
                Items = items,
                CurrentPage = pageNumber,
                TotalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                PageSize = pageSize,
                SearchTerm = searchTerm
            };

            return View(vm);
        }

        // Helper method to map status
        private string MapStatus(string status) => status switch
        {
            "NOT_STARTED" => "Belum Dimulai",
            "IN_PROGRESS_INPUT" => "Sedang Berlangsung",
            "IN_PROGRESS_SUBMIT" => "Sedang Berlangsung",
            "UNDER_REVIEW" => "Sedang Ditinjau",
            "VERIFIED" => "Terverifikasi",
            _ => status
        };

        [HttpGet("Add")]
        public IActionResult Add()
        {
            return View();
        }

        [HttpPost("Add")]
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

        [HttpGet("Edit/{id}")]
        public ActionResult Edit(string id)
        {
            var audit = _context.trx_audits.Find(id);
            if (audit == null) return HttpNotFound();

            // Ambil audit_level unik dari master_audit_flow
            var auditLevels = _context.master_audit_flows
                .Select(m => m.audit_level)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var viewModel = new AuditEditViewModel
            {
                Id = audit.id,
                SpbuId = audit.spbu_id,
                AppUserId = audit.app_user_id,
                AuditLevel = audit.audit_level,
                AuditType = audit.audit_type,
                AuditScheduleDate = audit.audit_schedule_date,
                Status = MapStatus(audit.status),
                SpbuList = _context.spbus
                    .Select(s => new SelectListItem { Value = s.id, Text = s.spbu_no })
                    .ToList(),
                UserList = _context.app_users
                    .Select(u => new SelectListItem { Value = u.id, Text = u.name })
                    .ToList(),
                AuditLevelList = auditLevels
                    .Select(a => new SelectListItem
                    {
                        Value = a,
                        Text = a,
                        Selected = (a == audit.audit_level)
                    }).ToList()
            };

            return View(viewModel);
        }

        [HttpPost("Edit/{id}")]
        public ActionResult Edit(AuditEditViewModel model)
        {
            var audit = _context.trx_audits.FirstOrDefault(a => a.id == model.Id);
            if (audit == null)
                return NotFound();

            // Update field dari form
            audit.spbu_id = model.SpbuId;
            audit.app_user_id = model.AppUserId;
            audit.audit_level = model.AuditLevel;
            audit.audit_type = model.AuditType;
            audit.audit_schedule_date = model.AuditScheduleDate;
            audit.audit_mom_intro = model.AuditMomIntro;
            audit.audit_mom_final = model.AuditMomFinal;
            audit.status = audit.status;

            // Validasi berdasarkan tipe audit
            if (model.AuditType == "Basic Operational")
            {
                audit.master_questioner_intro_id = null;
                audit.master_questioner_checklist_id = "4b295bf0-9d29-4a56-9004-4b96ab656257";
            }
            else
            {
                audit.master_questioner_intro_id = "7e3dca2d-2d99-4a8d-9fc0-9b80cb4c3a79";
                audit.master_questioner_checklist_id = "16d4f8e1-360a-47b0-86b7-8ac55a1a6f75";
            }

            // Metadata update
            audit.updated_date = DateTime.Now;
            audit.updated_by = User.Identity.Name;

            _context.SaveChanges();

            // Update audit_next pada SPBU
            var spbu = _context.spbus.FirstOrDefault(s => s.id == model.SpbuId);
            if (spbu != null)
            {
                spbu.audit_next = model.AuditLevel;
                spbu.updated_by = User.Identity.Name;
                spbu.updated_date = DateTime.Now;
            }

            _context.SaveChanges();

            TempData["Success"] = "Data berhasil diperbarui.";
            return RedirectToAction("Index");
        }

        private ActionResult HttpNotFound()
        {
            throw new NotImplementedException();
        }

        [HttpGet("Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            var data = await _context.trx_audits
                .Include(a => a.spbu)
                .Include(a => a.app_user)
                .FirstOrDefaultAsync(a => a.id == id);

            if (data == null)
                return NotFound();

            var vm = new SchedulerDetailViewModel
            {
                Id = data.id,
                SpbuNo = data.spbu.spbu_no,
                SpbuAddress = data.spbu.address,
                AppUserName = data.app_user?.name,
                AuditScheduleDate = data.audit_schedule_date?.ToDateTime(new TimeOnly()),
                AuditType = data.audit_type,
                AuditLevel = data.audit_level,
                Status = MapStatus(data.status),
                AuditMomIntro = data.audit_mom_intro,
                AuditMomFinal = data.audit_mom_final
            };

            return View(vm);
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
            
            string nextauditsspbu = "";
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

                var existingSPBU = await _context.spbus
                    .Where(x => x.id == spbuId)
                    .OrderByDescending(x => x.created_date)
                    .FirstOrDefaultAsync();

                if (existingAudit != null)
                {
                    tid = existingAudit.id;
                    audit_level = existingAudit.audit_level;
                }

                if (existingSPBU != null)
                {
                    nextauditsspbu = !string.IsNullOrEmpty(existingSPBU.audit_next)
                        ? existingSPBU.audit_next
                        : existingSPBU.audit_current;
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
                string auditNext = nextauditsspbu;
                string levelspbu = null;

                var auditFlowSql = @"SELECT * FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                var auditFlow = await conn.QueryFirstOrDefaultAsync<dynamic>(auditFlowSql, new { level = audit_level });

                var auditNextSql = @"SELECT * FROM spbu WHERE id = @id LIMIT 1;";
                var auditNextRes = await conn.QueryFirstOrDefaultAsync<dynamic>(auditNextSql, new { id = spbuId });

                //if (auditFlow != null)
                //{
                //    string passedGood = auditFlow.passed_good;
                //    string passedExcellent = auditFlow.passed_excellent;
                //    string passedAuditLevel = auditFlow.passed_audit_level;
                //    string failed_audit_level = auditFlow.failed_audit_level;

                //    if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                //    {
                //        auditNext = passedAuditLevel;
                //    }
                //    else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                //    {
                //        auditNext = passedAuditLevel;
                //    }
                //    else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && goodStatus == "NOT CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                //    {
                //        auditNext = failed_audit_level;
                //    }
                //    else if (goodStatus == "CERTIFIED" && excellentStatus == "NOT CERTIFIED")
                //    {
                //        auditNext = passedGood;
                //    }
                //    else if (goodStatus == "CERTIFIED" && excellentStatus == "CERTIFIED")
                //    {
                //        auditNext = passedExcellent;
                //    }
                //    else if (string.IsNullOrWhiteSpace(passedGood) && string.IsNullOrWhiteSpace(passedExcellent) && finalScore >= 75)
                //    {
                //        auditNext = passedAuditLevel;
                //    }
                //    else
                //    {
                //        auditNext = failed_audit_level;
                //    }
                //}

                auditNext = auditNextRes.audit_next;

                var auditlevelClassSql = @"SELECT audit_level_class FROM master_audit_flow WHERE audit_level = @level LIMIT 1;";
                var auditlevelClass = await conn.QueryFirstOrDefaultAsync<dynamic>(auditlevelClassSql, new { level = auditNext });
                levelspbu = auditlevelClass != null
                ? (auditlevelClass.audit_level_class ?? "")
                : "";

                bool isBasicOperational = TipeAudit == "Basic Operational";

                string checklistId = "";
                string? introId = null;

                var conn2 = _context.Database.GetDbConnection();
                if (conn2.State != ConnectionState.Open)
                    await conn2.OpenAsync();

                if (isBasicOperational)
                {
                    var sqlChecklist = "select id from master_questioner where type = 'Basic Operational' and category = 'CHECKLIST' order by version desc limit 1";
                    checklistId = await conn2.QueryFirstOrDefaultAsync<string>(sqlChecklist);
                }
                else
                {
                    var sqlChecklist = "select id from master_questioner where type = 'Mystery Audit' and category = 'CHECKLIST' order by version desc limit 1";
                    checklistId = await conn2.QueryFirstOrDefaultAsync<string>(sqlChecklist);

                    var sqlIntro = "select id from master_questioner where type = 'Mystery Audit' and category = 'INTRO' order by version desc limit 1";
                    introId = await conn2.QueryFirstOrDefaultAsync<string>(sqlIntro);
                }

                var trxAudit = new trx_audit
                {
                    id = Guid.NewGuid().ToString(),
                    report_prefix = "",
                    report_no = "",                          
                    spbu_id = spbuId,
                    app_user_id = userId,
                    master_questioner_intro_id = introId,
                    master_questioner_checklist_id = checklistId,
                    audit_level = nextauditsspbu,
                    audit_type = TipeAudit,
                    audit_schedule_date = DateOnly.FromDateTime(TanggalAudit),
                    audit_execution_time = null,
                    audit_media_upload = 0,
                    audit_media_total = 0,
                    audit_mom_intro = "",
                    audit_mom_final = "",
                    status = "NOT_STARTED",
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(string id)
        {
            var audit = _context.trx_audits.FirstOrDefault(a => a.id == id && a.status != "DELETED");
            if (audit != null)
            {
                audit.status = "DELETED";
                audit.updated_date = DateTime.Now;
                _context.SaveChanges();
                TempData["Success"] = "Jadwal audit berhasil dihapus.";
            }
            return RedirectToAction("Index");
        }

    }
}
