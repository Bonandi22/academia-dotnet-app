using System.Text.Json;
using Academia.Application.Contracts;
using Academia.Domain.Entities;
using Academia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academia.Api.Learning;

/// <summary>
/// Validação e escrita do progresso, partilhadas pelos endpoints individuais e por POST /api/learning/sync.
/// Nada aqui chama SaveChanges: quem chama decide o limite da transação. Todas as consultas filtram por userId.
/// </summary>
public static class LearningStore
{
    // Limites por pedido: o conteúdo tem dezenas de aulas e flashcards; isto só trava abusos e pedidos gigantes.
    public const int MaxItemsPerList = 500;
    public const int MaxQuizAttemptsPerSync = 100;
    // O conteúdo dá poucas dezenas de itens a rever; sem teto, um cliente podia encher a base com itemKeys inventados.
    public const int MaxReviewsPerUser = 300;
    private static readonly string[] ReviewKinds = ["lesson", "quiz", "flashcard"];
    private static readonly string[] Ratings = ["again", "good", "easy"];

    // Limites iguais aos HasMaxLength do AcademiaDbContext.
    public static string? Validate(LessonProgressRequest? r) =>
        r is not null && ValidKey(r.ModuleId, 80) && ValidKey(r.LessonId, 120) ? null : "Identificador de aula inválido.";

    public static string? Validate(ReviewRequest? r) =>
        r is not null && ValidKey(r.ItemKey, 180) && ValidKey(r.ModuleId, 80) && (r.ModuleTitle?.Length ?? 0) <= 180
        && !string.IsNullOrWhiteSpace(r.Title) && r.Title.Trim().Length <= 250 && (r.Reason?.Length ?? 0) <= 1000
        && ReviewKinds.Contains(r.Kind) ? null : "Dados de revisão inválidos.";

    public static string? Validate(QuizAttemptRequest? r) =>
        r is not null && r.Id != Guid.Empty && r.Total is >= 1 and <= 100 && r.Score >= 0 && r.Score <= r.Total
        && r.WrongQuestionIds is not null && r.WrongQuestionIds.Count <= r.Total ? null : "Resultado de quiz inválido.";

    public static string? Validate(FlashcardRatingRequest? r) =>
        r is not null && ValidKey(r.FlashcardId, 160) && Ratings.Contains(r.Rating) ? null : "Avaliação de flashcard inválida.";

    /// <summary>Devolve a primeira falha com o caminho do item (ex.: "reviews[2]: …"), ou null se tudo for válido.</summary>
    public static string? Validate(SyncRequest request)
    {
        var lists = new (string Name, int Count, int Max)[]
        {
            ("deletedReviewKeys", request.DeletedReviewKeys?.Count ?? 0, MaxItemsPerList),
            ("lessons", request.Lessons?.Count ?? 0, MaxItemsPerList),
            ("reviews", request.Reviews?.Count ?? 0, MaxItemsPerList),
            ("quizAttempts", request.QuizAttempts?.Count ?? 0, MaxQuizAttemptsPerSync),
            ("flashcards", request.Flashcards?.Count ?? 0, MaxItemsPerList)
        };
        foreach (var (name, count, max) in lists)
            if (count > max) return $"{name}: no máximo {max} itens por sincronização.";

        return FirstError("deletedReviewKeys", request.DeletedReviewKeys, key => ValidKey(key, 180) ? null : "Chave de revisão inválida.")
            ?? FirstError("lessons", request.Lessons, Validate)
            ?? FirstError("reviews", request.Reviews, Validate)
            ?? FirstError("quizAttempts", request.QuizAttempts, Validate)
            ?? FirstError("flashcards", request.Flashcards, Validate);
    }

    /// <summary>
    /// Regista no change tracker as alterações do pedido (já validado): upsert de aulas, revisões e flashcards,
    /// remoção de revisões apagadas e inserção das tentativas de quiz ainda não gravadas.
    /// Itens repetidos no mesmo pedido: ganha o último (tentativas de quiz: o primeiro, como no servidor).
    /// Devolve uma mensagem de erro, sem registar nada, se o pedido ultrapassar MaxReviewsPerUser.
    /// </summary>
    public static async Task<string?> ApplyAsync(AcademiaDbContext db, Guid userId, SyncRequest request, DateTimeOffset now)
    {
        // A collation do SQL Server não distingue maiúsculas (nem os índices únicos): os dicionários também não podem,
        // senão "Quiz:Q1" e "quiz:q1" dariam duas inserções e um 500 no índice único.
        var deletedKeys = (request.DeletedReviewKeys ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewRequests = request.Reviews ?? [];
        var reviewKeys = deletedKeys.Concat(reviewRequests.Select(r => r.ItemKey)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var reviews = await db.Reviews.Where(x => x.UserId == userId && reviewKeys.Contains(x.ItemKey)).ToDictionaryAsync(x => x.ItemKey, StringComparer.OrdinalIgnoreCase);
        var added = reviewRequests.Select(r => r.ItemKey).Where(k => !deletedKeys.Contains(k) && !reviews.ContainsKey(k)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var removedCount = deletedKeys.Count(reviews.ContainsKey);
        if (added > 0 && await db.Reviews.CountAsync(x => x.UserId == userId) - removedCount + added > MaxReviewsPerUser)
            return $"Limite de {MaxReviewsPerUser} itens de revisão por conta atingido. Limpa os itens concluídos e tenta novamente.";
        foreach (var key in deletedKeys)
            if (reviews.Remove(key, out var removed)) db.Reviews.Remove(removed);
        foreach (var r in reviewRequests)
        {
            if (deletedKeys.Contains(r.ItemKey)) continue; // apagar ganha, como o tombstone no cliente
            if (reviews.TryGetValue(r.ItemKey, out var entity)) entity.UpdatedAt = now;
            else db.Reviews.Add(reviews[r.ItemKey] = entity = new ReviewEntry { UserId = userId, ItemKey = r.ItemKey });
            entity.Title = r.Title.Trim(); entity.ModuleId = r.ModuleId; entity.ModuleTitle = r.ModuleTitle ?? "";
            entity.Kind = r.Kind; entity.Reason = r.Reason ?? ""; entity.IsResolved = r.IsResolved;
        }

        var lessonRequests = request.Lessons ?? [];
        var moduleIds = lessonRequests.Select(l => l.ModuleId).Distinct().ToList();
        var lessons = await db.LessonProgress.Where(x => x.UserId == userId && moduleIds.Contains(x.ModuleId))
            .ToDictionaryAsync(x => LessonKey(x.ModuleId, x.LessonId), StringComparer.OrdinalIgnoreCase);
        foreach (var l in lessonRequests)
        {
            if (lessons.TryGetValue(LessonKey(l.ModuleId, l.LessonId), out var entity)) entity.UpdatedAt = now;
            else db.LessonProgress.Add(lessons[LessonKey(l.ModuleId, l.LessonId)] = entity = new LessonProgress { UserId = userId, ModuleId = l.ModuleId, LessonId = l.LessonId });
            entity.IsCompleted = l.IsCompleted;
        }

        var flashcardRequests = request.Flashcards ?? [];
        var flashcardIds = flashcardRequests.Select(f => f.FlashcardId).Distinct().ToList();
        var flashcards = await db.Flashcards.Where(x => x.UserId == userId && flashcardIds.Contains(x.FlashcardId)).ToDictionaryAsync(x => x.FlashcardId, StringComparer.OrdinalIgnoreCase);
        foreach (var f in flashcardRequests)
        {
            if (flashcards.TryGetValue(f.FlashcardId, out var entity)) entity.UpdatedAt = now;
            else db.Flashcards.Add(flashcards[f.FlashcardId] = entity = new FlashcardProgress { UserId = userId, FlashcardId = f.FlashcardId });
            entity.Rating = f.Rating;
        }

        var attemptRequests = (request.QuizAttempts ?? []).DistinctBy(q => q.Id).ToList();
        var attemptIds = attemptRequests.Select(q => q.Id).ToList();
        var stored = await db.QuizAttempts.Where(x => x.UserId == userId && attemptIds.Contains(x.ClientAttemptId)).Select(x => x.ClientAttemptId).ToListAsync();
        foreach (var q in attemptRequests.Where(q => !stored.Contains(q.Id)))
            db.QuizAttempts.Add(NewQuizAttempt(userId, q, now));
        return null;
    }

    /// <summary>Tentativa nova a partir do pedido: a data é a do quiz, salvo se vier no futuro.</summary>
    public static QuizAttempt NewQuizAttempt(Guid userId, QuizAttemptRequest r, DateTimeOffset now) => new()
    {
        UserId = userId, ClientAttemptId = r.Id, Score = r.Score, Total = r.Total,
        WrongQuestionIdsJson = JsonSerializer.Serialize(r.WrongQuestionIds.Distinct().Take(r.Total)),
        CompletedAt = r.CompletedAt is { } at && at <= now ? at : now
    };

    public static async Task<LearningSnapshotResponse> SnapshotAsync(AcademiaDbContext db, Guid userId)
    {
        var lessons = await db.LessonProgress.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new LessonProgressResponse(x.ModuleId, x.LessonId, x.IsCompleted, x.UpdatedAt)).ToListAsync();
        var reviews = await db.Reviews.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.UpdatedAt)
            .Select(x => new ReviewResponse(x.ItemKey, x.Title, x.ModuleId, x.ModuleTitle, x.Kind, x.Reason, x.IsResolved, x.CreatedAt, x.UpdatedAt)).ToListAsync();
        var quizRows = await db.QuizAttempts.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CompletedAt).Take(50)
            .Select(x => new { x.ClientAttemptId, x.Score, x.Total, x.WrongQuestionIdsJson, x.CompletedAt }).ToListAsync();
        var quizzes = quizRows.Select(x => new QuizAttemptResponse(x.ClientAttemptId, x.Score, x.Total, ParseWrongQuestionIds(x.WrongQuestionIdsJson), x.CompletedAt)).ToList();
        var flashcards = await db.Flashcards.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new FlashcardResponse(x.FlashcardId, x.Rating, x.UpdatedAt)).ToListAsync();
        return new LearningSnapshotResponse(lessons, reviews, quizzes, flashcards);
    }

    public static async Task<QuizAttemptResponse?> FindQuizAttemptAsync(AcademiaDbContext db, Guid userId, Guid clientAttemptId)
    {
        var row = await db.QuizAttempts.AsNoTracking().Where(x => x.UserId == userId && x.ClientAttemptId == clientAttemptId)
            .Select(x => new { x.Score, x.Total, x.WrongQuestionIdsJson, x.CompletedAt }).SingleOrDefaultAsync();
        return row is null ? null : new QuizAttemptResponse(clientAttemptId, row.Score, row.Total, ParseWrongQuestionIds(row.WrongQuestionIdsJson), row.CompletedAt);
    }

    public static bool ValidKey(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Contains('\\') && !value.Any(char.IsControl);

    // ValidKey rejeita caracteres de controlo, por isso "\n" separa sem ambiguidade.
    private static string LessonKey(string moduleId, string lessonId) => $"{moduleId}\n{lessonId}";

    private static List<string> ParseWrongQuestionIds(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static string? FirstError<T>(string listName, IReadOnlyList<T>? items, Func<T, string?> validate)
    {
        for (var i = 0; i < (items?.Count ?? 0); i++)
            if (validate(items![i]) is { } error) return $"{listName}[{i}]: {error}";
        return null;
    }
}
