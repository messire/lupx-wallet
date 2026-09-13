namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Pagination cursor over (Name, CreatedAt) for all three user-editable references — lists
/// are sorted by name (convenient for UI dropdowns); CreatedAt is a tie-breaker for name
/// collisions between an active and a deactivated record. Id is intentionally excluded —
/// same reason as WalletCursor in the Wallets module: EF Core does not translate member
/// access through a converted typed Id in LINQ queries.
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
