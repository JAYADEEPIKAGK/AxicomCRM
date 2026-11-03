using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AcxiomCRM.Controllers.Api;

[ApiController, Route("api/leads"), Authorize]
public class LeadsApiController(CrmDbContext db, IAuditService audit) : ControllerBase
{
    private IQueryable<Lead> Scope() => db.Leads.AccessibleTo(User, db.Users);
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LeadDto>>> Get(string? status)
    {
        var q = Scope().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        return await q.OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new LeadDto(x.Id, x.Name, x.Email, x.Status, x.Company)).ToListAsync();
    }
    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeadDto>> Get(int id)
    {
        var x = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        return x is null ? NotFound() : new LeadDto(x.Id, x.Name, x.Email, x.Status, x.Company);
    }
    [HttpPost]
    public async Task<ActionResult<LeadDto>> Create(LeadWriteDto dto)
    {
        if (!LeadStatuses.All.Contains(dto.Status)) { ModelState.AddModelError(nameof(dto.Status), "Invalid lead status."); return ValidationProblem(ModelState); }
        var x = new Lead { Name = dto.Name, Email = dto.Email, Company = dto.Company, Status = dto.Status, OwnerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)! };
        db.Leads.Add(x); await db.SaveChangesAsync(); await audit.RecordAsync("Create", "Lead", x.Id.ToString());
        return CreatedAtAction(nameof(Get), new { id = x.Id }, new LeadDto(x.Id, x.Name, x.Email, x.Status, x.Company));
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, LeadWriteDto dto)
    {
        if (!LeadStatuses.All.Contains(dto.Status)) { ModelState.AddModelError(nameof(dto.Status), "Invalid lead status."); return ValidationProblem(ModelState); }
        var x = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (x is null) return NotFound();
        x.Name = dto.Name; x.Email = dto.Email; x.Company = dto.Company; x.Status = dto.Status;
        await db.SaveChangesAsync(); await audit.RecordAsync("Update", "Lead", id.ToString());
        return NoContent();
    }
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var x = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (x is null) return NotFound();
        x.IsActive = false; await db.SaveChangesAsync(); await audit.RecordAsync("Deactivate", "Lead", id.ToString());
        return NoContent();
    }
}
public record LeadDto(int Id, string Name, string? Email, string Status, string? Company);
public record LeadWriteDto([System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(120)] string Name, [System.ComponentModel.DataAnnotations.EmailAddress] string? Email, [System.ComponentModel.DataAnnotations.StringLength(120)] string? Company, [System.ComponentModel.DataAnnotations.Required] string Status);
