using System.Collections.Concurrent;

namespace UnambitiousFx.Examples.WebApi.Infrastructure;

/// <summary>
/// In-memory repository for tasks (for demo purposes)
/// In a real application, this would be replaced with EF Core or another ORM
/// </summary>
public sealed class TaskRepository
{
    private readonly ConcurrentDictionary<Guid, TaskEntity> _tasks = new();

    public TaskEntity Create(string title, string description)
    {
        var task = new TaskEntity(Guid.NewGuid(), title, description);
        _tasks.TryAdd(task.Id, task);
        return task;
    }

    public TaskEntity? GetById(Guid id)
    {
        _tasks.TryGetValue(id, out var task);
        return task;
    }

    public List<TaskEntity> GetAll()
    {
        return _tasks.Values.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public bool Update(Guid id, string title, string description)
    {
        if (!_tasks.TryGetValue(id, out var task))
            return false;

        task.Update(title, description);
        return true;
    }

    public bool Complete(Guid id)
    {
        if (!_tasks.TryGetValue(id, out var task))
            return false;

        task.Complete();
        return true;
    }

    public bool Delete(Guid id)
    {
        return _tasks.TryRemove(id, out _);
    }

    public void Clear()
    {
        _tasks.Clear();
    }
}

/// <summary>
/// Domain entity representing a task
/// </summary>
public sealed class TaskEntity
{
    public Guid Id { get; }
    public string Title { get; private set; }
    public string Description { get; private set; }
    public TaskStatus Status { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime? CompletedAt { get; private set; }

    public TaskEntity(Guid id, string title, string description)
    {
        Id = id;
        Title = title;
        Description = description;
        Status = TaskStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public void Complete()
    {
        Status = TaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }
}

public enum TaskStatus
{
    Pending,
    InProgress,
    Completed
}
