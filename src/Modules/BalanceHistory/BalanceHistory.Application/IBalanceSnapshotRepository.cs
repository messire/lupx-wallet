using LupexWallet.BalanceHistory.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Application;

/// <summary>
/// Порт репозитория (интерфейс объявлен в Application, реализация — в Infrastructure).
/// Идентичность BalanceSnapshot — пара (WalletId, SnapshotDate), см. BalanceSnapshot.
/// </summary>
public interface IBalanceSnapshotRepository
{
    void Add(BalanceSnapshot snapshot);

    Task<BalanceSnapshot?> GetAsync(WalletId walletId, DateOnly snapshotDate, CancellationToken cancellationToken);

    /// <summary>Все существующие слепки кошелька в диапазоне [from, to] — один запрос вместо N (используется каскадным пересчетом, ADR-0003).</summary>
    Task<IReadOnlyList<BalanceSnapshot>> GetRangeAsync(WalletId walletId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Последняя (максимальная) дата существующего слепка кошелька, если есть — используется досозданием пропусков (ADR-0004).</summary>
    Task<DateOnly?> GetLatestSnapshotDateAsync(WalletId walletId, CancellationToken cancellationToken);

    /// <summary>Страница слепков кошелька за период [from, to], по возрастанию даты (docs/api/openapi.yaml: GET /wallets/{id}/balance-history).</summary>
    Task<BalanceSnapshotPageResult> ListAsync(
        WalletId walletId,
        DateOnly from,
        DateOnly to,
        BalanceSnapshotCursor? afterCursor,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Курсор только по SnapshotDate — в отличие от WalletCursor/OperationCursor в других
/// модулях, здесь не нужен tiebreaker вроде CreatedAt: (WalletId, SnapshotDate) уже
/// составной первичный ключ BalanceSnapshot (schema.md), поэтому в пределах одного
/// кошелька SnapshotDate сам по себе уникален и монотонно упорядочивает страницы.
/// </summary>
public sealed record BalanceSnapshotCursor(DateOnly SnapshotDate)
{
    public string Encode() => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{SnapshotDate:O}"));

    public static BalanceSnapshotCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return new BalanceSnapshotCursor(DateOnly.Parse(raw));
        }
        catch
        {
            return null;
        }
    }
}

public sealed record BalanceSnapshotPageResult(IReadOnlyList<BalanceSnapshot> Items, bool HasMore);
