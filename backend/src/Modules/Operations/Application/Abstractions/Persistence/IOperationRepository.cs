using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Cursor on (OperationDate, CreatedAt) — same reason as WalletCursor/NameCursor: EF Core
/// cannot translate member access through a typed Id in LINQ.
/// </summary>
public sealed record OperationCursor(DateOnly OperationDate, DateTimeOffset CreatedAt)
{
    public string Encode() => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{OperationDate:O}|{CreatedAt:O}"));

    public static OperationCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            return new OperationCursor(DateOnly.Parse(parts[0]), DateTimeOffset.Parse(parts[1]));
        }
        catch
        {
            return null;
        }
    }
}

public sealed record OperationListFilter(WalletId? WalletId, OperationTypeId? OperationTypeId, DateOnly? DateFrom, DateOnly? DateTo);

public sealed record OperationPageResult(IReadOnlyList<Operation> Items, bool HasMore);

/// <summary>No physical removal (ADR-0002, §3); GetByIdAsync/ListAsync return soft-deleted rows too.</summary>
public interface IOperationRepository
{
    void Add(Operation operation);
    Task<Operation?> GetByIdAsync(OperationId id, CancellationToken cancellationToken);
    Task<OperationPageResult> ListAsync(OperationListFilter filter, OperationCursor? afterCursor, int limit, CancellationToken cancellationToken);
}
