using LupexWallet.Audit.Domain;

namespace LupexWallet.Audit.Application;

/// <summary>
/// Курсор по паре (OccurredAt, Id) — как WalletCursor/OperationCursor/NameCursor, нужен
/// тай-брейкер. AuditEntry.OccurredAt гарантированно строго возрастает только в пределах
/// CLR-процесса (Audit.Domain.MonotonicClock, ADR-0010, разрешение 100нс), но колонка
/// PostgreSQL timestamptz хранит только микросекунды — при округлении до микросекунды две
/// разные in-process метки могут схлопнуться в одно и то же значение в БД. Без тай-брейкера
/// курсорная пагинация по одному OccurredAt могла бы терять или дублировать записи при такой
/// коллизии. Id (Guid, не последовательный) не несёт смысловой упорядоченности — важна лишь
/// его уникальность и стабильность сортировки в паре с OccurredAt.
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

/// <summary>Порт репозитория (интерфейс в Application, реализация — в Infrastructure).</summary>
public interface IAuditEntryRepository
{
    void Add(AuditEntry entry);

    /// <summary>Страница записей аудита конкретной записи, occurred_at по убыванию (docs/api/openapi.yaml: GET /audit-entries).</summary>
    Task<AuditEntryPageResult> ListAsync(
        string entityType, Guid entityId, AuditEntryCursor? afterCursor, int limit, CancellationToken cancellationToken);
}
