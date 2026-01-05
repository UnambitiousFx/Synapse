using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Examples.Application.Infrastructure.Services;

namespace UnambitiousFx.Examples.WebApiAot.Models;

[JsonSerializable(typeof(UpdateTodoModel))]
[JsonSerializable(typeof(CreateTodoModel))]
[JsonSerializable(typeof(IEnumerable<Todo>))]
[JsonSerializable(typeof(RequestFulfillmentModel))]
[JsonSerializable(typeof(FulfillmentInfo))]
[JsonSerializable(typeof(IEnumerable<FulfillmentInfo>))]
[SuppressMessage("ReSharper", "PartialTypeWithSinglePart")]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}