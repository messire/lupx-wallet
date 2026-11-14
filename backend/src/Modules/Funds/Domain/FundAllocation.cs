using LupexWallet.SharedKernel;

namespace LupexWallet.Funds.Domain;

/// <summary>
/// Reserved part of one wallet's money inside a fund (ADR-0010 §4). Child entity of
/// <see cref="Fund"/>, changed only through the fund. A released allocation stays with amount 0:
/// it is the allocation history that forbids deleting the fund.
/// </summary>
public sealed class FundAllocation
{
    public Guid Id { get; private set; }
    public FundId FundId { get; private set; }
    public WalletId WalletId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private FundAllocation()
    {
        // EF Core only.
    }

    internal static FundAllocation Create(FundId fundId, WalletId walletId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        FundId = fundId,
        WalletId = walletId,
        Amount = 0m,
        CreatedAt = now,
        UpdatedAt = now,
    };

    internal void SetAmount(decimal amount, DateTimeOffset now)
    {
        Amount = amount;
        UpdatedAt = now;
    }
}
