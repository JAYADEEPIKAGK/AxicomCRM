using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AcxiomCRM.Models;
using AcxiomCRM.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

public class HomeController(CrmDbContext db) : Controller
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult Index()
    {
        return RedirectToAction(nameof(Dashboard));
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Dashboard()
    {
        var customers = db.Customers.AccessibleTo(User, db.Users).Where(x => x.IsActive);
        var leads = db.Leads.AccessibleTo(User, db.Users).Where(x => x.IsActive);
        var opportunities = db.Opportunities.AccessibleTo(User, db.Users).Where(x => x.IsActive);
        var followUps = db.FollowUps.AccessibleTo(User, db.Users).Where(x => x.IsActive && x.CompletedAtUtc == null);
        var model = new DashboardViewModel
        {
            Customers = await customers.CountAsync(),
            Leads = await leads.CountAsync(),
            OpenLeads = await leads.CountAsync(x => x.Status != LeadStatuses.Converted && x.Status != LeadStatuses.Lost),
            Opportunities = await opportunities.CountAsync(),
            OpenOpportunities = await opportunities.CountAsync(x => x.Stage != OpportunityStages.Won && x.Stage != OpportunityStages.Lost),
            Won = await opportunities.CountAsync(x => x.Stage == OpportunityStages.Won),
            Lost = await opportunities.CountAsync(x => x.Stage == OpportunityStages.Lost),
            Pipeline = await opportunities.Where(x => x.Stage != OpportunityStages.Won && x.Stage != OpportunityStages.Lost).SumAsync(x => (decimal?)x.Amount) ?? 0,
            LeadByStatus = await leads.GroupBy(x => x.Status).Select(x => new ChartPoint(x.Key, x.Count())).ToListAsync(),
            OpportunitiesByStage = await opportunities.GroupBy(x => x.Stage).Select(x => new ChartPoint(x.Key, x.Count())).ToListAsync(),
            MonthlySales = await GetMonthlySalesAsync(opportunities),
            UpcomingFollowUps = await followUps.Where(x => x.DueAt >= DateTime.Now).OrderBy(x => x.DueAt).Take(5).ToListAsync()
        };
        return View(model);
    }

    private static async Task<List<ChartPoint>> GetMonthlySalesAsync(IQueryable<Opportunity> opportunities)
    {
        var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstMonth = currentMonth.AddMonths(-5);
        var sales = await opportunities
            .Where(x => x.Stage == OpportunityStages.Won && x.ClosedAtUtc >= firstMonth)
            .GroupBy(x => new { x.ClosedAtUtc!.Value.Year, x.ClosedAtUtc.Value.Month })
            .Select(x => new { x.Key.Year, x.Key.Month, Amount = (double)x.Sum(o => o.Amount) })
            .ToListAsync();
        return Enumerable.Range(0, 6)
            .Select(offset => firstMonth.AddMonths(offset))
            .Select(month => new ChartPoint(
                month.ToString("yyyy-MM"),
                sales.FirstOrDefault(x => x.Year == month.Year && x.Month == month.Month)?.Amount ?? 0))
            .ToList();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public record ChartPoint(string Label, double Value);
    public class DashboardViewModel
    {
        public int Customers { get; set; }
        public int Leads { get; set; }
        public int OpenLeads { get; set; }
        public int Opportunities { get; set; }
        public int OpenOpportunities { get; set; }
        public int Won { get; set; }
        public int Lost { get; set; }
        public decimal Pipeline { get; set; }
        public List<ChartPoint> LeadByStatus { get; set; } = [];
        public List<ChartPoint> OpportunitiesByStage { get; set; } = [];
        public List<ChartPoint> MonthlySales { get; set; } = [];
        public List<AcxiomCRM.Models.FollowUp> UpcomingFollowUps { get; set; } = [];
    }
}
