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
                return RedirectToAction("Index", "Home");

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.app_users.FirstOrDefaultAsync(u => u.username == model.Username && u.status == "ACTIVE");

            if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.password_hash))
            {
                ModelState.AddModelError(string.Empty, "Username atau password salah");
                return View(model);
            }

            // Tambahkan claim
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.username),
                new Claim("FullName", user.name),
                new Claim(ClaimTypes.NameIdentifier, user.id)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));

            // Set session juga
            HttpContext.Session.SetString("UserId", user.id);
            HttpContext.Session.SetString("UserName", user.name);

            return RedirectToAction("Index", "Home");
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
