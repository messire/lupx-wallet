using FluentAssertions;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.Operations.Infrastructure;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// Operations subscribers to the Wallets events (ADR-0002, §2; ADR-0003, §6): balance corrections
/// are not wallet history, so they are removed together with the wallet and follow its currency.
/// Both handlers take the wallet lock before loading anything.
/// </summary>
public sealed class WalletEventHandlersTests
{
    private static readonly CurrencyId OldCurrency = new(Guid.NewGuid());
    private static readonly CurrencyId NewCurrency = new(Guid.NewGuid());
    private static readonly DateOnly Date = new(2026, 9, 10);

    private readonly IOperationRepository _repository = Substitute.For<IOperationRepository>();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly WalletId _walletId = WalletId.New();
    private readonly List<string> _calls = [];

    public WalletEventHandlersTests()
    {
        _unitOfWork.When(u => u.AcquireWalletLocksAsync(Arg.Any<IEnumerable<WalletId>>(), Arg.Any<CancellationToken>()))
            .Do(call => _calls.Add("lock:" + string.Join(",", call.Arg<IEnumerable<WalletId>>().Select(x => x.Value))));
        _repository.When(r => r.GetCorrectionsAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>())).Do(_ => _calls.Add("load"));
        _unitOfWork.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => _calls.Add("save"));
    }

    private Operation Correction(DateOnly date, decimal stated, decimal amount)
    {
        var correction = Operation.CreateCorrection(OperationId.New(), _walletId, OldCurrency, date, stated);
        correction.ApplyRecalculatedDelta(amount);
        correction.ClearDomainEvents();
        return correction;
    }

    private void ArrangeCorrections(params Operation[] corrections) =>
        _repository.GetCorrectionsAsync(_walletId, Arg.Any<CancellationToken>()).Returns(corrections);

    private static DomainEventNotification<T> Notification<T>(T domainEvent) where T : IDomainEvent => new(domainEvent);

    // ---- WalletDeleted ----

    [Fact]
    public async Task WalletDeleted_MarksEveryCorrectionRemovedRemovesItPhysicallyAndSavesOnce()
    {
        var first = Correction(Date, 350m, 50m);
        var second = Correction(Date.AddDays(10), 600m, -150m);
        ArrangeCorrections(first, second);

        await new WalletDeletedHandler(_repository, _unitOfWork)
            .Handle(Notification(new WalletDeleted(_walletId)), CancellationToken.None);

        foreach (var correction in new[] { first, second })
        {
            var removed = correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CorrectionRemoved>().Subject;
            removed.OperationId.Should().Be(correction.Id);
            removed.WalletId.Should().Be(_walletId);
            removed.OperationDate.Should().Be(correction.OperationDate);
            _repository.Received(1).RemoveCorrection(correction);
        }

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WalletDeleted_TakesTheWalletLockBeforeLoadingAndSavesAfterwards()
    {
        ArrangeCorrections(Correction(Date, 350m, 50m));

        await new WalletDeletedHandler(_repository, _unitOfWork)
            .Handle(Notification(new WalletDeleted(_walletId)), CancellationToken.None);

        _calls.Should().Equal($"lock:{_walletId.Value}", "load", "save");
    }

    [Fact]
    public async Task WalletDeleted_WalletWithoutCorrections_RemovesNothingAndDoesNotSave()
    {
        ArrangeCorrections();

        await new WalletDeletedHandler(_repository, _unitOfWork)
            .Handle(Notification(new WalletDeleted(_walletId)), CancellationToken.None);

        _repository.DidNotReceive().RemoveCorrection(Arg.Any<Operation>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- WalletCurrencyChanged ----

    [Fact]
    public async Task WalletCurrencyChanged_RebasesEveryCorrectionToTheNewCurrencyKeepingTheNumbers()
    {
        var first = Correction(Date, 350m, 50m);
        var second = Correction(Date.AddDays(10), 600m, -150m);
        ArrangeCorrections(first, second);

        await new WalletCurrencyChangedHandler(_repository, _unitOfWork)
            .Handle(Notification(new WalletCurrencyChanged(_walletId, OldCurrency, NewCurrency)), CancellationToken.None);

        first.StatedBalance.Should().Be(new Money(350m, NewCurrency));
        first.Amount.Should().Be(new Money(50m, NewCurrency));
        second.StatedBalance.Should().Be(new Money(600m, NewCurrency));
        second.Amount.Should().Be(new Money(-150m, NewCurrency));
        first.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CorrectionCurrencyChanged>();
        second.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CorrectionCurrencyChanged>();
        _repository.DidNotReceive().RemoveCorrection(Arg.Any<Operation>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WalletCurrencyChanged_TakesTheWalletLockBeforeLoading()
    {
        ArrangeCorrections(Correction(Date, 350m, 50m));

        await new WalletCurrencyChangedHandler(_repository, _unitOfWork)
            .Handle(Notification(new WalletCurrencyChanged(_walletId, OldCurrency, NewCurrency)), CancellationToken.None);

        _calls.Should().Equal($"lock:{_walletId.Value}", "load", "save");
    }

    [Fact]
    public async Task WalletCurrencyChanged_WalletWithoutCorrections_DoesNotSave()
    {
        ArrangeCorrections();

        await new WalletCurrencyChangedHandler(_repository, _unitOfWork)
            .Handle(Notification(new WalletCurrencyChanged(_walletId, OldCurrency, NewCurrency)), CancellationToken.None);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
