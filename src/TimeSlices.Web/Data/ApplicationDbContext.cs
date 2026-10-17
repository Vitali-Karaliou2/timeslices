using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TimeSlices.Web.Data;

/// <summary>
/// В БД — только то, что относится к пользователю. Треки, срезы и примеры читаются
/// из файлов содержания (docs/spec/shell-spec.md).
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<UserSliceState> UserSliceStates => Set<UserSliceState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserSliceState>(e =>
        {
            e.HasIndex(s => new { s.UserId, s.SliceId, s.TrackId }).IsUnique();
            e.Property(s => s.UserId).IsRequired();
            e.Property(s => s.SliceId).IsRequired().HasMaxLength(100);
            e.Property(s => s.TrackId).IsRequired().HasMaxLength(100);
            e.Property(s => s.State).HasConversion<string>().HasMaxLength(20);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
