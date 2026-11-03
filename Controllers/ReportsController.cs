using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

[Authorize]
public class ReportsController(CrmDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var customers = db.Customers.AccessibleTo(User, db.Users).Where(x => x.IsActive);
        var leads = db.Leads.AccessibleTo(User, db.Users).Where(x => x.IsActive);
        var opps = db.Opportunities.AccessibleTo(User, db.Users).Where(x => x.IsActive);
        ViewBag.Customers = await customers.CountAsync();
        ViewBag.Leads = await leads.GroupBy(x => x.Status).Select(x => new { Name = x.Key, Count = x.Count() }).ToListAsync();
        ViewBag.Opportunities = await opps.GroupBy(x => x.Stage).Select(x => new { Name = x.Key, Count = x.Count(), Value = x.Sum(o => o.Amount) }).ToListAsync();
        ViewBag.FollowUps = await db.FollowUps.AccessibleTo(User, db.Users).Where(x => x.IsActive).CountAsync();
        ViewBag.Activities = await db.Activities.AccessibleTo(User, db.Users).Where(x => x.IsActive).CountAsync();
        return View();
    }

    public async Task<IActionResult> Customers()
        => View(await db.Customers.AccessibleTo(User, db.Users).Where(x => x.IsActive).OrderBy(x => x.Name).Take(500).ToListAsync());

    public async Task<IActionResult> Leads()
        => View(await db.Leads.AccessibleTo(User, db.Users).Where(x => x.IsActive).OrderBy(x => x.Status).ThenBy(x => x.Name).Take(500).ToListAsync());

    public async Task<IActionResult> FollowUps()
        => View(await db.FollowUps.AccessibleTo(User, db.Users).Where(x => x.IsActive).Include(x => x.Customer).Include(x => x.Lead).OrderByDescending(x => x.DueAt).Take(500).ToListAsync());

    public async Task<IActionResult> Opportunities()
        => View(await db.Opportunities.AccessibleTo(User, db.Users).Where(x => x.IsActive).Include(x => x.Customer).OrderBy(x => x.ExpectedCloseDate).Take(500).ToListAsync());

    public async Task<IActionResult> Pipeline()
        => View(await db.Opportunities.AccessibleTo(User, db.Users).Where(x => x.IsActive && x.Stage != OpportunityStages.Won && x.Stage != OpportunityStages.Lost).Include(x => x.Customer).OrderBy(x => x.ExpectedCloseDate).Take(500).ToListAsync());

    public async Task<IActionResult> Conversion()
    {
        var leads = db.Leads.AccessibleTo(User, db.Users).Where(x => x.IsActive);
        var total = await leads.CountAsync();
        var converted = await leads.CountAsync(x => x.Status == LeadStatuses.Converted);
        var model = new ConversionReportViewModel
        {
            TotalLeads = total,
            ConvertedLeads = converted,
            ConversionRate = total == 0 ? 0 : (decimal)converted / total * 100,
            ByStatus = await leads.GroupBy(x => x.Status).Select(x => new StatusCount(x.Key, x.Count())).ToListAsync()
        };
        return View(model);
    }

    public async Task<IActionResult> UserActivity()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var logs = db.AuditLogs.AsQueryable();
        if (User.IsInRole("Manager"))
        {
            var teamIds = db.Users.Where(x => x.Id == userId || x.ManagerUserId == userId).Select(x => x.Id);
            logs = logs.Where(x => x.UserId != null && teamIds.Contains(x.UserId));
        }
        else if (!User.IsInRole("Admin"))
        {
            logs = logs.Where(x => x.UserId == userId);
        }
        return View(await logs.OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync());
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Audit()
        => View(await db.AuditLogs.OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync());

    public record StatusCount(string Status, int Count);
    public class ConversionReportViewModel
    {
        public int TotalLeads { get; set; }
        public int ConvertedLeads { get; set; }
        public decimal ConversionRate { get; set; }
        public List<StatusCount> ByStatus { get; set; } = [];
    }
}
