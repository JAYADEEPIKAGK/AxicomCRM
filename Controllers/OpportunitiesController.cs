using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

[Authorize]
public class OpportunitiesController(CrmDbContext db, IAuditService audit) : Controller
{
    private IQueryable<Opportunity> Scope() => db.Opportunities.AccessibleTo(User, db.Users);
    public async Task<IActionResult> Index(string? search, string? stage)
    {
        var query = Scope().Where(x => x.IsActive).Include(x => x.Customer).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search) || (x.Customer != null && x.Customer.Name.Contains(search)));
        if (!string.IsNullOrWhiteSpace(stage) && OpportunityStages.All.Contains(stage)) query = query.Where(x => x.Stage == stage);
        ViewBag.Search = search;
        ViewBag.Stage = stage;
        return View(await query.OrderBy(x => x.ExpectedCloseDate).Take(200).ToListAsync());
    }
    public async Task<IActionResult> Details(int id)
    {
        var opportunity = await Scope().Include(x => x.Customer).Include(x => x.Lead).FirstOrDefaultAsync(x => x.Id == id);
        return opportunity is null ? NotFound() : View(opportunity);
    }
    public async Task<IActionResult> Create()
    {
        await PopulateRelatedRecordsAsync();
        return View(new Opportunity());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Opportunity input)
    {
        input.IsActive = true;
        ValidateBusinessRules(input);
        await ValidateRelatedRecordsAsync(input);
        if (!ModelState.IsValid) { await PopulateRelatedRecordsAsync(); return View(input); }
        input.OwnerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        input.ClosedAtUtc = input.Stage == OpportunityStages.Won ? DateTime.UtcNow : null;
        db.Opportunities.Add(input); await db.SaveChangesAsync();
        await audit.RecordAsync("Create", "Opportunity", input.Id.ToString());
        TempData["Success"] = "Opportunity created."; return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> Edit(int id)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        await PopulateRelatedRecordsAsync();
        return View(item);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Opportunity input)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        input.IsActive = item.IsActive;
        ValidateBusinessRules(input);
        await ValidateRelatedRecordsAsync(input);
        if (!ModelState.IsValid) { await PopulateRelatedRecordsAsync(); return View(input); }
        item.Name = input.Name; item.Amount = input.Amount; item.Probability = input.Probability; item.Stage = input.Stage; item.ExpectedCloseDate = input.ExpectedCloseDate; item.CustomerId = input.CustomerId; item.LeadId = input.LeadId;
        item.ClosedAtUtc = item.Stage == OpportunityStages.Won ? item.ClosedAtUtc ?? DateTime.UtcNow : null;
        await db.SaveChangesAsync(); await audit.RecordAsync("Update", "Opportunity", id.ToString());
        TempData["Success"] = "Opportunity updated."; return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        item.IsActive = false; await db.SaveChangesAsync(); await audit.RecordAsync("Deactivate", "Opportunity", id.ToString());
        return RedirectToAction(nameof(Index));
    }
    private void ValidateBusinessRules(Opportunity item)
    {
        if (!OpportunityStages.All.Contains(item.Stage)) ModelState.AddModelError(nameof(item.Stage), "Choose a valid pipeline stage.");
        if (item.IsActive && item.Amount <= 0) ModelState.AddModelError(nameof(item.Amount), "Active opportunities must have an amount greater than zero.");
        if (item.Probability is < 0 or > 100) ModelState.AddModelError(nameof(item.Probability), "Probability must be between 0 and 100.");
        if (item.IsActive && item.ExpectedCloseDate.Date < DateTime.Today) ModelState.AddModelError(nameof(item.ExpectedCloseDate), "Expected close date cannot be in the past.");
    }

    private async Task ValidateRelatedRecordsAsync(Opportunity item)
    {
        if (item.CustomerId is not null && !await db.Customers.AccessibleTo(User, db.Users).AnyAsync(x => x.Id == item.CustomerId && x.IsActive))
            ModelState.AddModelError(nameof(item.CustomerId), "Select an active customer you have access to.");
        if (item.LeadId is not null && !await db.Leads.AccessibleTo(User, db.Users).AnyAsync(x => x.Id == item.LeadId && x.IsActive))
            ModelState.AddModelError(nameof(item.LeadId), "Select an active lead you have access to.");
    }

    private async Task PopulateRelatedRecordsAsync()
    {
        ViewBag.Customers = await db.Customers.AccessibleTo(User, db.Users).Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        ViewBag.Leads = await db.Leads.AccessibleTo(User, db.Users).Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
    }
}
