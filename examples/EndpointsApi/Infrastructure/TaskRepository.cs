using System.Collections.Concurrent;
using UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

namespace UnambitiousFx.Examples.EndpointsApi.Infrastructure;

/// <summary>
///     In-memory repository for tasks (for demo purposes). In a real application this would be
///     replaced with EF Core or another ORM.
/// </summary>
public sealed class TaskRepository
{
    private readonly ConcurrentDictionary<Guid, TaskDto> _tasks = new();

    /// <summary>Creates a new task with the given title.</summary>
    /// <param name="title">The task's title.</param>
    /// <returns>The created task.</returns>
    public TaskDto Create(string title)
    {
        var task = new TaskDto { Id = Guid.NewGuid(), Title = title };
        _tasks[task.Id] = task;
        return task;
    }

    /// <summary>Gets a task by its id.</summary>
    /// <param name="id">The task's id.</param>
    /// <returns>The task, or <see langword="null" /> when no task has that id.</returns>
    public TaskDto? GetById(Guid id)
    {
        return _tasks.TryGetValue(id, out var task) ? task : null;
    }

    /// <summary>Gets every task.</summary>
    /// <returns>Every task currently stored.</returns>
    public IReadOnlyList<TaskDto> GetAll()
    {
        return _tasks.Values.ToList();
    }

    /// <summary>Updates a task's title.</summary>
    /// <param name="id">The task's id.</param>
    /// <param name="title">The task's new title.</param>
    /// <returns><see langword="true" /> when the task existed and was updated.</returns>
    public bool Update(Guid id, string title)
    {
        if (!_tasks.TryGetValue(id, out var existing))
        {
            return false;
        }

        _tasks[id] = existing with { Title = title };
        return true;
    }

    /// <summary>Deletes a task.</summary>
    /// <param name="id">The task's id.</param>
    /// <returns><see langword="true" /> when the task existed and was removed.</returns>
    public bool Delete(Guid id)
    {
        return _tasks.TryRemove(id, out _);
    }
}
