using LupexWallet.SharedKernel;

namespace LupexWallet.Funds.Domain;

/// <summary>
/// Reserve released from a fund by one linked debt/loan payment (ADR-0010 §4, B9). Child entity of
/// <see cref="Fund"/>. <see cref="Amount"/> is what is released now: it decreases when the payment
/// expense is soft-deleted and the reserve comes back, and grows again when the expense is restored.
/// </summary>
public sealed class FundLinkedRelease
{
    public Guid Id { get; private set; }
    public FundId FundId { get; private set; }
    public OperationId OperationId { get; private set; }
    public WalletId WalletId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private FundLinkedRelease()
    {
        // EF Core only.
    }

    internal static FundLinkedRelease Create(FundId fundId, OperationId operationId, WalletId walletId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        FundId = fundId,
        OperationId = operationId,
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
