# Synapse MinimalApi Example (Native AOT Ready)

This example demonstrates how to use Synapse in a Native AOT-compatible ASP.NET Core application.

## What is Native AOT?

Native Ahead-of-Time (AOT) compilation compiles your .NET application directly to native machine code, resulting in:

- **Faster Startup** - No JIT compilation at runtime
- **Smaller Memory Footprint** - Reduced working set
- **Smaller Deployment Size** - Only necessary code is included
- **Better Performance** - Optimized native code

## Features Demonstrated

- ✅ **Native AOT Compatibility** - Uses `CreateSlimBuilder()` and trimming-friendly patterns
- ✅ **JSON Source Generation** - Required for AOT, using `[JsonSerializable]` attributes
- ✅ **Minimal Dependencies** - Lean and fast
- ✅ **Same Synapse API** - Identical to WebApi example, just AOT-optimized

## Project Structure

Identical to [WebApi](../WebApi/README.md) but with AOT-specific configurations.

## Key Differences from WebApi

### 1. Slim Builder

```csharp
// WebApi uses:
var builder = WebApplication.CreateBuilder(args);

// MinimalApi uses:
var builder = WebApplication.CreateSlimBuilder(args);
```

`CreateSlimBuilder` includes only essential services for smaller binaries.

### 2. JSON Source Generation

```csharp
[JsonSerializable(typeof(CreateTaskRequest))]
[JsonSerializable(typeof(UpdateTaskRequest))]
[JsonSerializable(typeof(TaskDto))]
[JsonSerializable(typeof(List<TaskDto>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0,
        AppJsonSerializerContext.Default);
});
```

This tells the AOT compiler which types need JSON serialization support.

### 3. Project Configuration

```xml
<PropertyGroup>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

- `PublishAot` - Enables Native AOT compilation
- `InvariantGlobalization` - Reduces size by using invariant culture only

## Running the Example

### Development (JIT)

```bash
cd examples/MinimalApi
dotnet run
```

### Publish as Native AOT

```bash
dotnet publish -c Release
```

The native executable will be in `bin/Release/net10.0/{runtime}/publish/`.

**Size Comparison:**

- Regular publish: ~90 MB
- Native AOT publish: ~15-25 MB
- Startup time: 2-3x faster

## AOT Compatibility Notes

### ✅ Synapse is AOT-Ready

Synapse is designed to work with Native AOT:

- No runtime reflection in hot paths
- Source generators for handler registration
- Trimming annotations
- ValueTask for minimal allocations

### ⚠️ What to Watch For

1. **Avoid Runtime Reflection**
   ```csharp
   // Bad (not AOT-friendly)
   var handler = Activator.CreateInstance(handlerType);

   // Good (AOT-friendly)
   var handler = new MyHandler(dependencies);
   ```

2. **JSON Serialization**
    - Always add `[JsonSerializable]` for types used in HTTP requests/responses
    - Use source-generated serializers

3. **Dependency Injection**
    - Register services explicitly
    - Avoid scanning assemblies at runtime

## Performance Characteristics

| Metric        | Regular .NET | Native AOT      |
|---------------|--------------|-----------------|
| Startup Time  | 500-800ms    | 150-250ms       |
| Memory (idle) | 60-80 MB     | 20-35 MB        |
| Binary Size   | ~90 MB       | ~15-25 MB       |
| First Request | Similar      | Slightly faster |

## Testing AOT Compatibility

### Check for AOT Warnings

```bash
dotnet publish -c Release /p:PublishAot=true
```

Look for warnings like:

- `IL2026` - Methods that require runtime reflection
- `IL3050` - Trimming warnings

### Run Published Binary

```bash
./bin/Release/net10.0/{runtime}/publish/MinimalApi
```

## Common AOT Issues and Solutions

### Issue: JSON Serialization Fails

**Problem:** Type not included in source generation

```
System.InvalidOperationException: No metadata for type X
```

**Solution:** Add to `AppJsonSerializerContext`:

```csharp
[JsonSerializable(typeof(YourType))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }
```

### Issue: Trimming Removes Required Code

**Problem:** Code referenced via reflection is trimmed

**Solution:** Use `[DynamicallyAccessedMembers]` or register explicitly

## API Endpoints

Same as [WebApi example](../WebApi/README.md#api-endpoints).

## When to Use Native AOT

### ✅ Good Fit

- Microservices and containers
- Serverless/FaaS (Azure Functions, AWS Lambda)
- CLI tools
- Resource-constrained environments
- Cold-start sensitive applications

### ❌ Not Ideal

- Applications heavy on runtime reflection
- Dynamic plugin systems
- Large dependency trees with non-AOT-ready libraries

## Next Steps

- Compare startup times between WebApi and MinimalApi
- Profile memory usage
- Test in containerized environments
- See [GettingStarted](../GettingStarted/README.md) for Synapse fundamentals

## Learn More

- [.NET Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Trim Warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings)
- [JSON Source Generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
