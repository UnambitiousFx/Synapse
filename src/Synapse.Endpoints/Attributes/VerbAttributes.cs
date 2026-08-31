namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>Declares a <c>GET</c> endpoint at the given route.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GetAttribute : HttpEndpointAttribute
{
    /// <summary>Initializes a new instance of the <see cref="GetAttribute" /> class.</summary>
    /// <param name="route">The route template.</param>
    public GetAttribute(string route) : base("GET", route)
    {
    }
}

/// <summary>Declares a <c>POST</c> endpoint at the given route.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PostAttribute : HttpEndpointAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PostAttribute" /> class.</summary>
    /// <param name="route">The route template.</param>
    public PostAttribute(string route) : base("POST", route)
    {
    }
}

/// <summary>Declares a <c>PUT</c> endpoint at the given route.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PutAttribute : HttpEndpointAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PutAttribute" /> class.</summary>
    /// <param name="route">The route template.</param>
    public PutAttribute(string route) : base("PUT", route)
    {
    }
}

/// <summary>Declares a <c>PATCH</c> endpoint at the given route.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PatchAttribute : HttpEndpointAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PatchAttribute" /> class.</summary>
    /// <param name="route">The route template.</param>
    public PatchAttribute(string route) : base("PATCH", route)
    {
    }
}

/// <summary>Declares a <c>DELETE</c> endpoint at the given route.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DeleteAttribute : HttpEndpointAttribute
{
    /// <summary>Initializes a new instance of the <see cref="DeleteAttribute" /> class.</summary>
    /// <param name="route">The route template.</param>
    public DeleteAttribute(string route) : base("DELETE", route)
    {
    }
}
