using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Academia.Api.Lab;
using Academia.Api.Learning;
using Academia.Application.Contracts;
using Academia.Domain.Entities;
using Academia.Infrastructure.Persistence;
using Academia.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var jwt = builder.Configuration.GetSection("Jwt").Get<TokenSettings>() ?? new TokenSettings();
if (Encoding.UTF8.GetByteCount(jwt.Secret) < 32)
    throw new InvalidOperationException("Jwt:Secret must contain at least 32 bytes. Configure a secure secret outside source control.");
// A chave dos appsettings*.json está no repositório: quem a lê pode forjar tokens de qualquer conta.
// Em Production tem de vir de fora (variável de ambiente, .env, cofre) e ser diferente.
if (builder.Environment.IsProduction() && IsVersionedJwtSecret(builder.Configuration, jwt.Secret))
    throw new InvalidOperationException("Jwt:Secret em Production não pode ser a chave versionada nos appsettings*.json. Define Jwt__Secret (ex.: JWT_SECRET no .env do docker-compose) com uma chave aleatória.");

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
    options.Events = new JwtBearerEvents
    {
        // Um token bem assinado de uma conta que já não existe (apagada, ou outra base com a mesma chave) é uma
        // sessão inválida: 401 aqui, em vez de a escrita rebentar mais à frente na chave estrangeira.
        OnTokenValidated = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AcademiaDbContext>();
            var exists = Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                && await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, context.HttpContext.RequestAborted);
            if (!exists) context.Fail("A conta deste token não existe.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<AcademiaDbContext>();

// Atrás de um proxy (nginx no docker-compose da raiz), o IP do cliente vem em X-Forwarded-For. Só se confia no header
// quando a API não está exposta diretamente; senão qualquer cliente o forjava para fugir ao rate limiting por IP.
var trustForwardedHeaders = builder.Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders");
if (trustForwardedHeaders)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear(); // o IP do nginx na rede do Docker não é fixo
        options.KnownProxies.Clear();
    });

// Rate limiting: por IP nas rotas anónimas de auth (força bruta, registo em massa), por conta em /api/learning.
// Os limites vêm de configuração para os testes os poderem ajustar.
var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
var learningPermitLimit = builder.Configuration.GetValue("RateLimiting:LearningPermitLimit", 120);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, ct) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
        new { message = "Demasiados pedidos. Espera um minuto e tenta novamente." }, ct));
    options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitLimit, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("learning", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = learningPermitLimit, Window = TimeSpan.FromMinutes(1) }));
});

// Laboratório de volume (ver backend/lab/LAB.md). Os endpoints são anónimos e destrutivos (apagam índices,
// geram milhões de linhas): nunca podem ficar ligados fora de Development, mesmo por engano de configuração.
var labAtivo = builder.Configuration.LabAtivo();
if (labAtivo && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Lab:Enabled só é permitido em Development: o Lab expõe endpoints anónimos e destrutivos.");
if (labAtivo) builder.Services.AddLab(builder.Configuration);

var app = builder.Build();
if (trustForwardedHeaders) app.UseForwardedHeaders();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
else { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter(); // depois da autenticação, para a política "learning" particionar por conta
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
}).AllowAnonymous().RequireRateLimiting("auth");

auth.MapPost("/login", async (LoginRequest request, AcademiaDbContext db, IPasswordHasher<UserAccount> hasher, TokenSettings settings) =>
{
    var email = request.Email?.Trim().ToLowerInvariant() ?? "";
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
        return Results.Unauthorized();
    return Results.Ok(CreateAuthResponse(user, settings));
}).AllowAnonymous().RequireRateLimiting("auth");

auth.MapGet("/me", async (ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    var id = GetUserId(principal);
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
    return user is null ? Results.NotFound() : Results.Ok(new UserResponse(user.Id, user.DisplayName, user.Email));
}).RequireAuthorization();

var learning = app.MapGroup("/api/learning").WithTags("Aprendizagem").RequireAuthorization().RequireRateLimiting("learning");
learning.MapGet("/snapshot", async (ClaimsPrincipal principal, AcademiaDbContext db) =>
    Results.Ok(await LearningStore.SnapshotAsync(db, GetUserId(principal))));

// Sincronização num só pedido: valida tudo primeiro e grava num único SaveChanges (uma transação: tudo ou nada).
learning.MapPost("/sync", async (SyncRequest request, ClaimsPrincipal principal, AcademiaDbContext db, ILogger<Program> logger) =>
{
    if (LearningStore.Validate(request) is { } error) return Results.BadRequest(new { message = error });
    var userId = GetUserId(principal);
    if (await LearningStore.ApplyAsync(db, userId, request, DateTimeOffset.UtcNow) is { } rejected) return Results.BadRequest(new { message = rejected });
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
    if (await LearningStore.ApplyAsync(db, GetUserId(principal), new SyncRequest(Lessons: [request]), DateTimeOffset.UtcNow) is { } rejected) return Results.BadRequest(new { message = rejected });
    await db.SaveChangesAsync();
    return Results.NoContent();
});

learning.MapPut("/reviews", async (ReviewRequest request, ClaimsPrincipal principal, AcademiaDbContext db) =>
{
    if (LearningStore.Validate(request) is { } error) return Results.BadRequest(new { message = error });
    if (await LearningStore.ApplyAsync(db, GetUserId(principal), new SyncRequest(Reviews: [request]), DateTimeOffset.UtcNow) is { } rejected) return Results.BadRequest(new { message = rejected });
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
    if (await LearningStore.ApplyAsync(db, GetUserId(principal), new SyncRequest(Flashcards: [request]), DateTimeOffset.UtcNow) is { } rejected) return Results.BadRequest(new { message = rejected });
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

static bool IsVersionedJwtSecret(IConfiguration configuration, string secret) =>
    configuration is IConfigurationRoot root && root.Providers.OfType<JsonConfigurationProvider>().Any(provider =>
        provider.Source.Path?.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) == true
        && provider.TryGet("Jwt:Secret", out var versioned) && versioned == secret);
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
