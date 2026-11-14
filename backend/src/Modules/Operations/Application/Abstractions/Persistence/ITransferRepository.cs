using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

public sealed record TransferCursor(DateOnly TransferDate, DateTimeOffset CreatedAt)
{
    public string Encode() => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{TransferDate:O}|{CreatedAt:O}"));

    public static TransferCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            return new TransferCursor(DateOnly.Parse(parts[0]), DateTimeOffset.Parse(parts[1]));
        }
        catch
        {
            return null;
        }
    }
}

public sealed record TransferPageResult(IReadOnlyList<Transfer> Items, bool HasMore);

public sealed record TransferWalletIds(WalletId SourceWalletId, WalletId TargetWalletId);

/// <summary>No physical removal (ADR-0002, §3); GetByIdAsync/ListAsync return soft-deleted rows too.</summary>
public interface ITransferRepository
{
    void Add(Transfer transfer);
    Task<Transfer?> GetByIdAsync(TransferId id, CancellationToken cancellationToken);

    /// <summary>Both wallets of the transfer without tracking, or null if it does not exist (used to take the wallet locks before loading the aggregate).</summary>
    Task<TransferWalletIds?> FindWalletIdsAsync(TransferId id, CancellationToken cancellationToken);

    Task<TransferPageResult> ListAsync(
        WalletId? walletId, TransferCursor? afterCursor, int limit, CancellationToken cancellationToken);
}
