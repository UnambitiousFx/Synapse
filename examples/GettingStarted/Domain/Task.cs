namespace UnambitiousFx.Examples.GettingStarted.Domain;

/// <summary>
///     Domain entity representing a task
/// </summary>
public sealed class Task
{
    public Task(Guid id, string title, string description)
    {
        Id = id;
        Title = title;
        Description = description;
        Status = TaskStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; }
    public string Title { get; private set; }
    public string Description { get; private set; }
    public TaskStatus Status { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime? CompletedAt { get; private set; }

    public void Complete()
    {
        Status = TaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void UpdateTitle(string title)
    {
        Title = title;
    }

    public void UpdateDescription(string description)
    {
        Description = description;
    }
}

public enum TaskStatus
{
    Pending,
    InProgress,
    Completed
}