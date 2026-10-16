namespace Academia.Domain.Entities;

public sealed class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<LessonProgress> Lessons { get; set; } = new List<LessonProgress>();
    public ICollection<ReviewEntry> Reviews { get; set; } = new List<ReviewEntry>();
    public ICollection<QuizAttempt> QuizAttempts { get; set; } = new List<QuizAttempt>();
    public ICollection<FlashcardProgress> Flashcards { get; set; } = new List<FlashcardProgress>();
}
