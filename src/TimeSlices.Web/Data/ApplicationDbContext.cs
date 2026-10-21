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
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<Answer> Answers => Set<Answer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserSliceState>(e =>
        {
            e.HasIndex(s => new { s.UserId, s.SliceId }).IsUnique();
            e.Property(s => s.UserId).IsRequired();
            e.Property(s => s.SliceId).IsRequired().HasMaxLength(100);
            e.Property(s => s.State).HasConversion<string>().HasMaxLength(20);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Attempt>(e =>
        {
            e.HasIndex(a => new { a.UserId, a.SliceId }, "IX_Attempts_UserId_SliceId");
            // Не больше одной незавершённой попытки на срез: двойное «Начать» не создаёт вторую.
            e.HasIndex(a => new { a.UserId, a.SliceId }, "IX_Attempts_Open").IsUnique()
                .HasFilter("\"Status\" IN ('InProgress', 'Submitted')");
            e.HasIndex(a => new { a.Status, a.SubmittedAt });
            e.Property(a => a.UserId).IsRequired();
            e.Property(a => a.SliceId).IsRequired().HasMaxLength(100);
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.ExaminerComment).HasMaxLength(4000);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.ExaminerId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(a => a.Answers).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Answer>(e =>
        {
            // Ответ на вопрос в попытке — один; сохранённый ответ не меняется.
            e.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
            e.Property(x => x.QuestionId).IsRequired().HasMaxLength(100);
            e.Property(x => x.Text).IsRequired().HasMaxLength(Assessment.SelfCheckService.MaxAnswerLength);
            e.Property(x => x.FollowUpText).HasMaxLength(Assessment.SelfCheckService.MaxAnswerLength);
            e.Property(x => x.SelfRating).HasConversion<string>().HasMaxLength(20);
        });
    }
}
