namespace LupexWallet.ReferenceData.Domain;

/// <summary>
/// Currency offered by the rate service (ADR-0004 §7), keyed by code. A replaceable catalog
/// mirror, not a user record: no events, no audit. A currency becomes active when a
/// <see cref="Currency"/> row with the same code exists.
/// </summary>
public sealed class AvailableCurrency
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public DateTimeOffset RefreshedAt { get; private set; }

    private AvailableCurrency()
    {
        // EF Core only.
    }

    public static AvailableCurrency Create(string code, string name, DateTimeOffset refreshedAt)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new CurrencyCodeRequiredException();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ReferenceItemNameRequiredException();
        }

        return new AvailableCurrency
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            RefreshedAt = refreshedAt,
        };
    }

    public void Update(string name, DateTimeOffset refreshedAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ReferenceItemNameRequiredException();
        }

        Name = name.Trim();
        RefreshedAt = refreshedAt;
    }
}
