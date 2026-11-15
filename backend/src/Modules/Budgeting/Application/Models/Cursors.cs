using System.Globalization;
using System.Text;

namespace LupexWallet.Budgeting.Application;

/// <summary>Opaque cursor over (Name, CreatedAt) for the category list.</summary>
public sealed record CategoryCursor(string Name, DateTimeOffset CreatedAt)
{
    public string Encode() => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CreatedAt:O}|{Name}"));

    public static CategoryCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            return new CategoryCursor(parts[1], DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Opaque cursor over (Month desc, CreatedAt asc) for the budget list.</summary>
public sealed record BudgetCursor(DateOnly Month, DateTimeOffset CreatedAt)
{
    public string Encode() => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Month:yyyy-MM-dd}|{CreatedAt:O}"));

    public static BudgetCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            return new BudgetCursor(
                DateOnly.ParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(parts[1], CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }
}
