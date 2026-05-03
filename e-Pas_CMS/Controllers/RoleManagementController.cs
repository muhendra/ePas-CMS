using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using e_Pas_CMS.ViewModels;
using e_Pas_CMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace e_Pas_CMS.Controllers
{
    [Route("RoleManagement")]
    [Authorize]
    public class RoleManagementController : Controller
    {
        private readonly EpasDbContext _context;

        public RoleManagementController(EpasDbContext context)
        {
            _context = context;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string searchTerm = "")
        {
            if (pageNumber <= 0)
                pageNumber = 1;

            if (pageSize <= 0)
                pageSize = 10;

            var keyword = searchTerm?.Trim() ?? "";

            var query = _context.app_roles.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x =>
                    (x.name != null && x.name.Contains(keyword)) ||
                    (x.app != null && x.app.Contains(keyword)) ||
                    (x.menu_function != null && x.menu_function.Contains(keyword)) ||
                    (x.status != null && x.status.Contains(keyword))
                );
            }

            var totalRecords = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

            var roleData = await query
                .OrderBy(x => x.name)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var menuOptions = GetMenuFunctionList();
            var menuDictionary = menuOptions
                .GroupBy(x => x.Value)
                .ToDictionary(x => x.Key, x => x.First().Text);

            var items = roleData.Select(x =>
            {
                var selectedCodes = ParseMenuFunctions(x.menu_function);

                return new RoleManagementItemViewModel
                {
                    Id = x.id,
                    NamaRole = x.name,
                    App = x.app,
                    MenuFunction = x.menu_function,
                    MenuFunctionLabels = selectedCodes
                        .Select(code => menuDictionary.ContainsKey(code) ? menuDictionary[code] : code)
                        .ToList(),
                    TotalPermission = selectedCodes.Count,
                    Status = x.status
                };
            }).ToList();

            var viewModel = new RoleManagementIndexViewModel
            {
                Items = items,
                SearchTerm = keyword,
                CurrentPage = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                TotalPages = totalPages
            };

            return View(viewModel);
        }

        [HttpGet("Edit/{id}")]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction(nameof(Index));

            var role = await _context.app_roles.FirstOrDefaultAsync(x => x.id == id);

            if (role == null)
                return NotFound();

            var selectedFunctions = ParseMenuFunctions(role.menu_function);
            var menuFunctionList = GetMenuFunctionList();

            var viewModel = new RoleManagementEditViewModel
            {
                Id = role.id,
                NamaRole = role.name,
                App = role.app,
                Status = role.status,
                MenuFunction = role.menu_function,
                SelectedMenuFunctions = selectedFunctions,
                MenuFunctionList = menuFunctionList
            };

            return View(viewModel);
        }

        [HttpPost("Edit/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, RoleManagementEditViewModel model)
        {
            if (string.IsNullOrWhiteSpace(id))
                return RedirectToAction(nameof(Index));

            var role = await _context.app_roles.FirstOrDefaultAsync(x => x.id == id);

            if (role == null)
                return NotFound();

            var selectedFunctions = NormalizeSelectedFunctions(model.SelectedMenuFunctions);

            role.name = model.NamaRole?.Trim();
            role.app = model.App?.Trim();
            role.status = string.IsNullOrWhiteSpace(model.Status) ? "ACTIVE" : model.Status.Trim();
            role.menu_function = string.Join("#", selectedFunctions);

            await _context.SaveChangesAsync();

            TempData["Success"] = "Role berhasil diupdate.";
            return RedirectToAction(nameof(Index));
        }

        private static List<string> ParseMenuFunctions(string menuFunction)
        {
            if (string.IsNullOrWhiteSpace(menuFunction))
                return new List<string>();

            return menuFunction
                .Split("#", StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> NormalizeSelectedFunctions(IEnumerable<string> selectedFunctions)
        {
            if (selectedFunctions == null)
                return new List<string>();

            return selectedFunctions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        private List<SelectListItem> GetMenuFunctionList()
        {
            var dashboard = new SelectListGroup { Name = "Dashboard" };
            var audit = new SelectListGroup { Name = "Audit" };
            var scheduler = new SelectListGroup { Name = "Scheduler" };
            var master = new SelectListGroup { Name = "Master Data" };
            var access = new SelectListGroup { Name = "Access Management" };
            var feedback = new SelectListGroup { Name = "Feedback" };
            var finance = new SelectListGroup { Name = "Finance" };
            var cityBoundary = new SelectListGroup { Name = "Batas Kota" };
            var legacy = new SelectListGroup { Name = "Legacy Access" };

            return new List<SelectListItem>
            {
                new SelectListItem { Group = dashboard, Text = "Dashboard - View", Value = "DASH_VIEW" },
                new SelectListItem { Group = dashboard, Text = "Dashboard - Auditor", Value = "DASH_AUDITOR_VIEW" },
                new SelectListItem { Group = dashboard, Text = "Dashboard - Verifikator", Value = "DASH_VERIFIKATOR_VIEW" },
                new SelectListItem { Group = dashboard, Text = "Dashboard - Export", Value = "DASH_EXPORT" },

                new SelectListItem { Group = audit, Text = "Audit - View", Value = "AUDIT_VIEW" },
                new SelectListItem { Group = audit, Text = "Audit - Review", Value = "AUDIT_REVIEW" },
                new SelectListItem { Group = audit, Text = "Audit - Edit", Value = "AUDIT_EDIT" },
                new SelectListItem { Group = audit, Text = "Audit - Assign", Value = "AUDIT_ASSIGN" },
                new SelectListItem { Group = audit, Text = "Audit - Approve", Value = "AUDIT_APPROVE" },
                new SelectListItem { Group = audit, Text = "Audit - Report", Value = "AUDIT_REPORT" },

                new SelectListItem { Group = audit, Text = "Basic Operational - View", Value = "BOA_VIEW" },
                new SelectListItem { Group = audit, Text = "Basic Operational - Review", Value = "BOA_REVIEW" },
                new SelectListItem { Group = audit, Text = "Basic Operational - Report", Value = "BOA_REPORT" },

                new SelectListItem { Group = audit, Text = "Mystery Guest - View", Value = "MG_VIEW" },
                new SelectListItem { Group = audit, Text = "Mystery Guest - Review", Value = "MG_REVIEW" },
                new SelectListItem { Group = audit, Text = "Mystery Guest - Report", Value = "MG_REPORT" },

                new SelectListItem { Group = scheduler, Text = "Scheduler - View", Value = "SCHEDULER_VIEW" },
                new SelectListItem { Group = scheduler, Text = "Scheduler - Add", Value = "SCHEDULER_ADD" },
                new SelectListItem { Group = scheduler, Text = "Scheduler - Edit", Value = "SCHEDULER_EDIT" },
                new SelectListItem { Group = scheduler, Text = "Scheduler - Delete", Value = "SCHEDULER_DELETE" },
                new SelectListItem { Group = scheduler, Text = "Scheduler - Assign", Value = "SCHEDULER_ASSIGN" },

                new SelectListItem { Group = master, Text = "Master SPBU - View", Value = "MASTER_SPBU_VIEW" },
                new SelectListItem { Group = master, Text = "Master SPBU - Edit", Value = "MASTER_SPBU_EDIT" },
                new SelectListItem { Group = master, Text = "Master Checklist - View", Value = "MASTER_CHECKLIST_VIEW" },
                new SelectListItem { Group = master, Text = "Master Checklist - Edit", Value = "MASTER_CHECKLIST_EDIT" },

                new SelectListItem { Group = access, Text = "Role Management - View", Value = "ROLE_VIEW" },
                new SelectListItem { Group = access, Text = "Role Management - Edit", Value = "ROLE_EDIT" },
                new SelectListItem { Group = access, Text = "App User - View", Value = "USER_VIEW" },
                new SelectListItem { Group = access, Text = "App User - Edit", Value = "USER_EDIT" },
                new SelectListItem { Group = access, Text = "App User - Assign Role", Value = "USER_ASSIGN_ROLE" },
                new SelectListItem { Group = access, Text = "App User - Approve", Value = "USER_APPROVE" },

                new SelectListItem { Group = feedback, Text = "Komplain - View", Value = "FEEDBACK_KOMPLAIN_VIEW" },
                new SelectListItem { Group = feedback, Text = "Komplain - Edit", Value = "FEEDBACK_KOMPLAIN_EDIT" },
                new SelectListItem { Group = feedback, Text = "Banding - View", Value = "FEEDBACK_BANDING_VIEW" },
                new SelectListItem { Group = feedback, Text = "Banding - Edit", Value = "FEEDBACK_BANDING_EDIT" },
                new SelectListItem { Group = feedback, Text = "Banding - Approve", Value = "FEEDBACK_BANDING_APPROVE" },

                new SelectListItem { Group = finance, Text = "Invoice - View", Value = "FINANCE_INVOICE_VIEW" },
                new SelectListItem { Group = finance, Text = "Invoice - Process", Value = "FINANCE_INVOICE_PROCESS" },
                new SelectListItem { Group = finance, Text = "Invoice - Approve", Value = "FINANCE_INVOICE_APPROVE" },
                new SelectListItem { Group = finance, Text = "Invoice - Reject", Value = "FINANCE_INVOICE_REJECT" },
                new SelectListItem { Group = finance, Text = "Invoice - Export", Value = "FINANCE_INVOICE_EXPORT" },

                new SelectListItem { Group = cityBoundary, Text = "Batas Kota - List View", Value = "CITY_BOUNDARY_VIEW" },
                new SelectListItem { Group = cityBoundary, Text = "Batas Kota - Detail View", Value = "CITY_BOUNDARY_DETAIL" },
                new SelectListItem { Group = cityBoundary, Text = "Batas Kota - Approval View", Value = "CITY_BOUNDARY_APPROVAL_VIEW" },
                new SelectListItem { Group = cityBoundary, Text = "Batas Kota - Approve", Value = "CITY_BOUNDARY_APPROVE" },
                new SelectListItem { Group = cityBoundary, Text = "Batas Kota - Reject", Value = "CITY_BOUNDARY_REJECT" },

                new SelectListItem { Group = legacy, Text = "Legacy - Audit Review Audit", Value = "ARA" },
                new SelectListItem { Group = legacy, Text = "Legacy - Audit Report", Value = "ARP" },
                new SelectListItem { Group = legacy, Text = "Legacy - Basic Operational Review", Value = "BOARA" },
                new SelectListItem { Group = legacy, Text = "Legacy - Basic Operational Report", Value = "BOARP" },
                new SelectListItem { Group = legacy, Text = "Legacy - Mystery Guest Review", Value = "MGARA" },
                new SelectListItem { Group = legacy, Text = "Legacy - Mystery Guest Report", Value = "MGARP" },
                new SelectListItem { Group = legacy, Text = "Legacy - Dashboard", Value = "DASH" },
                new SelectListItem { Group = legacy, Text = "Legacy - Dashboard Auditor", Value = "DAUD" },
                new SelectListItem { Group = legacy, Text = "Legacy - Dashboard Verifikator", Value = "DVER" },
                new SelectListItem { Group = legacy, Text = "Legacy - Scheduler Add Scheduler", Value = "SSCH" },
                new SelectListItem { Group = legacy, Text = "Legacy - Master SPBU", Value = "MSPBU" },
                new SelectListItem { Group = legacy, Text = "Legacy - Master Checklist", Value = "MCHK" },
                new SelectListItem { Group = legacy, Text = "Legacy - Access App Role", Value = "AROLE" },
                new SelectListItem { Group = legacy, Text = "Legacy - Access App User", Value = "AUSER" },
                new SelectListItem { Group = legacy, Text = "Legacy - Feedback Komplain", Value = "FKOM" },
                new SelectListItem { Group = legacy, Text = "Legacy - Feedback Banding", Value = "FBAN" },
                new SelectListItem { Group = legacy, Text = "Legacy - Finance Invoice Management", Value = "FININV" },
                new SelectListItem { Group = legacy, Text = "Legacy - Batas Kota List", Value = "BKLST" },
                new SelectListItem { Group = legacy, Text = "Legacy - Batas Kota Approval", Value = "BKAPR" }
            };
        }
    }
}