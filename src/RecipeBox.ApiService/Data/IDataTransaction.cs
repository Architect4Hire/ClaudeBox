namespace RecipeBox.ApiService.Data;

/// <summary>
/// An open transaction the data layer can commit, with no EF type on the surface — the data layer
/// decides <em>where</em> the atomic boundary of an operation is, while the repository stays the only
/// thing that knows how a transaction is actually started.
/// </summary>
/// <remarks>
/// Disposal without a <see cref="CommitAsync"/> rolls back, so the intended use is
/// <c>await using</c>: an exception on any leg of a composed operation leaves the store untouched.
/// </remarks>
public interface IDataTransaction : IAsyncDisposable
{
    /// <summary>Commits everything done on the repository since the transaction was opened.</summary>
    Task CommitAsync(CancellationToken ct);
}
