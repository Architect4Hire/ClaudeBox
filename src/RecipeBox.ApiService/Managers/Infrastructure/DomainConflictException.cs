namespace RecipeBox.ApiService.Managers.Infrastructure;

/// <summary>
/// Base for domain rule violations that mean "this clashes with state that already exists" — the
/// family that belongs on HTTP 409.
/// <para>
/// <see cref="DomainExceptionHandler"/> maps this base, not its subclasses, so a new domain conflict
/// gets the right status by deriving from it and nothing else: the handler never learns any domain's
/// type names. Override <see cref="Title"/> to give the client a summary specific to the rule.
/// </para>
/// </summary>
public abstract class DomainConflictException(string message) : Exception(message)
{
    /// <summary>
    /// Short, client-facing summary of the rule that was broken; becomes the ProblemDetails title.
    /// The exception message becomes the detail.
    /// </summary>
    public virtual string Title => "The request conflicts with existing data.";
}
