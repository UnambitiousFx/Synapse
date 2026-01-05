using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

public sealed class ManualRegisterGroup : IRegisterGroup
{
    public void Register(IDependencyInjectionBuilder builder)
    {
        builder.RegisterRequestHandler<TodoQueryHandler, TodoQuery, Todo>();
        builder.RegisterRequestHandler<DeleteTodoCommandHandler, DeleteTodoCommand>();
        builder.RegisterEventHandler<TodoCreatedHandler, TodoCreated>();
    }
}