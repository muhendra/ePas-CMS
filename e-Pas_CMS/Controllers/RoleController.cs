using Microsoft.AspNetCore.Mvc;
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
    [Route("Role")]
    [Authorize]
    public class RoleController : Controller
    {
        private readonly EpasDbContext _context;

        public RoleController(EpasDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, string searchTerm = "")
        {
            var query = _context.app_users
                .Where(u => u.status != "DELETED" && u.status == "ACTIVE");

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var lowerSearch = searchTerm.ToLower();

                query = query.Where(x =>
                    (!string.IsNullOrEmpty(x.name) && x.name.ToLower().Contains(lowerSearch)) ||
                    (!string.IsNullOrEmpty(x.username) && x.username.ToLower().Contains(lowerSearch))
                );
            }
        
            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var users = await query
                .OrderBy(x => x.name)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userIds = users.Select(u => u.id).ToList();

            var userRoles = await (from ur in _context.app_user_roles
                                   join r in _context.app_roles on ur.app_role_id equals r.id
                                   where userIds.Contains(ur.app_user_id)
                                   select new
                                   {
                                       UserId = ur.app_user_id,
                                       RoleName = r.name,
                                       Region = ur.region
                                   }).ToListAsync();

            var result = users.Select(u => new RoleAuditorViewModel
            {
                Id = u.id,
                Auditor = u.name,
                Username = u.username,
                email = u.email,
                NamaRole = string.Join(", ", userRoles.Where(x => x.UserId == u.id).Select(x => x.RoleName).Distinct()),
                Region = string.Join(", ", userRoles.Where(x => x.UserId == u.id && x.Region != null).Select(x => x.Region).Distinct().OrderBy(x => x)),
                Status = u.status == "ACTIVE" ? "ACTIVE" : "Nonaktif",
                SpbuList = new List<string>()
            }).ToList();

            var vm = new RoleAuditorIndexViewModel
            {
                Items = result,
                CurrentPage = pageNumber,
                TotalPages = totalPages,
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
            var model = new RoleAuditorAddViewModel
            {
                //AuditorList = _context.app_users
                //    .Where(x => x.status == "ACTIVE")
                //    .Select(x => new SelectListItem { Value = x.id, Text = x.name })
                //    .ToList(),

                RoleList = _context.app_roles
                    .Where(x => x.status == "ACTIVE")
                    .Select(x => new SelectListItem { Value = x.id, Text = x.name })
                    .ToList(),

                RegionList = _context.spbus
                    .Select(x => x.region)
                    .Distinct()
                    .OrderBy(r => r)
                    .Select(r => new SelectListItem { Value = r, Text = r })
                    .ToList(),
                SbmList = _context.spbus
            .Where(x => x.sbm != null)
            .Select(x => x.sbm)
            .Distinct()
            .OrderBy(s => s)
            .Select(s => new SelectListItem { Value = s, Text = s })
            .ToList(),
                SpbuList = _context.spbus
    .Where(x => x.spbu_no != null)
    .OrderBy(x => x.spbu_no)
    .Select(x => new SelectListItem
    {
        Value = x.id,
        Text = x.spbu_no + " - " + x.province_name + " - " + x.city_name + " - " + x.address
    }).ToList()

            };

            return View(model);
        }

        [HttpPost("Add")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(RoleAuditorAddViewModel model)
        {
            if (string.IsNullOrEmpty(model.Username) || string.IsNullOrEmpty(model.Password))
            {
                ModelState.AddModelError("", "Username dan Password wajib diisi.");
                return View(model);
            }

            var existingUser = await _context.app_users.FirstOrDefaultAsync(x => x.username == model.Username);
            if (existingUser != null)
            {
                ModelState.AddModelError("", "Username sudah digunakan.");
                return View(model);
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);

            var newUser = new app_user
            {
                id = Guid.NewGuid().ToString(),
                username = model.Username,
                password_hash = passwordHash,
                name = model.Name,
                phone_number = model.Handphone,
                email = model.Email,
                status = "ACTIVE",
                created_by = User.Identity.Name ?? "SYSTEM",
                updated_by = User.Identity.Name ?? "SYSTEM",
                created_date = DateTime.Now,
                updated_date = DateTime.Now
            };

            _context.app_users.Add(newUser);
            await _context.SaveChangesAsync();

            var roleIds = model.SelectedRoleIds?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var regionIds = model.SelectedRegionIds?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var sbmIds = model.SelectedSbmIds?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var selectedSpbuId = model.SelectedSpbuId;

            try
            {
                foreach (var roleId in roleIds)
                {
                    // Cek apakah nama role adalah DEFAULT_SPBU
                    var roleName = await _context.app_roles
                        .Where(x => x.id == roleId)
                        .Select(x => x.name)
                        .FirstOrDefaultAsync();

                    if ((roleName ?? "").Trim().ToUpper() == "DEFAULT_SPBU" && !string.IsNullOrEmpty(selectedSpbuId))
                    {
                        // Khusus DEFAULT_SPBU simpan dengan spbu_id
                        await AddUserRoleIfNotExist(newUser.id, roleId, null, null, selectedSpbuId);
                    }
                    else
                    {
                        // Jika ada Region dan SBM dipilih
                        if (regionIds.Length > 0 && sbmIds.Length > 0)
                        {
                            foreach (var region in regionIds)
                            {
                                foreach (var sbm in sbmIds)
                                {
                                    await AddUserRoleIfNotExist(newUser.id, roleId, region, sbm);
                                }
                            }
                        }
                        // Hanya Region
                        else if (regionIds.Length > 0)
                        {
                            foreach (var region in regionIds)
                            {
                                await AddUserRoleIfNotExist(newUser.id, roleId, region, null);
                            }
                        }
                        // Hanya SBM
                        else if (sbmIds.Length > 0)
                        {
                            foreach (var sbm in sbmIds)
                            {
                                await AddUserRoleIfNotExist(newUser.id, roleId, null, sbm);
                            }
                        }
                        // Tidak ada Region maupun SBM
                        else
                        {
                            await AddUserRoleIfNotExist(newUser.id, roleId, null, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Optional logging
                Console.WriteLine(ex.Message);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "User berhasil ditambahkan.";
            return RedirectToAction("Index");
        }

        private async Task AddUserRoleIfNotExist(string userId, string roleId, string region, string sbm, string spbuId = null)
        {
            var exists = await _context.app_user_roles.AnyAsync(x =>
                x.app_user_id == userId &&
                x.app_role_id == roleId &&
                x.region == region &&
                x.sbm == sbm &&
                x.spbu_id == spbuId);

            if (!exists)
            {
                var newRole = new app_user_role
                {
                    id = Guid.NewGuid().ToString(),
                    app_user_id = userId,
                    app_role_id = roleId,
                    region = region,
                    sbm = sbm,
                    spbu_id = spbuId
                };

                _context.app_user_roles.Add(newRole);
            }
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
        public async Task<IActionResult> Edit(string id)
        {
            var auditor = await _context.app_users
                .FirstOrDefaultAsync(x => x.id == id && x.status != "DELETED");

            if (auditor == null)
                return NotFound();

            var userRoles = await (from ur in _context.app_user_roles
                                   join r in _context.app_roles on ur.app_role_id equals r.id
                                   where ur.app_user_id == id
                                   select new { ur.app_role_id, r.name }).ToListAsync();

            var userRegions = await _context.app_user_roles
                .Where(x => x.app_user_id == id && x.region != null)
                .Select(x => x.region)
                .Distinct()
                .ToListAsync();

            var userSbms = await _context.app_user_roles
                .Where(x => x.app_user_id == id && x.sbm != null)
                .Select(x => x.sbm)
                .Distinct()
                .ToListAsync();
            var userSpbuId = await _context.app_user_roles
    .Where(x => x.app_user_id == id && x.spbu_id != null)
    .Select(x => x.spbu_id)
    .FirstOrDefaultAsync();

            var userSpbuName = await _context.spbus
                .Where(x => x.id == userSpbuId)
                .Select(x => x.spbu_no + " - " + x.province_name + " - " + x.city_name + " - " + x.address)
                .FirstOrDefaultAsync();

            var model = new RoleAuditorEditViewModel
            {
                AuditorId = auditor.id,
                UserName = auditor.username,
                AuditorName = auditor.name,
                Handphone = auditor.phone_number,
                Email = auditor.email,

                SelectedRoleIds = userRoles
                    .Select(x => x.app_role_id)
                    .Distinct()
                    .ToList(),

                SelectedRoleNames = userRoles
                    .Select(x => x.name)
                    .Distinct()
                    .ToList(),

                SelectedRegionIds = userRegions,
                SelectedRegionNames = userRegions,

                SelectedSbmIds = userSbms,
                SelectedSbmNames = userSbms,

                RoleList = await _context.app_roles
                    .Where(x => x.status == "ACTIVE")
                    .Select(x => new SelectListItem { Value = x.id, Text = x.name })
                    .ToListAsync(),

                RegionList = await _context.spbus
                    .Select(x => x.region)
                    .Distinct()
                    .OrderBy(r => r)
                    .Select(r => new SelectListItem { Value = r, Text = r })
                    .ToListAsync(),

                SbmList = await _context.spbus
                    .Where(x => !string.IsNullOrEmpty(x.sbm))
                    .Select(x => x.sbm)
                    .Distinct()
                    .OrderBy(s => s)
                    .Select(s => new SelectListItem { Value = s, Text = s })
                    .ToListAsync(),
                    SelectedSpbuId = userSpbuId,
                SelectedSpbuName = userSpbuName,
                SpbuList = await _context.spbus
        .Where(x => x.spbu_no != null)
        .OrderBy(x => x.spbu_no)
        .Select(x => new SelectListItem
        {
            Value = x.id,
            Text = x.spbu_no + " - " + x.province_name + " - " + x.city_name + " - " + x.address
        })
        .ToListAsync()
            };

            return View(model);
        }
        [HttpPost("Edit/{id}")]
        public async Task<IActionResult> Edit(RoleAuditorEditViewModel model)
        {
            if (string.IsNullOrEmpty(model.AuditorId))
            {
                ModelState.AddModelError("", "Auditor ID tidak ditemukan.");
                return View(model);
            }

            var user = await _context.app_users.FirstOrDefaultAsync(x => x.id == model.AuditorId);
            if (user == null)
            {
                ModelState.AddModelError("", "User tidak ditemukan.");
                return View(model);
            }

            // Update user (profil dasar)
            user.username = model.UserName;
            user.name = model.AuditorName;
            user.phone_number = model.Handphone;
            user.email = model.Email;
            user.updated_by = User.Identity.Name ?? "SYSTEM";
            user.updated_date = DateTime.Now;

            if (!string.IsNullOrEmpty(model.Password))
                user.password_hash = BCrypt.Net.BCrypt.HashPassword(model.Password);

            _context.app_users.Update(user);
            await _context.SaveChangesAsync();

            // ==== Ambil pilihan terbaru dari form ====
            var roleIds = (model.SelectedRoleIds ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var regionIds = (model.SelectedRegionIds ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var sbmIds = (model.SelectedSbmIds ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var selectedSpbuId = (model.SelectedSpbuId ?? "").Trim();

            // Peta roleId -> roleName
            var roleMap = await _context.app_roles
                .Where(r => roleIds.Contains(r.id))
                .ToDictionaryAsync(r => r.id, r => (r.name ?? "").Trim().ToUpper());

            // Cari roleId untuk DEFAULT_SPBU (yang terpilih saat ini)
            var defaultSpbuRoleIds = roleMap
                .Where(kv => kv.Value == "DEFAULT_SPBU")
                .Select(kv => kv.Key)
                .ToHashSet();

            bool hasDefaultSpbuRole = defaultSpbuRoleIds.Any();

            // Validasi: jika DEFAULT_SPBU dipilih, SPBU wajib dipilih
            if (hasDefaultSpbuRole && string.IsNullOrEmpty(selectedSpbuId))
            {
                ModelState.AddModelError("", "SPBU wajib dipilih saat role DEFAULT_SPBU dicentang.");
                return View(model);
            }

            // ==== Data eksisting user ====
            var existingRoles = await _context.app_user_roles
                .Where(x => x.app_user_id == model.AuditorId)
                .ToListAsync();

            var rolesToDelete = new List<app_user_role>();

            // Ambil semua roleId yang bernama DEFAULT_SPBU dari master (untuk bantu cek baris lama)
            var allDefaultRoleIds = await _context.app_roles
                .Where(r => r.name != null && r.name.ToUpper() == "DEFAULT_SPBU")
                .Select(r => r.id)
                .ToListAsync();
            var allDefaultRoleIdsSet = allDefaultRoleIds.ToHashSet();

            foreach (var ur in existingRoles)
            {
                // 1) Role tidak dipilih lagi
                if (!roleIds.Contains(ur.app_role_id))
                {
                    rolesToDelete.Add(ur);
                    continue;
                }

                // 2) Baris DEFAULT_SPBU
                if (allDefaultRoleIdsSet.Contains(ur.app_role_id))
                {
                    // Jika sekarang tidak pilih DEFAULT_SPBU sama sekali -> hapus semua DEFAULT_SPBU
                    if (!hasDefaultSpbuRole)
                    {
                        rolesToDelete.Add(ur);
                        continue;
                    }
                    // Jika pilih DEFAULT_SPBU, pertahankan hanya spbu_id yang sama dengan pilihan baru
                    if (string.IsNullOrEmpty(selectedSpbuId) || (ur.spbu_id ?? "") != selectedSpbuId)
                    {
                        rolesToDelete.Add(ur);
                    }
                    continue;
                }

                // 3) Non-DEFAULT_SPBU (pakai logika lama region/sbm)
                if (ur.region != null && !regionIds.Contains(ur.region))
                {
                    rolesToDelete.Add(ur);
                    continue;
                }

                if (ur.sbm != null && !sbmIds.Contains(ur.sbm))
                {
                    rolesToDelete.Add(ur);
                    continue;
                }

                // Jika sebelumnya null tapi sekarang user memilih region/sbm, baris "role-only" harus dibersihkan
                if ((ur.region == null && regionIds.Any()) || (ur.sbm == null && sbmIds.Any()))
                {
                    rolesToDelete.Add(ur);
                    continue;
                }
            }

            if (rolesToDelete.Any())
            {
                _context.app_user_roles.RemoveRange(rolesToDelete);
                await _context.SaveChangesAsync();
            }

            // Refresh cache kombinasi setelah delete
            var existingCombinations = await _context.app_user_roles
                .Where(x => x.app_user_id == model.AuditorId)
                .ToListAsync();

            // ==== Tambah kombinasi yang belum ada ====
            foreach (var roleId in roleIds)
            {
                bool isDefaultSpbu = defaultSpbuRoleIds.Contains(roleId);

                if (isDefaultSpbu)
                {
                    // DEFAULT_SPBU: simpan 1 baris dengan spbu_id = SelectedSpbuId (region/sbm = null)
                    if (!string.IsNullOrEmpty(selectedSpbuId))
                    {
                        var exists = existingCombinations.Any(x =>
                            x.app_role_id == roleId &&
                            x.app_user_id == model.AuditorId &&
                            x.spbu_id == selectedSpbuId &&
                            x.region == null &&
                            x.sbm == null);

                        if (!exists)
                        {
                            _context.app_user_roles.Add(new app_user_role
                            {
                                id = Guid.NewGuid().ToString(),
                                app_user_id = model.AuditorId,
                                app_role_id = roleId,
                                spbu_id = selectedSpbuId,
                                region = null,
                                sbm = null
                            });
                        }
                    }

                    // Lewati logika region/sbm untuk role DEFAULT_SPBU
                    continue;
                }

                // === NON-DEFAULT_SPBU ===

                bool addedAny = false;

                // 1) Kombinasi Region
                if (regionIds.Any())
                {
                    foreach (var region in regionIds)
                    {
                        var exists = existingCombinations.Any(x =>
                            x.app_role_id == roleId &&
                            x.region == region &&
                            x.sbm == null &&
                            x.spbu_id == null);

                        if (!exists)
                        {
                            _context.app_user_roles.Add(new app_user_role
                            {
                                id = Guid.NewGuid().ToString(),
                                app_user_id = model.AuditorId,
                                app_role_id = roleId,
                                region = region,
                                sbm = null,
                                spbu_id = null
                            });
                            addedAny = true;
                        }
                    }
                }

                // 2) Kombinasi SBM
                if (sbmIds.Any())
                {
                    foreach (var sbm in sbmIds)
                    {
                        var exists = existingCombinations.Any(x =>
                            x.app_role_id == roleId &&
                            x.region == null &&
                            x.sbm == sbm &&
                            x.spbu_id == null);

                        if (!exists)
                        {
                            _context.app_user_roles.Add(new app_user_role
                            {
                                id = Guid.NewGuid().ToString(),
                                app_user_id = model.AuditorId,
                                app_role_id = roleId,
                                region = null,
                                sbm = sbm,
                                spbu_id = null
                            });
                            addedAny = true;
                        }
                    }
                }

                // 3) Jika user tidak memilih region & sbm, simpan role-only
                if (!regionIds.Any() && !sbmIds.Any())
                {
                    var exists = existingCombinations.Any(x =>
                        x.app_role_id == roleId &&
                        x.region == null &&
                        x.sbm == null &&
                        x.spbu_id == null);

                    if (!exists)
                    {
                        _context.app_user_roles.Add(new app_user_role
                        {
                            id = Guid.NewGuid().ToString(),
                            app_user_id = model.AuditorId,
                            app_role_id = roleId,
                            region = null,
                            sbm = null,
                            spbu_id = null
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Data berhasil diperbarui.";
            return RedirectToAction("Index");
        }


        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Invalid User ID.");
            }

            var user = await _context.app_users.FirstOrDefaultAsync(x => x.id == id);
            if (user == null)
            {
                return NotFound("User tidak ditemukan.");
            }

            user.status = "INACTIVE";
            user.updated_by = User.Identity.Name ?? "SYSTEM";
            user.updated_date = DateTime.Now;

            _context.app_users.Update(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = "User berhasil di-nonaktifkan.";
            return RedirectToAction("Index");
        }


        private ActionResult HttpNotFound()
        {
            throw new NotImplementedException();
        }

        [HttpGet("Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            var query = from au in _context.app_users
                        where au.id == id && au.status != "DELETED"
                        join aur in _context.app_user_roles on au.id equals aur.app_user_id into aurGroup
                        from aur in aurGroup.DefaultIfEmpty()
                        join ar in _context.app_roles on aur.app_role_id equals ar.id into arGroup
                        from ar in arGroup.DefaultIfEmpty()
                        join s in _context.spbus on aur.region equals s.region into sGroup
                        from s in sGroup.DefaultIfEmpty()
                        where aur.region != null
                        group new { au, ar, aur, s } by new { au.id, au.name, au.status } into g
                        select new RoleAuditorDetailViewModel
                        {
                            AuditorId = g.Key.id,
                            AuditorName = g.Key.name,
                            AuditorStatus = g.Key.status == "ACTIVE" ? "ACTIVE" : "Nonaktif",
                            RoleNames = g.Select(x => x.ar.name).Where(x => x != null).Distinct().ToList(),
                            RegionNames = g.Select(x => x.aur.region).Where(x => x != null).Distinct().ToList(),
                            SpbuList = g.Select(x => x.s.spbu_no).Where(x => x != null).Distinct().ToList()
                        };

            var result = await query.FirstOrDefaultAsync();
            if (result == null)
                return NotFound();

            return View(result);
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
                    x.region,
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

    }
}
