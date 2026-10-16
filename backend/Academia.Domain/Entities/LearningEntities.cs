namespace Academia.Domain.Entities;

public sealed class LessonProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string ModuleId { get; set; } = string.Empty;
    public string LessonId { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public UserAccount? User { get; set; }
}

public sealed class ReviewEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string ItemKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string ModuleTitle { get; set; } = string.Empty;
    public string Kind { get; set; } = "lesson";
    public string Reason { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public UserAccount? User { get; set; }
}

public sealed class QuizAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public int Score { get; set; }
    public int Total { get; set; }
    public string WrongQuestionIdsJson { get; set; } = "[]";
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public UserAccount? User { get; set; }
}

public sealed class FlashcardProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string FlashcardId { get; set; } = string.Empty;
    public string Rating { get; set; } = "again";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public UserAccount? User { get; set; }
}
