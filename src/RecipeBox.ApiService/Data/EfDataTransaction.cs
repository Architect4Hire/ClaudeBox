using Microsoft.EntityFrameworkCore.Storage;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// Adapts EF Core's <see cref="IDbContextTransaction"/> to <see cref="IDataTransaction"/>, so the
/// EF dependency stops at the repository. Not used directly — <see cref="RecipeRepository"/> hands
/// one back from <see cref="IRecipeRepository.BeginTransactionAsync"/>.
/// </summary>
internal sealed class EfDataTransaction(IDbContextTransaction transaction) : IDataTransaction
{
    private readonly IDbContextTransaction _transaction = transaction;

    public Task CommitAsync(CancellationToken ct) => _transaction.CommitAsync(ct);

    // EF rolls an uncommitted transaction back on dispose, which is exactly the contract
    // IDataTransaction promises — nothing to add.
    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
