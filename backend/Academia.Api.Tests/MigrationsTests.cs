using Academia.Domain.Entities;
using Academia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Academia.Api.Tests;

/// <summary>Migrations de dados testadas numa base própria, para poder parar a meio do histórico.</summary>
public sealed class MigrationsTests : IAsyncLifetime
{
    private readonly AcademiaDbContext db = new(new DbContextOptionsBuilder<AcademiaDbContext>()
        .UseSqlServer(ApiFactory.NewTestDatabase()).Options);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await db.Database.EnsureDeletedAsync();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Chaves_de_aula_por_indice_passam_a_id_de_seccao_e_o_down_reverte()
    {
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("InitialCreate");
        var user = new UserAccount { Email = "migracao@exemplo.pt", DisplayName = "Migração", PasswordHash = "x" };
        db.Users.Add(user);
        db.LessonProgress.AddRange(
            new LessonProgress { UserId = user.Id, ModuleId = "csharp", LessonId = "0", IsCompleted = true },
            new LessonProgress { UserId = user.Id, ModuleId = "data", LessonId = "3", IsCompleted = false },
            new LessonProgress { UserId = user.Id, ModuleId = "csharp", LessonId = "99", IsCompleted = true });
        db.Reviews.AddRange(
            new ReviewEntry { UserId = user.Id, ItemKey = "lesson:aspnet:1", Title = "Aula", ModuleId = "aspnet", Kind = "lesson" },
            new ReviewEntry { UserId = user.Id, ItemKey = "quiz:q1", Title = "Quiz", ModuleId = "csharp", Kind = "quiz" });
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();
        var lessonsUp = await LessonKeysAsync();
        var reviewsUp = await ReviewKeysAsync();
        await migrator.MigrateAsync("InitialCreate");
        var lessonsDown = await LessonKeysAsync();
        var reviewsDown = await ReviewKeysAsync();

        // "99" não existe no mapeamento: fica como estava em vez de ser apagado ou inventado.
        Assert.Equal(["csharp::99", "csharp::async-await", "data::transactions-concurrency"], lessonsUp);
        Assert.Equal(["lesson:aspnet:authn-vs-authz", "quiz:q1"], reviewsUp);
        Assert.Equal(["csharp::0", "csharp::99", "data::3"], lessonsDown);
        Assert.Equal(["lesson:aspnet:1", "quiz:q1"], reviewsDown);
    }

    private Task<List<string>> LessonKeysAsync() => db.LessonProgress.AsNoTracking()
        .Select(x => x.ModuleId + "::" + x.LessonId).OrderBy(x => x).ToListAsync();

    private Task<List<string>> ReviewKeysAsync() => db.Reviews.AsNoTracking()
        .Select(x => x.ItemKey).OrderBy(x => x).ToListAsync();
}
