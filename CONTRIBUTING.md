# Contributing to UnambitiousFx.Synapse

First off, thank you for considering contributing to UnambitiousFx.Synapse! It's people like you that make this library better for everyone.

## Code of Conduct

This project and everyone participating in it is expected to uphold our values of respect, inclusivity, and professionalism. We expect all contributors to:

- Be respectful and considerate in communication
- Accept constructive criticism gracefully
- Focus on what is best for the community
- Show empathy towards other community members

## How Can I Contribute?

### Reporting Bugs

Before creating a bug report, please check existing issues to avoid duplicates. When creating a bug report, include as many details as possible:

**Use the bug report template** which includes:
- A clear and descriptive title
- Steps to reproduce the behavior
- Expected behavior vs actual behavior
- Code samples demonstrating the issue
- Your environment (.NET version, OS, etc.)

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When creating an enhancement suggestion:

**Use the feature request template** which includes:
- A clear description of the feature
- The use case or problem it solves
- Proposed solution or API design
- Alternative approaches you've considered
- Any additional context

### Pull Requests

We actively welcome your pull requests! Here's how to submit one:

1. **Fork the repository** and create your branch from `main`
2. **Make your changes** following our coding standards (see below)
3. **Add tests** if you've added code that should be tested
4. **Update documentation** including XML comments and README if needed
5. **Ensure the test suite passes** (`dotnet test`)
6. **Run benchmarks** if performance is affected
7. **Submit your pull request** with a clear description

#### Pull Request Guidelines

- **Branch naming**: Use descriptive names like `feature/add-retry-logic` or `bugfix/null-reference-in-bind`
- **Commit messages**: Follow [Conventional Commits](https://www.conventionalcommits.org/)
  - `feat:` for new features
  - `fix:` for bug fixes
  - `docs:` for documentation changes
  - `test:` for test changes
  - `refactor:` for code refactoring
  - `perf:` for performance improvements
  - `chore:` for maintenance tasks
- **Keep PRs focused**: One feature or fix per PR
- **Update CHANGELOG.md**: Add your changes under the `[Unreleased]` section
- **Link related issues**: Reference issues in your PR description

Example commit message:
```
feat: add Retry extension method for Result<T>

Implements retry logic with exponential backoff for operations
that return Result<T>. Includes configurable max attempts and delay.

Closes #123
```

## Development Environment Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- An IDE: [JetBrains Rider](https://www.jetbrains.com/rider/), [Visual Studio 2022](https://visualstudio.microsoft.com/), or [VS Code](https://code.visualstudio.com/)
- Git for version control

### Getting Started

1. **Clone the repository**
   ```bash
   git clone https://github.com/UnambitiousFx/Synapse.git
   cd Synapse
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Build the solution**
   ```bash
   dotnet build
   ```

4. **Run the tests**
   ```bash
   dotnet test
   ```

5. **Run benchmarks** (optional)
   ```bash
   dotnet run --project benchmarks/SynapseBenchmark/SynapseBenchmark.csproj -c Release
   ```

### Project Structure

```
Synapse/
├── src/
│   ├── Synapse/              # Core library
│   ├── Synapse.Generators/   # Source generators for OneOf
│   ├── Synapse.AspNetCore/   # ASP.NET Core integration
│   └── Synapse.xunit/        # xUnit testing utilities
├── test/
│   ├── Synapse.Tests/        # Core library tests
│   ├── Synapse.AspNetCore.Tests/
│   └── Synapse.xunit.Tests/
├── benchmarks/
│   └── SynapseBenchmark/     # Performance benchmarks
└── examples/                     # Usage examples (future)
```

## Coding Standards

### Code Style

We use `.editorconfig` to enforce consistent code style. Your IDE should automatically respect these settings.

Key conventions:
- **Indentation**: 4 spaces (no tabs)
- **Braces**: Always use braces for control flow, even single statements
- **Naming**:
  - PascalCase for public members, types, and methods
  - camelCase for parameters and local variables
  - _camelCase for private fields
  - IPascalCase for interfaces
- **Namespaces**: Use file-scoped namespaces (`namespace UnambitiousFx.Synapse;`)
- **Usings**: Place outside namespace, sort System directives first
- **Modern C# features**: Use modern syntax (pattern matching, records, collection expressions, etc.)

### Documentation

- **All public APIs** must have XML documentation comments
- Include `<summary>`, `<param>`, `<returns>`, `<typeparam>`, and `<exception>` tags as appropriate
- Provide `<example>` tags for complex or non-obvious APIs
- Keep documentation concise but complete
- Use proper grammar and punctuation

Example:
```csharp
/// <summary>
///     Transforms the success value of a result using the specified function.
/// </summary>
/// <typeparam name="TIn">The input value type.</typeparam>
/// <typeparam name="TOut">The output value type.</typeparam>
/// <param name="result">The result to transform.</param>
/// <param name="map">The transformation function.</param>
/// <returns>A result with the transformed value, or the original error.</returns>
public static Result<TOut> Map<TIn, TOut>(
    this Result<TIn> result,
    Func<TIn, TOut> map)
    where TIn : notnull
    where TOut : notnull
{
    return result.Match(
        value => Result.Success(map(value)),
        error => Result.Failure<TOut>(error)
    );
}
```

### Testing

- **Write tests** for all new Synapseity
- **Use descriptive test names**: `MethodName_Scenario_ExpectedBehavior`
- **Follow AAA pattern**: Arrange, Act, Assert
- **Test edge cases**: null values, empty collections, boundary conditions
- **Use theory tests** with `[Theory]` and `[InlineData]` for multiple scenarios
- **Aim for high coverage**: Minimum 80% line coverage

Example:
```csharp
[Fact]
public void Map_WithSuccessResult_TransformsValue()
{
    // Arrange
    var result = Result.Success(5);

    // Act
    var mapped = result.Map(x => x * 2);

    // Assert
    mapped.TryGet(out var value, out _).Should().BeTrue();
    value.Should().Be(10);
}

[Fact]
public void Map_WithFailureResult_PropagatesError()
{
    // Arrange
    var error = new Error("Test error");
    var result = Result.Failure<int>(error);

    // Act
    var mapped = result.Map(x => x * 2);

    // Assert
    mapped.TryGet(out _, out var actualError).Should().BeFalse();
    actualError.Should().Be(error);
}
```

### Performance Considerations

- **Avoid allocations** in hot paths when possible
- **Use `readonly struct`** for value types that shouldn't mutate
- **Prefer `ValueTask<T>`** over `Task<T>` for potentially synchronous operations
- **Benchmark changes** if they might affect performance
- **Document perf implications** in complex scenarios

## Testing Your Changes

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test test/Synapse.Tests/

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run tests with verbose output
dotnet test --logger "console;verbosity=detailed"
```

### Code Coverage

We aim for at least 80% code coverage. To generate a coverage report:

```bash
# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

# Install ReportGenerator (if not already installed)
dotnet tool install -g dotnet-reportgenerator-globaltool

# Generate HTML report
reportgenerator -reports:./coverage/**/coverage.cobertura.xml -targetdir:./coverage/report -reporttypes:Html

# Open report (macOS)
open ./coverage/report/index.html
```

### Running Benchmarks

Performance is important. If your changes might affect performance, run benchmarks:

```bash
cd benchmarks/SynapseBenchmark
dotnet run -c Release
```

Compare results before and after your changes.

## Release Process

(For maintainers)

1. Update `CHANGELOG.md` with the new version and date
2. Update version in `Directory.Build.props`
3. Create a git tag: `git tag v1.2.3`
4. Push tag: `git push origin v1.2.3`
5. CI will automatically build and publish to NuGet

## License

By contributing to UnambitiousFx.Synapse, you agree that your contributions will be licensed under the [MIT License](LICENSE).

## Questions?

Feel free to:
- Open a [Discussion](https://github.com/UnambitiousFx/Synapse/discussions) for questions
- Join our community chat (if available)
- Reach out to maintainers

Thank you for contributing! 🎉
