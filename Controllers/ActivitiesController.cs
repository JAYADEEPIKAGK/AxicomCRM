using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

[Authorize]
public class ActivitiesController(CrmDbContext db, IAuditService audit) : Controller
{
    private IQueryable<Activity> Scope() => db.Activities.AccessibleTo(User, db.Users);
    public async Task<IActionResult> Index() => View(await Scope().Where(x => x.IsActive).Include(x => x.Customer).Include(x => x.Lead).Include(x => x.Opportunity).OrderByDescending(x => x.OccurredAtUtc).Take(200).ToListAsync());
    public async Task<IActionResult> Create()
    {
        await PopulateRelatedRecordsAsync();
        return View(new Activity());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Activity input)
    {
        if (input.Type is not ("Call" or "Meeting" or "Email" or "Task")) ModelState.AddModelError(nameof(input.Type), "Choose a valid activity type.");
        var relatedCount = (input.CustomerId.HasValue ? 1 : 0) + (input.LeadId.HasValue ? 1 : 0) + (input.OpportunityId.HasValue ? 1 : 0);
        if (relatedCount > 1) ModelState.AddModelError("", "Select only one related record.");
        if (input.CustomerId is not null && !await db.Customers.AccessibleTo(User, db.Users).AnyAsync(x => x.Id == input.CustomerId && x.IsActive))
            ModelState.AddModelError(nameof(input.CustomerId), "Select an active customer you have access to.");
        if (input.LeadId is not null && !await db.Leads.AccessibleTo(User, db.Users).AnyAsync(x => x.Id == input.LeadId && x.IsActive))
            ModelState.AddModelError(nameof(input.LeadId), "Select an active lead you have access to.");
        if (input.OpportunityId is not null && !await db.Opportunities.AccessibleTo(User, db.Users).AnyAsync(x => x.Id == input.OpportunityId && x.IsActive))
            ModelState.AddModelError(nameof(input.OpportunityId), "Select an active opportunity you have access to.");
        if (!ModelState.IsValid) { await PopulateRelatedRecordsAsync(); return View(input); }
        input.OwnerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        input.IsActive = true;
        db.Activities.Add(input); await db.SaveChangesAsync();
        await audit.RecordAsync("Create", "Activity", input.Id.ToString());
        TempData["Success"] = "Activity logged."; return RedirectToAction(nameof(Index));
    }

    private async Task PopulateRelatedRecordsAsync()
    {
        ViewBag.Customers = await db.Customers.AccessibleTo(User, db.Users).Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        ViewBag.Leads = await db.Leads.AccessibleTo(User, db.Users).Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        ViewBag.Opportunities = await db.Opportunities.AccessibleTo(User, db.Users).Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
    }
}
