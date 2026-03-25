using System.Collections.Concurrent;

namespace UnambitiousFx.Examples.GettingStarted.Domain;

/// <summary>
/// In-memory repository for tasks (for demo purposes)
/// </summary>
public sealed class TaskRepository
{
    private readonly ConcurrentDictionary<Guid, Task> _tasks = new();

    public Task Create(string title, string description)
    {
        var task = new Task(Guid.NewGuid(), title, description);
        _tasks.TryAdd(task.Id, task);
        return task;
    }

    public Task? GetById(Guid id)
    {
        _tasks.TryGetValue(id, out var task);
        return task;
    }

    public List<Task> GetAll()
    {
        return _tasks.Values.ToList();
    }

    public void Delete(Guid id)
    {
        _tasks.TryRemove(id, out _);
    }

    public void Clear()
    {
        _tasks.Clear();
    }
}
