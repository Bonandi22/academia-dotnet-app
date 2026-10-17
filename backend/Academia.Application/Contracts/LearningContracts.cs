namespace Academia.Application.Contracts;

public sealed record RegisterRequest(string DisplayName, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);
public sealed record UserResponse(Guid Id, string DisplayName, string Email);
public sealed record LessonProgressRequest(string ModuleId, string LessonId, bool IsCompleted);
public sealed record ReviewRequest(string ItemKey, string Title, string ModuleId, string ModuleTitle, string Kind, string Reason, bool IsResolved = false);
/// <param name="Id">Gerado pelo cliente; reenviar o mesmo id não cria outra tentativa.</param>
/// <param name="CompletedAt">Quando o quiz foi feito (pode ser antes da sincronização); omitido ou no futuro passa a "agora".</param>
public sealed record QuizAttemptRequest(Guid Id, int Score, int Total, IReadOnlyList<string> WrongQuestionIds, DateTimeOffset? CompletedAt = null);
public sealed record FlashcardRatingRequest(string FlashcardId, string Rating);
/// <summary>Todo o progresso local de uma vez; aplicado numa única transação (tudo ou nada). Listas omitidas contam como vazias.</summary>
public sealed record SyncRequest(
    IReadOnlyList<string>? DeletedReviewKeys = null,
    IReadOnlyList<LessonProgressRequest>? Lessons = null,
    IReadOnlyList<ReviewRequest>? Reviews = null,
    IReadOnlyList<QuizAttemptRequest>? QuizAttempts = null,
    IReadOnlyList<FlashcardRatingRequest>? Flashcards = null);
public sealed record LearningSnapshotResponse(IReadOnlyList<LessonProgressResponse> Lessons, IReadOnlyList<ReviewResponse> Reviews, IReadOnlyList<QuizAttemptResponse> QuizAttempts, IReadOnlyList<FlashcardResponse> Flashcards);
public sealed record LessonProgressResponse(string ModuleId, string LessonId, bool IsCompleted, DateTimeOffset UpdatedAt);
public sealed record ReviewResponse(string ItemKey, string Title, string ModuleId, string ModuleTitle, string Kind, string Reason, bool IsResolved, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record QuizAttemptResponse(Guid Id, int Score, int Total, IReadOnlyList<string> WrongQuestionIds, DateTimeOffset CompletedAt);
public sealed record FlashcardResponse(string FlashcardId, string Rating, DateTimeOffset UpdatedAt);
