using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

public class ProfileController : Controller
{
    private readonly EpasDbContext _context;

    public ProfileController(EpasDbContext context)
    {
        _context = context;
    }

    // ========================
    // GET PROFILE
    // ========================
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var username = User.Identity.Name;

        var user = await _context.app_users
            .FirstOrDefaultAsync(x => x.username == username);

        if (user == null) return NotFound();

        var vm = new ProfileViewModel
        {
            Id = user.id,
            Username = user.username,
            Name = user.name,
            PhoneNumber = user.phone_number,
            Email = user.email,
            Homebase = user.homebase,
            AuditorCode = user.auditor_code
        };

        return View(vm);
    }

    // ========================
    // UPDATE PROFILE
    // ========================
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProfileViewModel vm)
    {
        // Identity must come from session; posted Id can be tampered with.
        var username = User.Identity?.Name;
        var user = await _context.app_users.FirstOrDefaultAsync(x => x.username == username);
        if (user == null) return NotFound();

        // Auditor ID can only be changed for the currently authenticated user.
        // Never use the posted vm.Id or vm.Username to select the DB record.
        var auditorCode = vm.AuditorCode?.Trim();
        if (auditorCode?.Length > 20)
        {
            ModelState.AddModelError(nameof(vm.AuditorCode),
                "ID Auditor maksimal 20 karakter.");
        }
        else if (!string.IsNullOrEmpty(auditorCode))
        {
            // Prevent different users from sharing the same auditor identifier.
            var normalizedCode = auditorCode.ToLowerInvariant();
            var alreadyUsed = await _context.app_users.AsNoTracking().AnyAsync(x =>
                x.id != user.id && x.auditor_code != null &&
                x.auditor_code.ToLower() == normalizedCode);

            if (alreadyUsed)
            {
                ModelState.AddModelError(nameof(vm.AuditorCode),
                    "ID Auditor sudah digunakan oleh user lain.");
            }
        }

        if (!ModelState.IsValid)
        {
            vm.Id = user.id;
            vm.Username = user.username;
            return View(vm);
        }

        user.name = vm.Name;
        user.phone_number = vm.PhoneNumber;
        user.email = vm.Email;
        user.homebase = (vm.Homebase ?? "").Trim();
        user.auditor_code = string.IsNullOrEmpty(auditorCode) ? null : auditorCode;
        user.updated_date = DateTime.Now;
        user.updated_by = user.username;

        await _context.SaveChangesAsync();

        TempData["Success"] = "Profile berhasil diupdate";
        return RedirectToAction("Index");
    }

    // ========================
    // CHANGE PASSWORD PAGE
    // ========================
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View();
    }

    // ========================
    // CHANGE PASSWORD POST
    // ========================
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        if (vm.NewPassword != vm.ConfirmPassword)
        {
            TempData["Error"] = "Password tidak sama";
            return RedirectToAction("ChangePassword");
        }

        var username = User.Identity.Name;

        var user = await _context.app_users
            .FirstOrDefaultAsync(x => x.username == username);

        if (user == null) return NotFound();

        // CHECK PASSWORD LAMA
        var currentHash = Hash(vm.CurrentPassword);
        if (currentHash != user.password_hash)
        {
            TempData["Error"] = "Password lama salah";
            return RedirectToAction("ChangePassword");
        }

        user.password_hash = Hash(vm.NewPassword);
        user.last_change_passwd_dt = DateTime.Now;
        user.updated_date = DateTime.Now;

        await _context.SaveChangesAsync();

        TempData["Success"] = "Password berhasil diubah";
        return RedirectToAction("ChangePassword");
    }

    // ========================
    // HASH FUNCTION
    // ========================
    private string Hash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
