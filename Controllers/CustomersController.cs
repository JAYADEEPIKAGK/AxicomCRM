using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

[Authorize]
public class CustomersController(CrmDbContext db, IAuditService audit) : Controller
{
    private IQueryable<Customer> Scope() => db.Customers.AccessibleTo(User, db.Users);

    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var q = Scope().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || (x.Company != null && x.Company.Contains(search)) || (x.Email != null && x.Email.Contains(search)));
        var total = await q.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20d)));
        var rows = await q.OrderBy(x => x.Name).Skip((page - 1) * 20).Take(20).ToListAsync();
        ViewBag.Search = search;
        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / 20d));
        return View(rows);
    }

    public async Task<IActionResult> Details(int id)
    {
        var customer = await Scope().Include(x => x.Leads).FirstOrDefaultAsync(x => x.Id == id);
        return customer is null ? NotFound() : View(customer);
    }
    public IActionResult Create() => View(new Customer());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer input)
    {
        if (!ModelState.IsValid) return View(input);
        input.OwnerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        input.IsActive = true;
        db.Customers.Add(input);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqlException { Number: 2601 or 2627 }) { ModelState.AddModelError("", "A customer with this email or phone already exists."); return View(input); }
        await audit.RecordAsync("Create", "Customer", input.Id.ToString());
        TempData["Success"] = "Customer created.";
        return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> Edit(int id)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : View(item);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Customer input)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        if (!ModelState.IsValid) return View(input);
        item.Name = input.Name; item.Email = input.Email; item.Phone = input.Phone; item.Company = input.Company; item.Address = input.Address;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqlException { Number: 2601 or 2627 }) { ModelState.AddModelError("", "A customer with this email or phone already exists."); return View(input); }
        await audit.RecordAsync("Update", "Customer", id.ToString());
        TempData["Success"] = "Customer updated.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var item = await Scope().FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        item.IsActive = false;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Deactivate", "Customer", id.ToString());
        TempData["Success"] = "Customer deactivated.";
        return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> History(int id)
    {
        if (!await Scope().AnyAsync(x => x.Id == id)) return NotFound();
        return View(await db.AuditLogs.Where(x => x.EntityType == "Customer" && x.EntityId == id.ToString()).OrderByDescending(x => x.CreatedAtUtc).ToListAsync());
    }
}
