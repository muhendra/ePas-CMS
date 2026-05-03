using e_Pas_CMS.Data;
using e_Pas_CMS.ViewModels;
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
            Email = user.email
        };

        return View(vm);
    }

    // ========================
    // UPDATE PROFILE
    // ========================
    [HttpPost]
    public async Task<IActionResult> Index(ProfileViewModel vm)
    {
        var user = await _context.app_users.FindAsync(vm.Id);
        if (user == null) return NotFound();

        user.name = vm.Name;
        user.phone_number = vm.PhoneNumber;
        user.email = vm.Email;
        user.updated_date = DateTime.Now;
        user.updated_by = User.Identity.Name;

        await _context.SaveChangesAsync();

        TempData["Success"] = "Profile berhasil diupdate";
        return RedirectToAction("Index");
    }

    // ========================
    // CHANGE PASSWORD PAGE
    // ========================
    public IActionResult ChangePassword()
    {
        return View();
    }

    // ========================
    // CHANGE PASSWORD POST
    // ========================
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