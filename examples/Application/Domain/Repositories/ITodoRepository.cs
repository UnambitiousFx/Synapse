using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Functional;

namespace UnambitiousFx.Examples.Application.Domain.Repositories;

public interface ITodoRepository
{
    ValueTask CreateAsync(Todo todo,
        CancellationToken cancellationToken = default);

    ValueTask UpdateAsync(Todo todo,
        CancellationToken cancellationToken = default);

    ValueTask<Maybe<Todo>> GetAsync(Guid id,
        CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<Todo>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(Guid id,
        CancellationToken cancellationToken);
}