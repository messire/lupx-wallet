using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;

namespace LupexWallet.Wallets.Application;

/// <summary>Repository port (interface in Application, implementation in Infrastructure; high-level-architecture.md: "Application depends only on Domain").</summary>
public interface IWalletRepository
{
    void Add(Wallet wallet);

    void Remove(Wallet wallet);

    Task<Wallet?> GetByIdAsync(WalletId id, CancellationToken cancellationToken);

    Task<bool> AnyExistsAsync(CancellationToken cancellationToken);

    Task<Wallet?> GetCurrentPrimaryAsync(CancellationToken cancellationToken);

    /// <summary>Page of active/archived wallets ordered by (DisplayOrder, Id) — UC-07. Cursor is opaque to the caller (docs/api/api-design.md, "Pagination").</summary>
    Task<WalletPageResult> ListAsync(
        bool includeArchived,
        WalletCursor? afterCursor,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Cursor on (DisplayOrder, CreatedAt) — both are plain translatable columns. Id is
/// deliberately excluded: EF Core cannot translate "w.Id.Value" through the converted
/// typed Id in LINQ, and WalletId is not IComparable. CreatedAt is immutable and monotonic,
/// which is enough for stable pagination even with a non-unique DisplayOrder.
/// </summary>
public sealed record WalletCursor(int DisplayOrder, DateTimeOffset CreatedAt)
{
    public string Encode() => Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes($"{DisplayOrder}:{CreatedAt:O}"));

    public static WalletCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split(':', 2);
            return new WalletCursor(int.Parse(parts[0]), DateTimeOffset.Parse(parts[1]));
        }
        catch
        {
            return null;
        }
    }
}

public sealed record WalletPageResult(IReadOnlyList<Wallet> Items, bool HasMore);
