using CloudStorage.Models;
using CloudStorage.Models.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CloudStorage.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var viewModel = new DashboardViewModel
        {
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? (user.Email ?? "User") : user.DisplayName,
            Email = user.Email ?? string.Empty
        };

        return View(viewModel);
    }
}
