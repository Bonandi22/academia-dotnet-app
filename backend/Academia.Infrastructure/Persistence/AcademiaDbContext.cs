using Academia.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Academia.Infrastructure.Persistence;

public sealed class AcademiaDbContext(DbContextOptions<AcademiaDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();
    public DbSet<ReviewEntry> Reviews => Set<ReviewEntry>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<FlashcardProgress> Flashcards => Set<FlashcardProgress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Email).HasMaxLength(320).IsRequired();
            b.HasIndex(x => x.Email).IsUnique(); b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
            b.Property(x => x.PasswordHash).IsRequired();
        });
        modelBuilder.Entity<LessonProgress>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.ModuleId).HasMaxLength(80); b.Property(x => x.LessonId).HasMaxLength(120);
            b.HasIndex(x => new { x.UserId, x.ModuleId, x.LessonId }).IsUnique();
            b.HasOne(x => x.User).WithMany(x => x.Lessons).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ReviewEntry>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.ItemKey).HasMaxLength(180); b.Property(x => x.Title).HasMaxLength(250);
            b.Property(x => x.ModuleId).HasMaxLength(80); b.Property(x => x.ModuleTitle).HasMaxLength(180);
            b.Property(x => x.Kind).HasMaxLength(30); b.Property(x => x.Reason).HasMaxLength(1000);
            b.HasIndex(x => new { x.UserId, x.ItemKey }).IsUnique();
            b.HasOne(x => x.User).WithMany(x => x.Reviews).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<QuizAttempt>(b => {
            b.HasKey(x => x.Id); b.HasIndex(x => new { x.UserId, x.CompletedAt });
            b.HasOne(x => x.User).WithMany(x => x.QuizAttempts).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FlashcardProgress>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.FlashcardId).HasMaxLength(160); b.Property(x => x.Rating).HasMaxLength(20);
            b.HasIndex(x => new { x.UserId, x.FlashcardId }).IsUnique();
            b.HasOne(x => x.User).WithMany(x => x.Flashcards).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
