using Microsoft.AspNetCore.Mvc;
using e_Pas_CMS.ViewModels;
using e_Pas_CMS.Models;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using e_Pas_CMS.Data;

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
            // Redirect kalau udah login
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
                .FirstOrDefaultAsync(u => u.username == model.Username && u.status == "ACTIVE");

            if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.password_hash))
            {
                ModelState.AddModelError(string.Empty, "Username atau password salah");
                return View(model);
            }

            // Ambil semua role user dari relasi
            var userRoles = await (from aur in _context.app_user_roles
                                   join ar in _context.app_roles on aur.app_role_id equals ar.id
                                   where aur.app_user_id == user.id
                                   select ar.name).ToListAsync();

            // Validasi: jika hanya punya 1 role dan itu "auditor", tolak login
            if (userRoles.Count == 1 && userRoles[0].Equals("auditor", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Tidak bisa login dengan user ini");
                return View(model);
            }

            // Tambahkan claims dasar
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.username),
        new Claim("FullName", user.name ?? ""),
        new Claim(ClaimTypes.NameIdentifier, user.id)
    };

            // Tambahkan semua role sebagai claim
            foreach (var role in userRoles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }


            // Ambil semua Menu Function Code (ex: ARA, ARP, etc)
            var menuFunctionsRaw = await (from aur in _context.app_user_roles
                                          join ar in _context.app_roles on aur.app_role_id equals ar.id
                                          select ar.menu_function)
                                          .Where(mf => mf != null && mf != "")
                                          .ToListAsync();

            // Pecah berdasarkan '#' dan ambil yang unik
            var menuFunctionTokens = menuFunctionsRaw
                .SelectMany(mf => mf.Split('#'))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct()
                .OrderBy(token => token) // Optional: urutkan alphabetically
                .ToList();

            // Gabungkan kembali menjadi 1 string
            var mergedMenuFunction = string.Join("#", menuFunctionTokens);

            // Tambahkan ke Claims (hanya 1 klaim)
            claims.Add(new Claim("MenuFunction", mergedMenuFunction));

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            // IMPORTANT: Build the principal explicitly with full claims.
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal,
                new AuthenticationProperties
                {
                    IsPersistent = true, // Optional: persistent login (keep cookie)
                    ExpiresUtc = DateTime.UtcNow.AddHours(8) // Optional: expiry time
                });

            // Set session jika diperlukan
            HttpContext.Session.SetString("UserId", user.id);
            HttpContext.Session.SetString("UserName", user.name ?? "");

            return RedirectToAction("Index", "Dashboard");
        }


        public async Task<IActionResult> Logout()
        {
            // Hapus session
            HttpContext.Session.Clear();

            // Hapus cookie auth
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
