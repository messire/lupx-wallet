using System.Globalization;
using System.Text;

namespace LupexWallet.Debts.Application;

/// <summary>Opaque cursor over (Name, CreatedAt) for the creditor list (same scheme as ReferenceData).</summary>
public sealed record CreditorCursor(string Name, DateTimeOffset CreatedAt)
{
    public string Encode() => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CreatedAt:O}|{Name}"));

    public static CreditorCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            return new CreditorCursor(parts[1], DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }
}

/// <summary>Opaque cursor over CreatedAt for the debt and loan lists.</summary>
public static class CreatedAtCursor
{
    public static string Encode(DateTimeOffset createdAt) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(createdAt.ToString("O", CultureInfo.InvariantCulture)));

    public static DateTimeOffset? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)), CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static int ClampLimit(int limit) => Math.Clamp(limit <= 0 ? 20 : limit, 1, 100);
}
