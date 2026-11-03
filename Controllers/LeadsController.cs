using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

[Authorize]
public class LeadsController(CrmDbContext db, IAuditService audit, UserManager<ApplicationUser> users) : Controller
{
    private IQueryable<Lead> Scope() => db.Leads.AccessibleTo(User, db.Users);
    public async Task<IActionResult> Index(string? status, string? search, int page = 1)
    {
        var q = Scope().Where(x => x.IsActive);
        if (!string.IsNullOrEmpty(status) && LeadStatuses.All.Contains(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || (x.Company != null && x.Company.Contains(search)));
        var total = await q.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        ViewBag.Status = status; ViewBag.Search = search; ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / 20d));
        return View(await q.OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * 20).Take(20).ToListAsync());
    }
    public async Task<IActionResult> Details(int id)
    {
        var lead = await Scope().Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id);
        return lead is null ? NotFound() : View(lead);
    }
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Assign(int id)
    {
        var lead = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (lead is null) return NotFound();
        await PopulateAssigneesAsync();
        return View(new LeadAssignmentInput { LeadId = lead.Id, OwnerUserId = lead.OwnerUserId });
    }

    [Authorize(Roles = "Admin,Manager"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int id, LeadAssignmentInput input)
    {
        var lead = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (lead is null) return NotFound();
        if (input.LeadId != id) return BadRequest();
        var assignee = await users.FindByIdAsync(input.OwnerUserId);
        var mayAssign = assignee is { IsActive: true } &&
            (User.IsInRole("Admin") ||
             assignee.Id == User.FindFirstValue(ClaimTypes.NameIdentifier) ||
             assignee.ManagerUserId == User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (!mayAssign) ModelState.AddModelError(nameof(input.OwnerUserId), "Select an active member of your team.");
        if (!ModelState.IsValid)
        {
            await PopulateAssigneesAsync();
            return View(input);
        }
        var oldOwner = lead.OwnerUserId;
        lead.OwnerUserId = assignee!.Id;
        await db.SaveChangesAsync();
        await audit.RecordAsync("LeadAssigned", "Lead", id.ToString(), $"Owner {oldOwner} -> {lead.OwnerUserId}");
        TempData["Success"] = "Lead assigned.";
        return RedirectToAction(nameof(Details), new { id });
    }
    public IActionResult Create() => View(new Lead());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Lead input)
    {
        if (!LeadStatuses.All.Contains(input.Status)) ModelState.AddModelError(nameof(input.Status), "Choose a valid lead status.");
        if (input.CustomerId is not null && !await db.Customers.AccessibleTo(User, db.Users).AnyAsync(x => x.Id == input.CustomerId && x.IsActive))
            ModelState.AddModelError(nameof(input.CustomerId), "Select an active customer you have access to.");
        if (!ModelState.IsValid) return View(input);
        input.OwnerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        input.IsActive = true;
        db.Leads.Add(input); await db.SaveChangesAsync();
        await audit.RecordAsync("Create", "Lead", input.Id.ToString());
        TempData["Success"] = "Lead created."; return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> Edit(int id)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : View(item);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Lead input)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        if (!LeadStatuses.All.Contains(input.Status)) ModelState.AddModelError(nameof(input.Status), "Choose a valid lead status.");
        if (!ModelState.IsValid) return View(input);
        item.Name = input.Name; item.Email = input.Email; item.Phone = input.Phone; item.Company = input.Company; item.Status = input.Status; item.Notes = input.Notes;
        await db.SaveChangesAsync(); await audit.RecordAsync("Update", "Lead", id.ToString());
        TempData["Success"] = "Lead updated."; return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(int id)
    {
        var lead = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        if (lead is null) return NotFound();
        if (lead.Status == LeadStatuses.Converted || lead.Status == LeadStatuses.Lost) return BadRequest("This lead cannot be converted.");
        var accessibleCustomers = db.Customers.AccessibleTo(User, db.Users).Where(x => x.IsActive);
        var customer = !string.IsNullOrWhiteSpace(lead.Email)
            ? await accessibleCustomers.FirstOrDefaultAsync(x => x.Email == lead.Email)
            : null;
        customer ??= !string.IsNullOrWhiteSpace(lead.Phone)
            ? await accessibleCustomers.FirstOrDefaultAsync(x => x.Phone == lead.Phone)
            : null;
        if (customer is null)
        {
            var duplicateExists = await db.Customers.AnyAsync(x =>
                (!string.IsNullOrWhiteSpace(lead.Email) && x.Email == lead.Email) ||
                (!string.IsNullOrWhiteSpace(lead.Phone) && x.Phone == lead.Phone));
            if (duplicateExists)
            {
                TempData["Error"] = "A customer with this contact information already exists outside your active records.";
                return RedirectToAction(nameof(Details), new { id });
            }
            customer = new Customer { Name = lead.Name, Email = lead.Email, Phone = lead.Phone, Company = lead.Company, OwnerUserId = lead.OwnerUserId };
            db.Customers.Add(customer);
        }
        lead.Customer = customer;
        lead.Status = LeadStatuses.Converted;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            TempData["Error"] = "A customer with this contact information was created concurrently. Review the customer record before retrying.";
            return RedirectToAction(nameof(Details), new { id });
        }
        await audit.RecordAsync("Convert", "Lead", id.ToString(), $"Customer {customer.Id}");
        TempData["Success"] = "Lead converted to customer."; return RedirectToAction("Details", "Customers", new { id = customer.Id });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        item.IsActive = false; await db.SaveChangesAsync(); await audit.RecordAsync("Deactivate", "Lead", id.ToString());
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateAssigneesAsync()
    {
        var teamMembers = await users.Users.Where(user => user.IsActive).OrderBy(user => user.FullName).ToListAsync();
        var managerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        ViewBag.Assignees = teamMembers.Where(user =>
            user.IsActive && (User.IsInRole("Admin") || user.Id == managerId || user.ManagerUserId == managerId))
            .OrderBy(user => user.FullName).ToList();
    }
}

public class LeadAssignmentInput
{
    public int LeadId { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    public string OwnerUserId { get; set; } = string.Empty;
}
