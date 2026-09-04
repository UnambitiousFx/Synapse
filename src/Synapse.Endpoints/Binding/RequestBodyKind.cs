namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>What a binder reads the request body as, so the endpoint can declare a matching content type.</summary>
public enum RequestBodyKind
{
    /// <summary>Nothing is read from the body.</summary>
    None,

    /// <summary>The message is deserialized from a JSON body.</summary>
    Json,

    /// <summary>
    ///     Values are read from a form — <c>multipart/form-data</c> or
    ///     <c>application/x-www-form-urlencoded</c>. Both content types are one kind here because an
    ///     endpoint that accepts either accepts both: the binder reads fields and files the same way
    ///     whichever encoding carried them.
    /// </summary>
    Form
}
