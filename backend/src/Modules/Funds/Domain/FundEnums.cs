namespace LupexWallet.Funds.Domain;

/// <summary>Fund lifecycle (ADR-0010 §4, §8). Closed is final.</summary>
public enum FundStatus
{
    Active,
    Closed,
}

/// <summary>Why an allocation was decreased.</summary>
public enum FundReleaseReason
{
    /// <summary>The user released the reserve.</summary>
    Manual,

    /// <summary>The fund was closed and all allocations were released.</summary>
    FundClosed,

    /// <summary>A payment of the linked debt/loan released the reserve (ADR-0010 §4, B9).</summary>
    LinkedPayment,
}
