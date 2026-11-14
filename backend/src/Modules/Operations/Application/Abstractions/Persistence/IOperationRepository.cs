using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Cursor on (OperationDate, IsCorrection, CreatedAt) — same reason as WalletCursor/NameCursor:
/// EF Core cannot translate member access through a typed Id in LINQ. A cursor without the
/// IsCorrection part (issued before balance corrections existed) decodes as a regular operation.
/// </summary>
public sealed record OperationCursor(DateOnly OperationDate, DateTimeOffset CreatedAt, bool IsCorrection = false)
{
    public string Encode() => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
        $"{OperationDate:O}|{CreatedAt:O}|{(IsCorrection ? 1 : 0)}"));

    public static OperationCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|');
            var isCorrection = parts.Length > 2 && parts[2] == "1";
            return new OperationCursor(DateOnly.Parse(parts[0]), DateTimeOffset.Parse(parts[1]), isCorrection);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record OperationListFilter(WalletId? WalletId, OperationTypeId? OperationTypeId, DateOnly? DateFrom, DateOnly? DateTo);

public sealed record OperationPageResult(IReadOnlyList<Operation> Items, bool HasMore);

/// <summary>A regular (non-correction, non-deleted) operation's effect on a wallet's balance.</summary>
public sealed record WalletLedgerEntry(DateOnly OperationDate, decimal AppliedDelta);

/// <summary>
/// Soft-deleted rows are never removed (ADR-0002, §3); GetByIdAsync/ListAsync return them too. The
/// only physical removal is <see cref="RemoveCorrection"/>, together with the wallet (ADR-0002, §2).
/// </summary>
public interface IOperationRepository
{
    void Add(Operation operation);
    Task<Operation?> GetByIdAsync(OperationId id, CancellationToken cancellationToken);

    /// <summary>Wallet of the operation without tracking, or null if it does not exist (used to take the wallet lock before loading the aggregate).</summary>
    Task<WalletId?> FindWalletIdAsync(OperationId id, CancellationToken cancellationToken);

    /// <summary>
    /// Page ordered by (OperationDate, IsCorrection, CreatedAt) ascending, so a correction follows
    /// the other operations of its day (ADR-0003, §6). Zero corrections are not listed.
    /// </summary>
    Task<OperationPageResult> ListAsync(OperationListFilter filter, OperationCursor? afterCursor, int limit, CancellationToken cancellationToken);

    /// <summary>Tracked corrections of the wallet, ordered by date ascending.</summary>
    Task<IReadOnlyList<Operation>> GetCorrectionsAsync(WalletId walletId, CancellationToken cancellationToken);

    /// <summary>Tracked correction of the wallet for the date, or null.</summary>
    Task<Operation?> GetCorrectionAsync(WalletId walletId, DateOnly operationDate, CancellationToken cancellationToken);

    /// <summary>
    /// Non-deleted, non-correction operations of the wallet as read-only projections from the
    /// database, skipping <paramref name="excludingIds"/> (operations changed by the current command,
    /// which the caller supplies from memory). When <paramref name="throughDate"/> is set, operations
    /// dated after it are not read.
    /// </summary>
    Task<IReadOnlyList<WalletLedgerEntry>> GetRegularLedgerAsync(
        WalletId walletId, IReadOnlyCollection<OperationId> excludingIds, DateOnly? throughDate, CancellationToken cancellationToken);

    Task<bool> AnyCorrectionsAsync(WalletId walletId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WalletId>> GetWalletIdsWithCorrectionsAsync(CancellationToken cancellationToken);

    /// <summary>Physically removes a correction (it is not soft-deletable); throws for a regular operation.</summary>
    void RemoveCorrection(Operation correction);
}
