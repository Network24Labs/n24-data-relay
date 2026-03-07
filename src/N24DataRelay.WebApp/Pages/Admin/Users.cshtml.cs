using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using N24DataRelay.WebApp.Data;

namespace N24DataRelay.WebApp.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UsersModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UsersModel(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public List<UserViewModel> PendingUsers { get; set; } = new();
    public List<UserViewModel> AllUsers { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public class UserViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public DateTime RegistrationDate { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? ApprovedBy { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsCurrentUser { get; set; }
    }

    public async Task OnGetAsync()
    {
        var currentUserId = _userManager.GetUserId(User);
        var users = _userManager.Users
            .OrderByDescending(u => u.RegistrationDate)
            .ToList();

        AllUsers = new();
        PendingUsers = new();

        foreach (var user in users)
        {
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var vm = new UserViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? "(unknown)",
                IsApproved = user.IsApproved,
                RegistrationDate = user.RegistrationDate,
                ApprovedDate = user.ApprovedDate,
                ApprovedBy = user.ApprovedBy,
                IsAdmin = isAdmin,
                IsCurrentUser = user.Id == currentUserId
            };
            AllUsers.Add(vm);
            if (!user.IsApproved)
                PendingUsers.Add(vm);
        }
    }

    public async Task<IActionResult> OnPostApproveAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        user.IsApproved = true;
        user.ApprovedDate = DateTime.UtcNow;
        user.ApprovedBy = User.Identity?.Name;
        await _userManager.UpdateAsync(user);

        StatusMessage = $"User {user.Email} has been approved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var email = user.Email ?? user.UserName;
        await _userManager.DeleteAsync(user);

        StatusMessage = $"User {email} has been removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAdminAsync(string userId)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (userId == currentUserId)
        {
            StatusMessage = "You cannot modify your own admin role.";
            return RedirectToPage();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            await _userManager.RemoveFromRoleAsync(user, "Admin");
            StatusMessage = $"{user.Email} removed from Admin role.";
        }
        else
        {
            await _userManager.AddToRoleAsync(user, "Admin");
            StatusMessage = $"{user.Email} added to Admin role.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string userId)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (userId == currentUserId)
        {
            StatusMessage = "You cannot revoke your own account.";
            return RedirectToPage();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        user.IsApproved = false;
        await _userManager.UpdateAsync(user);

        StatusMessage = $"Access revoked for {user.Email}.";
        return RedirectToPage();
    }
}
