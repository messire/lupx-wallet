using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Domain;

/// <summary>
/// Wallet aggregate (ddd-model.md, §2.1). Balances are stored as bare decimal fields plus a
/// single CurrencyId; Money is computed on read to avoid a duplicate currency_id column per
/// amount (schema.md).
/// </summary>
public sealed class Wallet : AggregateRoot<WalletId>
{
    private decimal _initialBalanceAmount;
    private decimal _currentBalanceAmount;

    public string Name { get; private set; } = null!;
    public WalletTypeId WalletTypeId { get; private set; }
    public string? PurposeDescription { get; private set; }
    public CurrencyId CurrencyId { get; private set; }
    public DateOnly AccountingStartDate { get; private set; }
    public bool IncludeInTotal { get; private set; }
    public bool IsPrimary { get; private set; }
    public bool IsArchived { get; private set; }
    public int DisplayOrder { get; private set; }
    public string? Color { get; private set; }
    public string? Icon { get; private set; }

    public Money InitialBalance => new(_initialBalanceAmount, CurrencyId);
    public Money CurrentBalance => new(_currentBalanceAmount, CurrencyId);

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Wallet()
    {
        // Только для EF Core — материализация через backing fields.
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a wallet (UC-01). <paramref name="isFirstWallet"/> is determined by the caller
    /// (Application layer, via IWalletRepository.AnyExistsAsync) — the aggregate itself cannot
    /// know whether other wallets exist.
    /// </summary>
    public static Wallet Create(
        WalletId id,
        string name,
        WalletTypeId walletTypeId,
        Money initialBalance,
        DateOnly accountingStartDate,
        string? purposeDescription,
        bool includeInTotal,
        int displayOrder,
        string? color,
        string? icon,
        bool isFirstWallet)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new WalletNameRequiredException();
        }

        var isPrimary = isFirstWallet;
        var now = DateTimeOffset.UtcNow;
        var wallet = new Wallet
        {
            Id = id,
            Name = name.Trim(),
            WalletTypeId = walletTypeId,
            CurrencyId = initialBalance.CurrencyId,
            _initialBalanceAmount = initialBalance.Amount,
            _currentBalanceAmount = initialBalance.Amount,
            AccountingStartDate = accountingStartDate,
            PurposeDescription = purposeDescription,
            // Решено, Q8: основной кошелек всегда включен в общую сумму.
            IncludeInTotal = isPrimary || includeInTotal,
            IsPrimary = isPrimary,
            IsArchived = false,
            DisplayOrder = displayOrder,
            Color = color,
            Icon = icon,
            CreatedAt = now,
            UpdatedAt = now,
        };

        wallet.Raise(new WalletCreated(id));
        if (isPrimary)
        {
            wallet.Raise(new PrimaryWalletChanged(id, PreviousPrimaryWalletId: null));
        }

        return wallet;
    }

    public void UpdateDetails(
        string name,
        WalletTypeId walletTypeId,
        string? purposeDescription,
        bool includeInTotal,
        int displayOrder,
        string? color,
        string? icon)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new WalletNameRequiredException();
        }

        Name = name.Trim();
        WalletTypeId = walletTypeId;
        PurposeDescription = purposeDescription;
        // Решено, Q8: у основного кошелька признак нельзя выключить.
        IncludeInTotal = IsPrimary || includeInTotal;
        DisplayOrder = displayOrder;
        Color = color;
        Icon = icon;
        Touch();

        Raise(new WalletUpdated(Id));
    }

    public void Archive()
    {
        if (IsPrimary)
        {
            throw new CannotArchivePrimaryWalletException(Id.Value);
        }

        IsArchived = true;
        Touch();
        Raise(new WalletArchived(Id));
    }

    /// <summary>Called by PrimaryWalletPolicy on the new primary wallet.</summary>
    public void MarkAsPrimary(WalletId? previousPrimaryWalletId)
    {
        if (IsArchived)
        {
            throw new CannotSetArchivedWalletAsPrimaryException(Id.Value);
        }

        IsPrimary = true;
        IncludeInTotal = true;
        Touch();
        Raise(new PrimaryWalletChanged(Id, previousPrimaryWalletId));
    }

    /// <summary>Called by PrimaryWalletPolicy on the previous primary wallet.</summary>
    public void UnmarkAsPrimary()
    {
        IsPrimary = false;
        Touch();
    }

    public void ChangeCurrency(CurrencyId newCurrencyId, bool hasHistory)
    {
        if (hasHistory)
        {
            throw new WalletCurrencyChangeNotAllowedException(Id.Value);
        }

        var oldCurrencyId = CurrencyId;
        CurrencyId = newCurrencyId;
        Touch();
        Raise(new WalletCurrencyChanged(Id, oldCurrencyId, newCurrencyId));
    }

    /// <summary>Applies a signed delta to the current balance; invoked via IWalletBalanceGateway (ADR-0007) from Operations commands.</summary>
    public void ApplyBalanceDelta(Money delta)
    {
        if (delta.CurrencyId != CurrencyId)
        {
            throw new WalletCurrencyMismatchException();
        }

        _currentBalanceAmount += delta.Amount;
        Touch();
        Raise(new WalletBalanceChanged(Id, delta));
    }

    /// <summary>
    /// Validates deletion is allowed. <paramref name="hasHistory"/> is computed by the
    /// Application layer (ADR-0009, IWalletHistorySource), since the aggregate cannot know
    /// about other modules' Operation/BalanceSnapshot data; IsPrimary is own state, checked
    /// directly.
    /// </summary>
    public void EnsureCanBeDeleted(bool hasHistory)
    {
        if (IsPrimary)
        {
            throw new CannotDeletePrimaryWalletException(Id.Value);
        }

        if (hasHistory)
        {
            throw new WalletDeletionNotAllowedException(Id.Value);
        }
    }

    public void MarkAsDeleted() => Raise(new WalletDeleted(Id));
}
