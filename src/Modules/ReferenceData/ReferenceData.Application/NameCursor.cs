namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Курсор постраничной навигации по (Name, CreatedAt) для всех трех пользовательских
/// справочников — списки сортируются по названию (удобно для выпадающих списков в UI),
/// CreatedAt — тай-брейкер на случай совпадения имен между активной и деактивированной
/// записью. Id намеренно не участвует — та же причина, что у WalletCursor в модуле
/// Wallets: EF Core не транслирует member-access через сконвертированный типизированный
/// Id в LINQ-запросах.
/// </summary>
public sealed record NameCursor(string Name, DateTimeOffset CreatedAt)
{
    public string Encode() => Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes($"{CreatedAt:O}|{Name}"));

    public static NameCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            return new NameCursor(parts[1], DateTimeOffset.Parse(parts[0]));
        }
        catch
        {
            return null;
        }
    }
}
