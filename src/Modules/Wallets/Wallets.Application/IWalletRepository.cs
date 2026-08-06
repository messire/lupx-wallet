using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Порт репозитория (интерфейс объявлен в Application, реализация — в Infrastructure;
/// high-level-architecture.md: "Application зависит только от Domain").
/// </summary>
public interface IWalletRepository
{
    void Add(Wallet wallet);

    void Remove(Wallet wallet);

    Task<Wallet?> GetByIdAsync(WalletId id, CancellationToken cancellationToken);

    Task<bool> AnyExistsAsync(CancellationToken cancellationToken);

    Task<Wallet?> GetCurrentPrimaryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Страница активных/архивных кошельков, отсортированная по (DisplayOrder, Id) —
    /// см. UC-07 в user-scenarios.md. Курсор непрозрачен для вызывающего кода
    /// (docs/api/api-design.md, §"Пагинация").
    /// </summary>
    Task<WalletPageResult> ListAsync(
        bool includeArchived,
        WalletCursor? afterCursor,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// (DisplayOrder, CreatedAt) — оба поля обычные translatable-колонки. Id намеренно не
/// участвует в сортировке/сравнении: EF Core не транслирует member-access вида
/// "w.Id.Value" через сконвертированный типизированный Id в LINQ-запросах (проверено
/// эмпирически), а сравнивать весь struct WalletId операторами "больше/меньше" нельзя —
/// он не реализует IComparable. CreatedAt неизменяем и монотонен, этого достаточно для
/// стабильной пагинации даже при неуникальном DisplayOrder.
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
