using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Academia.Api.Lab;
using Academia.Api.Learning;
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
    Results.Ok(await LearningStore.SnapshotAsync(db, GetUserId(principal))));

// Sincronização num só pedido: valida tudo primeiro e grava num único SaveChanges (uma transação: tudo ou nada).
learning.MapPost("/sync", async (SyncRequest request, ClaimsPrincipal principal, AcademiaDbContext db, ILogger<Program> logger) =>
{
    if (LearningStore.Validate(request) is { } error) return Results.BadRequest(new { message = error });
    var userId = GetUserId(principal);
    await LearningStore.ApplyAsync(db, userId, request, DateTimeOffset.UtcNow);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException ex)
    {
        // Só acontece com duas sincronizações da mesma conta a inserir a mesma chave em simultâneo.
        logger.LogWarning(ex, "Sincronização concorrente rejeitada para o utilizador {UserId}.", userId);
        return Results.Conflict(new { message = "Outra sincronização desta conta estava a decorrer. Tenta novamente." });
    }
    return Results.Ok(await LearningStore.SnapshotAsync(db, userId));
});

learning.MapPut("/lessons", async (LessonProgressRequest request, ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    if (LearningStore.Validate(request) is { } error) return Results.BadRequest(new { message = error });
    await LearningStore.ApplyAsync(db, GetUserId(principal), new SyncRequest(Lessons: [request]), DateTimeOffset.UtcNow);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

learning.MapPut("/reviews", async (ReviewRequest request, ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    if (LearningStore.Validate(request) is { } error) return Results.BadRequest(new { message = error });
    await LearningStore.ApplyAsync(db, GetUserId(principal), new SyncRequest(Reviews: [request]), DateTimeOffset.UtcNow);
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

learning.MapPost("/quiz-attempts", async (QuizAttemptRequest request, ClaimsPrincipal principal, AcademiaDbContext db, ILogger<Program> logger) =>
{
    if (LearningStore.Validate(request) is { } error) return Results.BadRequest(new { message = error });
    var userId = GetUserId(principal);
    // Idempotente pelo id do cliente: reenviar (retry, sync repetido) devolve a tentativa já gravada.
    var existing = await LearningStore.FindQuizAttemptAsync(db, userId, request.Id);
    if (existing is not null) return Results.Ok(existing);

    var attempt = LearningStore.NewQuizAttempt(userId, request, DateTimeOffset.UtcNow);
    db.QuizAttempts.Add(attempt);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException ex)
    {
        // Dois envios do mesmo id em simultâneo: o índice único (UserId, ClientAttemptId) trava o segundo.
        logger.LogInformation(ex, "Tentativa de quiz {AttemptId} já gravada por um pedido concorrente.", request.Id);
        db.ChangeTracker.Clear();
        existing = await LearningStore.FindQuizAttemptAsync(db, userId, request.Id);
        if (existing is null) throw;
        return Results.Ok(existing);
    }
    return Results.Created($"/api/learning/quiz-attempts/{request.Id}", await LearningStore.FindQuizAttemptAsync(db, userId, request.Id));
});

learning.MapPut("/flashcards", async (FlashcardRatingRequest request, ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    if (LearningStore.Validate(request) is { } error) return Results.BadRequest(new { message = error });
    await LearningStore.ApplyAsync(db, GetUserId(principal), new SyncRequest(Flashcards: [request]), DateTimeOffset.UtcNow);
    await db.SaveChangesAsync();
    return Results.NoContent();
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
