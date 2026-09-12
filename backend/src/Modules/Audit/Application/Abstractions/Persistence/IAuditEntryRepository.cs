using LupexWallet.Audit.Domain;

namespace LupexWallet.Audit.Application;

/// <summary>
/// Cursor over the (OccurredAt, Id) pair — like WalletCursor/OperationCursor/NameCursor, needs
/// a tiebreaker. AuditEntry.OccurredAt is guaranteed strictly increasing only within the CLR
/// process (Audit.Domain.MonotonicClock, ADR-0010, 100ns resolution), but the PostgreSQL
/// timestamptz column only stores microseconds — rounding can collapse two distinct in-process
/// timestamps into the same stored value. Without the tiebreaker, cursor pagination on
/// OccurredAt alone could lose or duplicate rows on such a collision. Id (a non-sequential Guid)
/// carries no ordering meaning by itself — only its uniqueness and stable sort alongside
/// OccurredAt matter.
/// </summary>
public sealed record AuditEntryCursor(DateTimeOffset OccurredAt, Guid Id)
{
    public string Encode() => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{OccurredAt:O}|{Id}"));

    public static AuditEntryCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            return new AuditEntryCursor(
                DateTimeOffset.Parse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind),
                Guid.Parse(parts[1]));
        }
        catch
        {
            return null;
        }
    }
}

public sealed record AuditEntryPageResult(IReadOnlyList<AuditEntry> Items, bool HasMore);

/// <summary>Repository port (interface in Application, implementation in Infrastructure).</summary>
public interface IAuditEntryRepository
{
    void Add(AuditEntry entry);

    /// <summary>Page of audit entries for one record, occurred_at descending (docs/api/openapi.yaml: GET /audit-entries).</summary>
    Task<AuditEntryPageResult> ListAsync(
        string entityType, Guid entityId, AuditEntryCursor? afterCursor, int limit, CancellationToken cancellationToken);
}
