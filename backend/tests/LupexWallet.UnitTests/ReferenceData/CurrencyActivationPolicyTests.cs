using FluentAssertions;
using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.ReferenceData.Infrastructure;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ReferenceData;

/// <summary>
/// CurrencyActivationPolicy and the ICurrencyActivation port (ADR-0004 §7): a currency
/// becomes active only from the available list; the code is normalised; wallets resolve a code to an
/// active currency (existing, activated, or null).
/// </summary>
public sealed class CurrencyActivationPolicyTests
{
    private readonly ICurrencyRepository _currencies = Substitute.For<ICurrencyRepository>();
    private readonly IAvailableCurrencyRepository _available = Substitute.For<IAvailableCurrencyRepository>();
    private readonly IReferenceDataUnitOfWork _unitOfWork = Substitute.For<IReferenceDataUnitOfWork>();

    private CurrencyActivationPolicy CreatePolicy() => new(_currencies, _available);

    private void MakeAvailable(string code, string name) =>
        _available.GetByCodeAsync(code, Arg.Any<CancellationToken>()).Returns(AvailableCurrency.Create(code, name, DateTimeOffset.UtcNow));

    // ---- ActivateAsync ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Activate_BlankCode_ThrowsCurrencyCodeRequired(string? code)
    {
        var act = () => CreatePolicy().ActivateAsync(code, CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyCodeRequiredException>();
    }

    [Fact]
    public async Task Activate_NormalizesTheCodeBeforeEveryLookup()
    {
        MakeAvailable("USD", "United States Dollar");

        var currency = await CreatePolicy().ActivateAsync("  usd ", CancellationToken.None);

        currency.Code.Should().Be("USD");
        await _currencies.Received(1).CodeExistsAsync("USD", Arg.Any<CancellationToken>());
        await _available.Received(1).GetByCodeAsync("USD", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Activate_AvailableAndNotActive_AddsCurrencyWithServiceNameAndCurrencyCreatedEvent()
    {
        MakeAvailable("USD", "United States Dollar");

        var currency = await CreatePolicy().ActivateAsync("USD", CancellationToken.None);

        currency.Name.Should().Be("United States Dollar");
        currency.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CurrencyCreated>();
        _currencies.Received(1).Add(currency);
    }

    [Fact]
    public async Task Activate_NotAvailable_ThrowsCurrencyNotAvailableAndAddsNothing()
    {
        _available.GetByCodeAsync("BTC", Arg.Any<CancellationToken>()).Returns((AvailableCurrency?)null);

        var act = () => CreatePolicy().ActivateAsync("BTC", CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyNotAvailableException>();
        _currencies.DidNotReceive().Add(Arg.Any<Currency>());
    }

    [Fact]
    public async Task Activate_AlreadyActive_ThrowsConflictEvenWhenNotAvailableAnymore()
    {
        _currencies.CodeExistsAsync("XYZ", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreatePolicy().ActivateAsync("xyz", CancellationToken.None);

        (await act.Should().ThrowAsync<CurrencyCodeAlreadyExistsException>())
            .Which.Message.Should().Be("Валюта XYZ уже добавлена в активные.");
        await _available.DidNotReceiveWithAnyArgs().GetByCodeAsync(default!, default);
    }

    // ---- EnsureActiveAsync ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task EnsureActive_BlankCode_ReturnsNullWithoutTouchingStorage(string? code)
    {
        var currency = await CreatePolicy().EnsureActiveAsync(code, CancellationToken.None);

        currency.Should().BeNull();
        await _currencies.DidNotReceiveWithAnyArgs().GetByCodeAsync(default!, default);
        await _available.DidNotReceiveWithAnyArgs().GetByCodeAsync(default!, default);
    }

    [Fact]
    public async Task EnsureActive_AlreadyActive_ReturnsExistingWithoutCreatingOrCheckingAvailability()
    {
        var existing = Currency.Create(CurrencyId.New(), "EUR", "Euro");
        _currencies.GetByCodeAsync("EUR", Arg.Any<CancellationToken>()).Returns(existing);

        var currency = await CreatePolicy().EnsureActiveAsync(" eur", CancellationToken.None);

        currency.Should().BeSameAs(existing);
        _currencies.DidNotReceive().Add(Arg.Any<Currency>());
        await _available.DidNotReceiveWithAnyArgs().GetByCodeAsync(default!, default);
    }

    [Fact]
    public async Task EnsureActive_AvailableButNotActive_CreatesIt()
    {
        MakeAvailable("GBP", "British Pound");

        var currency = await CreatePolicy().EnsureActiveAsync("gbp", CancellationToken.None);

        currency.Should().NotBeNull();
        currency!.Code.Should().Be("GBP");
        currency.Name.Should().Be("British Pound");
        _currencies.Received(1).Add(currency);
    }

    [Fact]
    public async Task EnsureActive_NeitherActiveNorAvailable_ReturnsNull()
    {
        var currency = await CreatePolicy().EnsureActiveAsync("ZZ9", CancellationToken.None);

        currency.Should().BeNull();
        _currencies.DidNotReceive().Add(Arg.Any<Currency>());
    }

    // ---- ICurrencyActivation port ----

    [Fact]
    public async Task Port_AvailableCode_SavesAndReturnsIdAndCode()
    {
        MakeAvailable("GBP", "British Pound");
        var port = new CurrencyActivation(CreatePolicy(), _unitOfWork);

        var result = await port.EnsureActiveAsync("GBP", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Code.Should().Be("GBP");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Port_NotActiveCode_TakesTheExclusiveLockAndRechecksBeforeActivating()
    {
        MakeAvailable("GBP", "British Pound");
        var port = new CurrencyActivation(CreatePolicy(), _unitOfWork);

        await port.EnsureActiveAsync(" gbp ", CancellationToken.None);

        Received.InOrder(() =>
        {
            _currencies.GetByCodeAsync("GBP", Arg.Any<CancellationToken>());
            _unitOfWork.LockCurrencyCodeAsync(" gbp ", Arg.Any<CancellationToken>());
            _currencies.GetByCodeAsync("GBP", Arg.Any<CancellationToken>());
            _currencies.Add(Arg.Any<Currency>());
        });
        await _unitOfWork.DidNotReceiveWithAnyArgs().LockCurrencyCodeSharedAsync(default!, default);
    }

    [Fact]
    public async Task Port_ActiveCode_TakesOnlyTheSharedLockAndDoesNotSave()
    {
        var existing = Currency.Create(CurrencyId.New(), "EUR", "Euro");
        _currencies.GetByCodeAsync("EUR", Arg.Any<CancellationToken>()).Returns(existing);
        var port = new CurrencyActivation(CreatePolicy(), _unitOfWork);

        var result = await port.EnsureActiveAsync(" eur ", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(existing.Id);
        await _unitOfWork.Received(1).LockCurrencyCodeSharedAsync(" eur ", Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceiveWithAnyArgs().LockCurrencyCodeAsync(default!, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Port_ActiveCodeRemovedWhileWaitingForTheSharedLock_ReturnsNullWithoutTakingTheExclusiveLock()
    {
        var existing = Currency.Create(CurrencyId.New(), "EUR", "Euro");
        _currencies.GetByCodeAsync("EUR", Arg.Any<CancellationToken>()).Returns(existing, (Currency?)null);
        var port = new CurrencyActivation(CreatePolicy(), _unitOfWork);

        var result = await port.EnsureActiveAsync("EUR", CancellationToken.None);

        result.Should().BeNull();
        await _unitOfWork.DidNotReceiveWithAnyArgs().LockCurrencyCodeAsync(default!, default);
    }

    [Fact]
    public async Task Port_BlankCode_TakesNoLock()
    {
        var port = new CurrencyActivation(CreatePolicy(), _unitOfWork);

        await port.EnsureActiveAsync("  ", CancellationToken.None);

        await _unitOfWork.DidNotReceiveWithAnyArgs().LockCurrencyCodeAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().LockCurrencyCodeSharedAsync(default!, default);
    }

    [Fact]
    public async Task Port_UnknownCode_ReturnsNullAndDoesNotSave()
    {
        var port = new CurrencyActivation(CreatePolicy(), _unitOfWork);

        var result = await port.EnsureActiveAsync("ZZ9", CancellationToken.None);

        result.Should().BeNull();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
