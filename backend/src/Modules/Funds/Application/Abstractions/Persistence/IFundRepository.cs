using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Funds.Application;

/// <summary>Repository port of the Fund aggregate; loads funds with their allocations.</summary>
public interface IFundRepository
{
    void Add(Fund fund);

    void Remove(Fund fund);

    Task<Fund?> GetByIdAsync(FundId id, CancellationToken cancellationToken);

    /// <summary>Active funds linked to the debt or loan, oldest first.</summary>
    Task<IReadOnlyList<Fund>> GetActiveLinkedAsync(Guid? debtId, Guid? loanId, CancellationToken cancellationToken);

    /// <summary>Active funds that keep a non-zero reserve released by the payment operation (B9), oldest first.</summary>
    Task<IReadOnlyList<Fund>> GetActiveWithLinkedReleaseAsync(OperationId operationId, CancellationToken cancellationToken);

    /// <summary>Page ordered by CreatedAt.</summary>
    Task<FundPageResult> ListAsync(bool includeClosed, FundCursor? afterCursor, int limit, CancellationToken cancellationToken);

    /// <summary>Sum of the allocations of all active funds on the wallet, as stored.</summary>
    Task<decimal> GetReservedOnWalletAsync(WalletId walletId, CancellationToken cancellationToken);

    /// <summary>Sum of the allocations of all active funds per wallet; wallets without reserves are absent.</summary>
    Task<IReadOnlyList<WalletReservation>> GetReservedByWalletAsync(CancellationToken cancellationToken);
}

public sealed record FundPageResult(IReadOnlyList<Fund> Items, bool HasMore);

/// <summary>Opaque cursor on CreatedAt (immutable, monotonic), see WalletCursor for the same choice.</summary>
public sealed record FundCursor(DateTimeOffset CreatedAt)
{
    public string Encode() => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(CreatedAt.ToString("O")));

    public static FundCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return new FundCursor(DateTimeOffset.Parse(raw, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
