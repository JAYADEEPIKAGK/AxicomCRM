using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AcxiomCRM.Models;

public abstract class CrmRecord
{
    public int Id { get; set; }
    [Required]
    public string OwnerUserId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Customer : CrmRecord
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;
    [EmailAddress, StringLength(254)]
    public string? Email { get; set; }
    [Phone, StringLength(30)]
    public string? Phone { get; set; }
    [StringLength(120)]
    public string? Company { get; set; }
    [StringLength(500)]
    public string? Address { get; set; }
    public ICollection<Lead> Leads { get; set; } = new List<Lead>();
}

public class Lead : CrmRecord
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;
    [EmailAddress, StringLength(254)]
    public string? Email { get; set; }
    [Phone, StringLength(30)]
    public string? Phone { get; set; }
    [StringLength(120)]
    public string? Company { get; set; }
    [Required, StringLength(30)]
    public string Status { get; set; } = LeadStatuses.New;
    [StringLength(1000)]
    public string? Notes { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}

public class Opportunity : CrmRecord
{
    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")]
    [Range(0.01, 999999999)]
    public decimal Amount { get; set; }
    [Range(0, 100)]
    public int Probability { get; set; } = 25;
    [Required, StringLength(30)]
    public string Stage { get; set; } = OpportunityStages.Prospecting;
    [DataType(DataType.Date)]
    public DateTime ExpectedCloseDate { get; set; } = DateTime.Today.AddMonths(1);
    public DateTime? ClosedAtUtc { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }
}

public class FollowUp : CrmRecord
{
    [Required, StringLength(160)]
    public string Subject { get; set; } = string.Empty;
    [Required, DataType(DataType.DateTime)]
    public DateTime DueAt { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }
}

public class Activity : CrmRecord
{
    [Required, StringLength(20)]
    public string Type { get; set; } = "Task";
    [Required, StringLength(160)]
    public string Subject { get; set; } = string.Empty;
    [StringLength(2000)]
    public string? Details { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }
    public int? OpportunityId { get; set; }
    public Opportunity? Opportunity { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    [StringLength(450)]
    public string? UserId { get; set; }
    [StringLength(160)]
    public string? UserName { get; set; }
    [Required, StringLength(80)]
    public string Action { get; set; } = string.Empty;
    [StringLength(80)]
    public string? EntityType { get; set; }
    [StringLength(100)]
    public string? EntityId { get; set; }
    [StringLength(1000)]
    public string? Details { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class LeadStatuses
{
    public const string New = "New", Contacted = "Contacted", Qualified = "Qualified", Converted = "Converted", Lost = "Lost";
    public static readonly string[] All = [New, Contacted, Qualified, Converted, Lost];
}

public static class OpportunityStages
{
    public const string Prospecting = "Prospecting", Qualification = "Qualification", Proposal = "Proposal", Negotiation = "Negotiation", Won = "Won", Lost = "Lost";
    public static readonly string[] All = [Prospecting, Qualification, Proposal, Negotiation, Won, Lost];
}
