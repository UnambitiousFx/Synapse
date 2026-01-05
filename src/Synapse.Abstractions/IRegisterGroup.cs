namespace UnambitiousFx.Synapse.Abstractions;

/// Represents a contract for registering groups of dependencies for use within the application's dependency injection container.
/// A group typically encapsulates related request or event handlers and their configurations, aiming to modularize registration processes.
public interface IRegisterGroup
{
    /// Registers custom handlers and dependencies into the dependency injection container.
    /// <param name="builder">
    ///     The builder instance to configure and register handlers and dependencies.
    ///     Implementations should make use of the builder methods such as
    ///     RegisterRequestHandler or RegisterEventHandler to register appropriate handlers.
    /// </param>
    void Register(IDependencyInjectionBuilder builder);
}