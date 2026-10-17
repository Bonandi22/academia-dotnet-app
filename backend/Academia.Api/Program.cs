using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Academia.Api.Lab;
using Academia.Application.Contracts;
using Academia.Domain.Entities;
using Academia.Infrastructure.Persistence;
using Academia.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var jwt = builder.Configuration.GetSection("Jwt").Get<TokenSettings>() ?? new TokenSettings();
if (Encoding.UTF8.GetByteCount(jwt.Secret) < 32)
    throw new InvalidOperationException("Jwt:Secret must contain at least 32 bytes. Configure a secure secret outside source control.");

builder.Services.AddDbContext<AcademiaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Academia")));
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddSingleton(jwt);
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"])
          .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<AcademiaDbContext>();

// Laboratório de volume (só em Development; ver backend/lab/LAB.md).
var labAtivo = builder.Configuration.LabAtivo();
if (labAtivo) builder.Services.AddLab(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
if (labAtivo) app.MapLabEndpoints();
app.MapGet("/api", () => Results.Ok(new { name = "Academia .NET API", version = "1.0.0" }));

var auth = app.MapGroup("/api/auth").WithTags("Autenticação");
auth.MapPost("/register", async (RegisterRequest request, AcademiaDbContext db, IPasswordHasher<UserAccount> hasher, TokenSettings settings) =>
{
    var name = request.DisplayName?.Trim() ?? "";
    var email = request.Email?.Trim().ToLowerInvariant() ?? "";
    var password = request.Password ?? "";
    var errors = new Dictionary<string, string[]>();
    if (name.Length is < 2 or > 100) errors["displayName"] = ["O nome deve ter entre 2 e 100 caracteres."];
    if (!IsValidEmail(email)) errors["email"] = ["Indica um email válido."];
    if (password.Length is < 10 or > 128) errors["password"] = ["A palavra-passe deve ter entre 10 e 128 caracteres."];
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    if (await db.Users.AnyAsync(u => u.Email == email)) return Results.Conflict(new { message = "Já existe uma conta com este email." });
    var user = new UserAccount { Email = email, DisplayName = name };
    user.PasswordHash = hasher.HashPassword(user, password);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Created("/api/auth/me", CreateAuthResponse(user, settings));
}).AllowAnonymous();

auth.MapPost("/login", async (LoginRequest request, AcademiaDbContext db, IPasswordHasher<UserAccount> hasher, TokenSettings settings) =>
{
    var email = request.Email?.Trim().ToLowerInvariant() ?? "";
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
        return Results.Unauthorized();
    return Results.Ok(CreateAuthResponse(user, settings));
}).AllowAnonymous();

auth.MapGet("/me", async (ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    var id = GetUserId(principal);
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
    return user is null ? Results.NotFound() : Results.Ok(new UserResponse(user.Id, user.DisplayName, user.Email));
}).RequireAuthorization();

var learning = app.MapGroup("/api/learning").WithTags("Aprendizagem").RequireAuthorization();
learning.MapGet("/snapshot", async (ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    var userId = GetUserId(principal);
    var lessons = await db.LessonProgress.AsNoTracking().Where(x => x.UserId == userId)
        .Select(x => new LessonProgressResponse(x.ModuleId, x.LessonId, x.IsCompleted, x.UpdatedAt)).ToListAsync();
    var reviews = await db.Reviews.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.UpdatedAt)
        .Select(x => new ReviewResponse(x.ItemKey, x.Title, x.ModuleId, x.ModuleTitle, x.Kind, x.Reason, x.IsResolved, x.CreatedAt, x.UpdatedAt)).ToListAsync();
        
    var quizRows = await db.QuizAttempts
        .AsNoTracking()
        .Where(x => x.UserId == userId)
        .OrderByDescending(x => x.CompletedAt)
        .Take(50)
        .Select(x => new
        {
            x.Id,
            x.Score,
            x.Total,
            x.WrongQuestionIdsJson,
            x.CompletedAt
        })
        .ToListAsync();

    var quizzes = quizRows
        .Select(x => new QuizAttemptResponse(
            x.Id,
            x.Score,
            x.Total,
            JsonSerializer.Deserialize<List<string>>(
                x.WrongQuestionIdsJson,
                (JsonSerializerOptions?)null
            ) ?? new List<string>(),
            x.CompletedAt
        ))
        .ToList();

    var flashcards = await db.Flashcards.AsNoTracking().Where(x => x.UserId == userId)
        .Select(x => new FlashcardResponse(x.FlashcardId, x.Rating, x.UpdatedAt)).ToListAsync();
    return Results.Ok(new LearningSnapshotResponse(lessons, reviews, quizzes, flashcards));
});

learning.MapPut("/lessons", async (LessonProgressRequest request, ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    if (!ValidKey(request.ModuleId, 80) || !ValidKey(request.LessonId, 120)) return Results.BadRequest(new { message = "Identificador de aula inválido." });
    var userId = GetUserId(principal);
    var entity = await db.LessonProgress.SingleOrDefaultAsync(x => x.UserId == userId && x.ModuleId == request.ModuleId && x.LessonId == request.LessonId);
    if (entity is null) db.LessonProgress.Add(new LessonProgress { UserId = userId, ModuleId = request.ModuleId, LessonId = request.LessonId, IsCompleted = request.IsCompleted });
    else { entity.IsCompleted = request.IsCompleted; entity.UpdatedAt = DateTimeOffset.UtcNow; }
    await db.SaveChangesAsync();
    return Results.NoContent();
});

learning.MapPut("/reviews", async (ReviewRequest request, ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    // Limites iguais aos HasMaxLength de ReviewEntry no AcademiaDbContext.
    if (!ValidKey(request.ItemKey, 180) || !ValidKey(request.ModuleId, 80) || (request.ModuleTitle?.Length ?? 0) > 180
        || string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250 || (request.Reason?.Length ?? 0) > 1000
        || !new[] { "lesson", "quiz", "flashcard" }.Contains(request.Kind))
        return Results.BadRequest(new { message = "Dados de revisão inválidos." });
    var userId = GetUserId(principal);
    var moduleTitle = request.ModuleTitle ?? "";
    var entity = await db.Reviews.SingleOrDefaultAsync(x => x.UserId == userId && x.ItemKey == request.ItemKey);
    if (entity is null)
        db.Reviews.Add(new ReviewEntry { UserId = userId, ItemKey = request.ItemKey, Title = request.Title.Trim(), ModuleId = request.ModuleId, ModuleTitle = moduleTitle, Kind = request.Kind, Reason = request.Reason ?? "", IsResolved = request.IsResolved });
    else { entity.Title = request.Title.Trim(); entity.ModuleId = request.ModuleId; entity.ModuleTitle = moduleTitle; entity.Kind = request.Kind; entity.Reason = request.Reason ?? ""; entity.IsResolved = request.IsResolved; entity.UpdatedAt = DateTimeOffset.UtcNow; }
    await db.SaveChangesAsync();
    return Results.NoContent();
});

learning.MapDelete("/reviews/{itemKey}", async (string itemKey, ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    var userId = GetUserId(principal);
    var entity = await db.Reviews.SingleOrDefaultAsync(x => x.UserId == userId && x.ItemKey == itemKey);
    if (entity is null) return Results.NotFound();
    db.Reviews.Remove(entity); await db.SaveChangesAsync(); return Results.NoContent();
});

learning.MapPost("/quiz-attempts", async (QuizAttemptRequest request, ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    if (request.Total < 1 || request.Total > 100 || request.Score < 0 || request.Score > request.Total || request.WrongQuestionIds is null || request.WrongQuestionIds.Count > request.Total)
        return Results.BadRequest(new { message = "Resultado de quiz inválido." });
    var attempt = new QuizAttempt { UserId = GetUserId(principal), Score = request.Score, Total = request.Total, WrongQuestionIdsJson = JsonSerializer.Serialize(request.WrongQuestionIds.Distinct().Take(request.Total)) };
    db.QuizAttempts.Add(attempt); await db.SaveChangesAsync();
    return Results.Created($"/api/learning/quiz-attempts/{attempt.Id}", new QuizAttemptResponse(attempt.Id, attempt.Score, attempt.Total, request.WrongQuestionIds, attempt.CompletedAt));
});

learning.MapPut("/flashcards", async (FlashcardRatingRequest request, ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    if (!ValidKey(request.FlashcardId, 160) || !new[] { "again", "good", "easy" }.Contains(request.Rating)) return Results.BadRequest(new { message = "Avaliação de flashcard inválida." });
    var userId = GetUserId(principal);
    var card = await db.Flashcards.SingleOrDefaultAsync(x => x.UserId == userId && x.FlashcardId == request.FlashcardId);
    if (card is null) db.Flashcards.Add(new FlashcardProgress { UserId = userId, FlashcardId = request.FlashcardId, Rating = request.Rating });
    else { card.Rating = request.Rating; card.UpdatedAt = DateTimeOffset.UtcNow; }
    await db.SaveChangesAsync(); return Results.NoContent();
});

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AcademiaDbContext>();
    // Aplica as migrations pendentes (Academia.Infrastructure/Persistence/Migrations). Uma base criada
    // pelo antigo EnsureCreated não tem __EFMigrationsHistory e falha aqui: é preciso recriá-la.
    await db.Database.MigrateAsync();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Could not apply SQL Server migrations. Check the connection string, ensure SQL Server is running, and recreate databases created before migrations existed.");
}
if (labAtivo) await app.Services.InitializeLabAsync(app.Logger);
app.Run();

static Guid GetUserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());
static bool ValidKey(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Contains('\\');
static bool IsValidEmail(string email) => email.Length <= 320 && System.Net.Mail.MailAddress.TryCreate(email, out var parsed) && parsed is not null && parsed.Address.Equals(email, StringComparison.OrdinalIgnoreCase);
static AuthResponse CreateAuthResponse(UserAccount user, TokenSettings settings)
{
    var now = DateTimeOffset.UtcNow;
    var expires = now.AddMinutes(Math.Clamp(settings.ExpirationMinutes, 5, 1440));
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Name, user.DisplayName) };
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret));
    var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, now.UtcDateTime, expires.UtcDateTime,
        new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, new UserResponse(user.Id, user.DisplayName, user.Email));
}

// Torna o Program visível ao WebApplicationFactory<Program> dos testes de integração.
public partial class Program;
