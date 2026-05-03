using Microsoft.AspNetCore.Mvc;
using e_Pas_CMS.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using e_Pas_CMS.Data;
using e_Pas_CMS.Helpers;

namespace e_Pas_CMS.Controllers
{
    public class AuthController : Controller
    {
        private readonly EpasDbContext _context;

        public AuthController(EpasDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
                return RedirectToAction("Index", "Dashboard");

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.app_users
                .FirstOrDefaultAsync(u =>
                    u.username == model.Username &&
                    u.status == "ACTIVE");

            if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.password_hash))
            {
                ModelState.AddModelError(string.Empty, "Username atau password salah");
                return View(model);
            }

            var userRoles = await (
                from aur in _context.app_user_roles
                join ar in _context.app_roles on aur.app_role_id equals ar.id
                where aur.app_user_id == user.id
                select ar.name
            ).Distinct().ToListAsync();

            if (userRoles.Count == 1 &&
                userRoles[0].Equals("auditor", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Tidak bisa login dengan user ini");
                return View(model);
            }

            var menuFunctionsRaw = await (
                from aur in _context.app_user_roles
                join ar in _context.app_roles on aur.app_role_id equals ar.id
                where aur.app_user_id == user.id
                      && ar.status == "ACTIVE"
                      && ar.menu_function != null
                      && ar.menu_function != ""
                select ar.menu_function
            ).ToListAsync();

            var menuFunctionTokens = menuFunctionsRaw
                .SelectMany(PermissionHelper.ParseMenuFunctions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            var mergedMenuFunction = string.Join("#", menuFunctionTokens);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.username),
                new Claim("FullName", user.name ?? ""),
                new Claim(ClaimTypes.NameIdentifier, user.id),
                new Claim(PermissionHelper.MenuFunctionClaimType, mergedMenuFunction)
            };

            foreach (var role in userRoles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            foreach (var permission in menuFunctionTokens)
            {
                claims.Add(new Claim(PermissionHelper.PermissionClaimType, permission));
            }

            var claimsIdentity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTime.UtcNow.AddHours(8)
                });

            HttpContext.Session.SetString("UserId", user.id);
            HttpContext.Session.SetString("UserName", user.name ?? "");
            HttpContext.Session.SetString("MenuFunction", mergedMenuFunction);

            return RedirectToAction("Index", "Dashboard");
        }

        public async Task<IActionResult> Logout()
        {
            HttpContext.Session.Clear();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View("AccessDenied");
        }
    }
}