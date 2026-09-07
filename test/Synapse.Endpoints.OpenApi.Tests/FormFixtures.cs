using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Tests;

/// <summary>A message holding a file part and a plain field, form-bound by convention.</summary>
public sealed record UploadAttachmentCommand : IRequest<string>
{
    /// <summary>The uploaded file, bound with no attribute the way a single file part is.</summary>
    public required IFormFile File { get; init; }

    /// <summary>A plain field alongside the file, bound by property name.</summary>
    public required string Caption { get; init; }
}

/// <summary>Uploads one attachment. A form-bound endpoint with a mixed file/field schema.</summary>
[Post("/attachments")]
public sealed partial class UploadAttachmentEndpoint : Endpoint<UploadAttachmentCommand, string>;

/// <summary>A message holding every file under one field name.</summary>
public sealed record UploadManyCommand : IRequest<string>
{
    /// <summary>Every file on the request, per the <c>IFormFileCollection</c> shape.</summary>
    public required IFormFileCollection Files { get; init; }
}

/// <summary>Uploads many attachments at once. Exercises the file-collection schema.</summary>
[Post("/attachments/many")]
public sealed partial class UploadManyEndpoint : Endpoint<UploadManyCommand, string>;

/// <summary>A plain JSON-bound message, with nothing form-bound at all.</summary>
public sealed record CreateTaskCommand : IRequest<TaskDto>
{
    /// <summary>The task's title, bound from the JSON body by default.</summary>
    public required string Title { get; init; }
}

/// <summary>Creates a task. The control for the JSON path: the form pass must leave it untouched.</summary>
[Post("/tasks")]
public sealed partial class CreateTaskEndpoint : Endpoint<CreateTaskCommand, TaskDto>;
