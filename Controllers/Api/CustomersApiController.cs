using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace AcxiomCRM.Controllers.Api;

[ApiController, Route("api/customers"), Authorize]
public class CustomersApiController(CrmDbContext db, IAuditService audit) : ControllerBase
{
    private IQueryable<Customer> Scope() => db.Customers.AccessibleTo(User, db.Users);
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomerDto>>> Get(string? search, int page = 1, int pageSize = 50)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = Scope().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || (x.Company != null && x.Company.Contains(search)));
        return await q.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CustomerDto(x.Id, x.Name, x.Email, x.Phone, x.Company)).ToListAsync();
    }
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerDto>> Get(int id)
    {
        var x = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        return x is null ? NotFound() : new CustomerDto(x.Id, x.Name, x.Email, x.Phone, x.Company);
    }
    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create(CustomerWriteDto dto)
    {
        var entity = new Customer { Name = dto.Name, Email = dto.Email, Phone = dto.Phone, Company = dto.Company, OwnerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)! };
        db.Customers.Add(entity);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqlException { Number: 2601 or 2627 }) { return Conflict(new { message = "A customer with this email or phone already exists." }); }
        await audit.RecordAsync("Create", "Customer", entity.Id.ToString());
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, new CustomerDto(entity.Id, entity.Name, entity.Email, entity.Phone, entity.Company));
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CustomerWriteDto dto)
    {
        var entity = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (entity is null) return NotFound();
        entity.Name = dto.Name; entity.Email = dto.Email; entity.Phone = dto.Phone; entity.Company = dto.Company;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqlException { Number: 2601 or 2627 }) { return Conflict(new { message = "A customer with this email or phone already exists." }); }
        await audit.RecordAsync("Update", "Customer", id.ToString());
        return NoContent();
    }
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await Scope().FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (entity is null) return NotFound();
        entity.IsActive = false; await db.SaveChangesAsync(); await audit.RecordAsync("Deactivate", "Customer", id.ToString());
        return NoContent();
    }
}

public record CustomerDto(int Id, string Name, string? Email, string? Phone, string? Company);
public record CustomerWriteDto([System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(120)] string Name, [System.ComponentModel.DataAnnotations.EmailAddress] string? Email, [System.ComponentModel.DataAnnotations.Phone] string? Phone, [System.ComponentModel.DataAnnotations.StringLength(120)] string? Company);
