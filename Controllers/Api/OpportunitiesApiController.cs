using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AcxiomCRM.Controllers.Api;

[ApiController, Route("api/opportunities"), Authorize]
public class OpportunitiesApiController(CrmDbContext db, IAuditService audit) : ControllerBase
{
    private IQueryable<Opportunity> Scope() => db.Opportunities.AccessibleTo(User, db.Users);
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OpportunityDto>>> Get(string? stage)
    {
        var q = Scope().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(stage)) q = q.Where(x => x.Stage == stage);
        return await q.OrderBy(x => x.ExpectedCloseDate).Take(100).Select(x => new OpportunityDto(x.Id, x.Name, x.Amount, x.Probability, x.Stage, x.ExpectedCloseDate)).ToListAsync();
    }
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OpportunityDto>> Get(int id)
    {
        var x = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        return x is null ? NotFound() : new OpportunityDto(x.Id, x.Name, x.Amount, x.Probability, x.Stage, x.ExpectedCloseDate);
    }
    [HttpPost]
    public async Task<ActionResult<OpportunityDto>> Create(OpportunityWriteDto dto)
    {
        var error = Validate(dto);
        if (error is not null) { ModelState.AddModelError(nameof(OpportunityWriteDto), error); return ValidationProblem(ModelState); }
        var x = new Opportunity { Name = dto.Name, Amount = dto.Amount, Probability = dto.Probability, Stage = dto.Stage, ExpectedCloseDate = dto.ExpectedCloseDate, ClosedAtUtc = dto.Stage == OpportunityStages.Won ? DateTime.UtcNow : null, OwnerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)! };
        db.Opportunities.Add(x); await db.SaveChangesAsync(); await audit.RecordAsync("Create", "Opportunity", x.Id.ToString());
        return CreatedAtAction(nameof(Get), new { id = x.Id }, new OpportunityDto(x.Id, x.Name, x.Amount, x.Probability, x.Stage, x.ExpectedCloseDate));
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, OpportunityWriteDto dto)
    {
        var error = Validate(dto);
        if (error is not null) { ModelState.AddModelError(nameof(OpportunityWriteDto), error); return ValidationProblem(ModelState); }
        var x = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (x is null) return NotFound();
        x.Name = dto.Name; x.Amount = dto.Amount; x.Probability = dto.Probability; x.Stage = dto.Stage; x.ExpectedCloseDate = dto.ExpectedCloseDate;
        x.ClosedAtUtc = x.Stage == OpportunityStages.Won ? x.ClosedAtUtc ?? DateTime.UtcNow : null;
        await db.SaveChangesAsync(); await audit.RecordAsync("Update", "Opportunity", id.ToString()); return NoContent();
    }
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var x = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (x is null) return NotFound();
        x.IsActive = false; await db.SaveChangesAsync(); await audit.RecordAsync("Deactivate", "Opportunity", id.ToString()); return NoContent();
    }
    private static string? Validate(OpportunityWriteDto dto)
    {
        if (!OpportunityStages.All.Contains(dto.Stage)) return "Invalid pipeline stage.";
        if (dto.Amount <= 0) return "Amount must be greater than zero.";
        if (dto.Probability is < 0 or > 100) return "Probability must be between 0 and 100.";
        if (dto.ExpectedCloseDate.Date < DateTime.Today) return "Expected close date cannot be in the past.";
        return null;
    }
}
public record OpportunityDto(int Id, string Name, decimal Amount, int Probability, string Stage, DateTime ExpectedCloseDate);
public record OpportunityWriteDto([Required, StringLength(160)] string Name, [Range(0.01, 999999999)] decimal Amount, [Range(0, 100)] int Probability, [Required] string Stage, DateTime ExpectedCloseDate);
