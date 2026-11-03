using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

[Authorize]
public class FollowUpsController(CrmDbContext db, IAuditService audit) : Controller
{
    private IQueryable<FollowUp> Scope() => db.FollowUps.AccessibleTo(User, db.Users);
    public async Task<IActionResult> Index(string view = "pending")
    {
        IQueryable<FollowUp> q = Scope().Where(x => x.IsActive).Include(x => x.Customer).Include(x => x.Lead);
        q = view == "missed"
            ? q.Where(x => x.CompletedAtUtc == null && x.DueAt < DateTime.Now)
            : q.Where(x => x.CompletedAtUtc == null);
        ViewBag.View = view;
        return View(await q.OrderBy(x => x.DueAt).Take(200).ToListAsync());
    }
    public async Task<IActionResult> Create()
    {
        await PopulateOptionsAsync();
        return View(new FollowUp { DueAt = DateTime.Today.AddDays(1) });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FollowUp input)
    {
        if (input.DueAt < DateTime.Now) ModelState.AddModelError(nameof(input.DueAt), "Follow-up date must be in the future.");
        if (input.CustomerId is null && input.LeadId is null) ModelState.AddModelError("", "Select a customer or lead.");
        if (input.CustomerId is not null && input.LeadId is not null) ModelState.AddModelError("", "Choose either a customer or a lead, not both.");
        if (input.CustomerId is not null && !await db.Customers.AccessibleTo(User, db.Users).AnyAsync(x => x.Id == input.CustomerId && x.IsActive))
            ModelState.AddModelError(nameof(input.CustomerId), "Select an active customer you have access to.");
        if (input.LeadId is not null && !await db.Leads.AccessibleTo(User, db.Users).AnyAsync(x => x.Id == input.LeadId && x.IsActive))
            ModelState.AddModelError(nameof(input.LeadId), "Select an active lead you have access to.");
        if (!ModelState.IsValid) { await PopulateOptionsAsync(); return View(input); }
        input.OwnerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        input.IsActive = true;
        input.CompletedAtUtc = null;
        db.FollowUps.Add(input); await db.SaveChangesAsync();
        await audit.RecordAsync("Create", "FollowUp", input.Id.ToString());
        TempData["Success"] = "Follow-up scheduled."; return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive && x.CompletedAtUtc == null);
        if (item is null) return NotFound();
        item.CompletedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(); await audit.RecordAsync("Complete", "FollowUp", id.ToString());
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(int id, DateTime dueAt)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive && x.CompletedAtUtc == null);
        if (item is null) return NotFound();
        if (dueAt < DateTime.Now)
        {
            TempData["Error"] = "Follow-up date must be in the future.";
            return RedirectToAction(nameof(Index));
        }
        item.DueAt = dueAt; item.CompletedAtUtc = null; await db.SaveChangesAsync();
        await audit.RecordAsync("Reschedule", "FollowUp", id.ToString());
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateOptionsAsync()
    {
        ViewBag.Customers = await db.Customers.AccessibleTo(User, db.Users).Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        ViewBag.Leads = await db.Leads.AccessibleTo(User, db.Users).Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
    }
}
