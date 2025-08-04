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
    [Route("RoleManagement")]
    [Authorize]
    public class RoleManagementController : Controller
    {
        private readonly EpasDbContext _context;

        public RoleManagementController(EpasDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, string searchTerm = "")
        {
            var query = _context.app_roles.Where(x => x.status == "ACTIVE").AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(x => x.name.Contains(searchTerm) || x.app.Contains(searchTerm));
            }

            var totalRecords = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

            var items = await query
                .OrderBy(x => x.name)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new RoleManagementItemViewModel
                {
                    Id = x.id,
                    NamaRole = x.name,
                    App = x.app,
                    MenuFunction = x.menu_function,
                    Status = x.status
                })
                .ToListAsync();

            var viewModel = new RoleManagementIndexViewModel
            {
                Items = items,
                SearchTerm = searchTerm,
                CurrentPage = pageNumber,
                TotalPages = totalPages
            };

            return View(viewModel);
        }


        [HttpGet("Edit/{id}")]
        public async Task<IActionResult> Edit(string id)
        {
            var role = await _context.app_roles.FirstOrDefaultAsync(x => x.id == id);
            if (role == null)
                return NotFound();

            var selectedFunctions = string.IsNullOrEmpty(role.menu_function)
                ? new List<string>()
                : role.menu_function.Split('#').ToList();

            var viewModel = new RoleManagementEditViewModel
            {
                Id = role.id,
                NamaRole = role.name,
                App = role.app,
                Status = role.status,
                MenuFunction = role.menu_function,
                SelectedMenuFunctions = selectedFunctions,
                MenuFunctionList = GetMenuFunctionList()
            };

            return View(viewModel);
        }

        [HttpPost("Edit/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, RoleManagementEditViewModel model)
        {
            //if (!ModelState.IsValid)
            //{
            //    model.MenuFunctionList = GetMenuFunctionList();
            //    return View(model);
            //}

            var role = await _context.app_roles.FirstOrDefaultAsync(x => x.id == id);
            if (role == null)
                return NotFound();

            role.name = model.NamaRole;
            role.app = model.App;
            role.status = model.Status;
            role.menu_function = string.Join("#", model.SelectedMenuFunctions);

            await _context.SaveChangesAsync();

            TempData["Success"] = "Role berhasil diupdate.";
            return RedirectToAction("Index");
        }

        // Helper Method
        private List<SelectListItem> GetMenuFunctionList()
        {
            return new List<SelectListItem>
    {
        new SelectListItem { Text = "Audit - Review Audit", Value = "ARA" },
        new SelectListItem { Text = "Audit - Report", Value = "ARP" },
        new SelectListItem { Text = "Audit Basic Operational - Review Audit BOA", Value = "BOARA" },
        new SelectListItem { Text = "Audit Basic Operational - Report BOA", Value = "BOARP" },
        new SelectListItem { Text = "Scheduler - Add Scheduler", Value = "SSCH" },
        new SelectListItem { Text = "Master - SPBU", Value = "MSPBU" },
        new SelectListItem { Text = "Access - App Role", Value = "AROLE" },
        new SelectListItem { Text = "Access - App User", Value = "AUSER" },
        new SelectListItem { Text = "Feedback - Komplain", Value = "FKOM" },
        new SelectListItem { Text = "Feedback - Banding", Value = "FBAN" },
    };
        }

    }
}
