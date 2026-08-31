using UnambitiousFx.Examples.EndpointsApi.Features.Tasks;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Contracts;

/// <summary>
///     The <c>v1</c> wire shape for creating a task. Deliberately not
///     <see cref="CreateTaskCommand" />: the published field is <c>name</c>, while the message the
///     domain speaks calls it <c>Title</c>.
/// </summary>
public sealed record CreateTaskRequestV1
{
    /// <summary>The task's name, as <c>v1</c> clients call it.</summary>
    public required string Name { get; init; }
}

/// <summary>The <c>v1</c> wire shape returned after a create.</summary>
public sealed record CreateTaskResponseV1
{
    /// <summary>The new task's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>A link to the created task, which the internal message does not carry.</summary>
    public required string Self { get; init; }
}

/// <summary>
///     The fourth high-level base class: an endpoint whose HTTP contract is deliberately not its
///     message.
/// </summary>
/// <remarks>
///     <para>
///         Nothing here is new behaviour — <c>POST /tasks</c> already creates a task through
///         <see cref="CreateTaskCommand" /> and the same handler serves both routes. What differs is
///         that the wire contract is free to move without the message moving with it: <c>v1</c> clients
///         send <c>name</c> and receive <c>id</c> plus a <c>self</c> link, none of which
///         <see cref="CreateTaskCommand" /> or <see cref="TaskCreated" /> knows about.
///     </para>
///     <para>
///         The binder is still generated — over <see cref="CreateTaskRequestV1" /> rather than over the
///         message, since that is the type arriving on the wire. What this level costs over
///         <see cref="Endpoint{TRequest,TResponse}" /> is exactly the two mapping methods below.
///     </para>
/// </remarks>
[Post("/v1/tasks")]
public sealed class CreateTaskV1Endpoint
    : MappedEndpoint<CreateTaskRequestV1, CreateTaskCommand, TaskCreated, CreateTaskResponseV1>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<CreateTaskResponseV1> builder)
    {
        // Ok() is the default for this level; stated explicitly here only to show the call exists
        // alongside Created() and Accepted(). A v1 contract that promised 201 would say Created().
        builder.Ok()
            .Tag("Contracts")
            .Summary("Create a task (v1 wire contract)");
    }

    /// <inheritdoc />
    public override CreateTaskCommand ToRequest(CreateTaskRequestV1 request)
    {
        return new CreateTaskCommand { Title = request.Name };
    }

    /// <inheritdoc />
    public override CreateTaskResponseV1 ToResponse(TaskCreated response)
    {
        return new CreateTaskResponseV1
        {
            Id = response.TaskId,
            Self = $"/tasks/{response.TaskId}"
        };
    }
}
