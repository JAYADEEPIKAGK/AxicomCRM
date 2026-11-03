using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Data;

public class CrmDbContext(DbContextOptions<CrmDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        builder.Entity<ApplicationUser>().HasIndex(x => x.ManagerUserId);
        builder.Entity<ApplicationUser>().HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Customer>().HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL AND [Email] <> ''");
        builder.Entity<Customer>().HasIndex(x => x.Phone).IsUnique().HasFilter("[Phone] IS NOT NULL AND [Phone] <> ''");
        builder.Entity<Customer>().HasIndex(x => new { x.OwnerUserId, x.IsActive });
        builder.Entity<Lead>().HasIndex(x => new { x.OwnerUserId, x.Status, x.IsActive });
        builder.Entity<Opportunity>().HasIndex(x => new { x.OwnerUserId, x.Stage, x.IsActive });
        builder.Entity<FollowUp>().HasIndex(x => new { x.OwnerUserId, x.DueAt, x.CompletedAtUtc });
        builder.Entity<AuditLog>().HasIndex(x => x.CreatedAtUtc);
        foreach (var type in new[] { typeof(Customer), typeof(Lead), typeof(Opportunity), typeof(FollowUp), typeof(Activity) })
            builder.Entity(type).HasOne(typeof(ApplicationUser), "Owner").WithMany().HasForeignKey("OwnerUserId").OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Customer>().HasMany(x => x.Leads).WithOne(x => x.Customer).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Opportunity>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Opportunity>().HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.SetNull);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<CrmRecord>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAtUtc = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAtUtc = now;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
