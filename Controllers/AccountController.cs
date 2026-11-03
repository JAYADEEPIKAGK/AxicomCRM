using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

public class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, RoleManager<IdentityRole> roles, IAuditService audit) : Controller
{
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginInput { ReturnUrl = returnUrl });

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email);
        var result = user is { IsActive: true }
            ? await signIn.PasswordSignInAsync(user, input.Password, input.RememberMe, lockoutOnFailure: true)
            : Microsoft.AspNetCore.Identity.SignInResult.Failed;
        await audit.RecordAsync(result.Succeeded ? "LoginSuccess" : "LoginFailure", "User", user?.Id, "Authentication attempt", user?.Id, user?.Email);
        if (result.Succeeded) return LocalRedirect(Url.IsLocalUrl(input.ReturnUrl) ? input.ReturnUrl! : Url.Action("Index", "Home")!);
        ModelState.AddModelError(string.Empty, result.IsLockedOut ? "Account locked. Try again later." : "Invalid email or password, or the account is inactive.");
        return View(input);
    }

    [AllowAnonymous]
    public IActionResult Register() => View(new RegisterInput());

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = new ApplicationUser { UserName = input.Email, Email = input.Email, FullName = input.FullName, EmailConfirmed = false };
        var result = await users.CreateAsync(user, input.Password);
        if (result.Succeeded)
        {
            if (!await roles.RoleExistsAsync("Sales Executive"))
            {
                var roleResult = await roles.CreateAsync(new IdentityRole("Sales Executive"));
                if (!roleResult.Succeeded)
                {
                    await users.DeleteAsync(user);
                    foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                    return View(input);
                }
            }
            var membership = await users.AddToRoleAsync(user, "Sales Executive");
            if (!membership.Succeeded)
            {
                await users.DeleteAsync(user);
                foreach (var error in membership.Errors) ModelState.AddModelError(string.Empty, error.Description);
                return View(input);
            }
            await audit.RecordAsync("Register", "User", user.Id, actorUserId: user.Id, actorUserName: user.Email);
            await signIn.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Home");
        }
        foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        return View(input);
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await audit.RecordAsync("Logout", "User");
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}

public class LoginInput
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterInput
{
    [Required, StringLength(100), Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required, MinLength(8), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

[Authorize(Roles = "Admin")]
public class UsersController(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, IAuditService audit) : Controller
{
    public async Task<IActionResult> Index() => View(await users.Users.OrderBy(x => x.FullName).ToListAsync());

    public async Task<IActionResult> Create()
    {
        await PopulateManagersAsync();
        return View(new UserAdminInput());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserAdminInput input)
    {
        ValidateRole(input.Role);
        if (string.IsNullOrWhiteSpace(input.Password)) ModelState.AddModelError(nameof(input.Password), "A password is required.");
        if (!await IsValidManagerAsync(input.ManagerUserId)) ModelState.AddModelError(nameof(input.ManagerUserId), "Select an active user with the Manager role.");
        if (!ModelState.IsValid) { await PopulateManagersAsync(); return View(input); }
        var user = new ApplicationUser { UserName = input.Email, Email = input.Email, FullName = input.FullName, ManagerUserId = input.ManagerUserId, EmailConfirmed = true };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            await PopulateManagersAsync();
            return View(input);
        }
        await EnsureRoleExistsAsync(input.Role);
        var roleResult = await users.AddToRoleAsync(user, input.Role);
        if (!roleResult.Succeeded)
        {
            await users.DeleteAsync(user);
            foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            await PopulateManagersAsync();
            return View(input);
        }
        await audit.RecordAsync("UserCreated", "User", user.Id, input.Role);
        TempData["Success"] = "User created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        await PopulateManagersAsync();
        return View(new UserAdminInput { Id = user.Id, FullName = user.FullName, Email = user.Email ?? string.Empty, Role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "Sales Executive", IsActive = user.IsActive, ManagerUserId = user.ManagerUserId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UserAdminInput input)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        ValidateRole(input.Role);
        if (!input.IsActive && user.Id == User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier))
        {
            ModelState.AddModelError(nameof(input.IsActive), "You cannot deactivate your own account.");
        }
        var existingRoles = await users.GetRolesAsync(user);
        if (existingRoles.Contains("Admin") && input.Role != "Admin" && user.Id == User.FindFirstValue(ClaimTypes.NameIdentifier))
            ModelState.AddModelError(nameof(input.Role), "You cannot change your own administrator role.");
        if (existingRoles.Contains("Manager") && input.Role != "Manager" && await users.Users.AnyAsync(x => x.ManagerUserId == user.Id))
            ModelState.AddModelError(nameof(input.Role), "Reassign this manager's team before changing their role.");
        if (!input.IsActive && existingRoles.Contains("Manager") && await users.Users.AnyAsync(x => x.ManagerUserId == user.Id))
            ModelState.AddModelError(nameof(input.IsActive), "Reassign this manager's team before deactivating the account.");
        if (!await IsValidManagerAsync(input.ManagerUserId, user.Id)) ModelState.AddModelError(nameof(input.ManagerUserId), "Select an active manager other than this user.");
        if (!ModelState.IsValid) { await PopulateManagersAsync(); return View(input); }
        user.FullName = input.FullName;
        user.Email = input.Email;
        user.UserName = input.Email;
        user.IsActive = input.IsActive;
        user.ManagerUserId = input.ManagerUserId;
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            foreach (var error in update.Errors) ModelState.AddModelError(string.Empty, error.Description);
            await PopulateManagersAsync();
            return View(input);
        }
        var current = existingRoles;
        if (!current.Contains(input.Role))
        {
            if (current.Contains("Admin") && user.Id == User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier))
            {
                ModelState.AddModelError(nameof(input.Role), "You cannot change your own administrator role.");
                await PopulateManagersAsync();
                return View(input);
            }
            await EnsureRoleExistsAsync(input.Role);
            var removal = await users.RemoveFromRolesAsync(user, current);
            if (!removal.Succeeded)
            {
                foreach (var error in removal.Errors) ModelState.AddModelError(string.Empty, error.Description);
                await PopulateManagersAsync();
                return View(input);
            }
            var addition = await users.AddToRoleAsync(user, input.Role);
            if (!addition.Succeeded)
            {
                foreach (var error in addition.Errors) ModelState.AddModelError(string.Empty, error.Description);
                await PopulateManagersAsync();
                return View(input);
            }
            await audit.RecordAsync("RoleChanged", "User", user.Id, input.Role);
        }
        var stampResult = await users.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            foreach (var error in stampResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            await PopulateManagersAsync();
            return View(input);
        }
        await audit.RecordAsync("UserUpdated", "User", user.Id);
        TempData["Success"] = "User updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(string id, bool active)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (!active && user.Id == User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier))
        {
            TempData["Error"] = "You cannot deactivate your own account.";
            return RedirectToAction(nameof(Index));
        }
        if (!active && await users.IsInRoleAsync(user, "Manager") && await users.Users.AnyAsync(x => x.ManagerUserId == user.Id))
        {
            TempData["Error"] = "Reassign this manager's team before deactivating the account.";
            return RedirectToAction(nameof(Index));
        }
        user.IsActive = active;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(result.Errors);
        var stampResult = await users.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded) return BadRequest(stampResult.Errors);
        await audit.RecordAsync(active ? "UserActivated" : "UserDeactivated", "User", id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string id, string role)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (role is not ("Admin" or "Manager" or "Sales Executive")) return BadRequest("Invalid role.");
        var current = await users.GetRolesAsync(user);
        if (current.Contains("Manager") && role != "Manager" && await users.Users.AnyAsync(x => x.ManagerUserId == user.Id))
        {
            TempData["Error"] = "Reassign this manager's team before changing their role.";
            return RedirectToAction(nameof(Index));
        }
        if (current.Contains("Admin") && role != "Admin" && user.Id == User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier))
        {
            TempData["Error"] = "You cannot change your own administrator role.";
            return RedirectToAction(nameof(Index));
        }
        var remove = await users.RemoveFromRolesAsync(user, current);
        if (!remove.Succeeded) return BadRequest(remove.Errors);
        await EnsureRoleExistsAsync(role);
        var add = await users.AddToRoleAsync(user, role);
        if (!add.Succeeded) return BadRequest(add.Errors);
        var stampResult = await users.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded) return BadRequest(stampResult.Errors);
        await audit.RecordAsync("RoleChanged", "User", id, role);
        return RedirectToAction(nameof(Index));
    }

    private void ValidateRole(string role)
    {
        if (role is not ("Admin" or "Manager" or "Sales Executive")) ModelState.AddModelError(nameof(UserAdminInput.Role), "Choose a valid role.");
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        if (await roles.RoleExistsAsync(role)) return;
        var result = await roles.CreateAsync(new IdentityRole(role));
        if (!result.Succeeded) throw new InvalidOperationException($"Could not create role '{role}': {string.Join("; ", result.Errors.Select(x => x.Description))}");
    }

    private async Task PopulateManagersAsync()
        => ViewBag.Managers = (await users.GetUsersInRoleAsync("Manager")).Where(x => x.IsActive).OrderBy(x => x.FullName).ToList();

    private async Task<bool> IsValidManagerAsync(string? managerUserId, string? userBeingEdited = null)
    {
        if (managerUserId is null) return true;
        if (managerUserId == userBeingEdited) return false;
        var manager = await users.FindByIdAsync(managerUserId);
        return manager is { IsActive: true } && await users.IsInRoleAsync(manager, "Manager");
    }
}

public class UserAdminInput
{
    public string? Id { get; set; }
    [Required, StringLength(100)] public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = "Sales Executive";
    public string? ManagerUserId { get; set; }
    public bool IsActive { get; set; } = true;
    [MinLength(8), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
}
