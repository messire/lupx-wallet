using FluentAssertions;
using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ReferenceData;

/// <summary>
/// ActivateCurrencyCommandHandler — a currency is added to the active set only from the available
/// list (ADR-0004 §7); the code is unique globally (schema.md).
/// </summary>
public sealed class ActivateCurrencyCommandHandlerTests
{
    private readonly ICurrencyRepository _currencies = Substitute.For<ICurrencyRepository>();
    private readonly IAvailableCurrencyRepository _available = Substitute.For<IAvailableCurrencyRepository>();
    private readonly IReferenceDataUnitOfWork _unitOfWork = Substitute.For<IReferenceDataUnitOfWork>();

    private ActivateCurrencyCommandHandler CreateHandler() =>
        new(new CurrencyActivationPolicy(_currencies, _available), _unitOfWork);

    [Fact]
    public async Task Handle_CodeAlreadyActive_ThrowsCurrencyCodeAlreadyExistsExceptionAndDoesNotPersist()
    {
        _currencies.CodeExistsAsync("USD", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateHandler().Handle(new ActivateCurrencyCommand("USD"), CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyCodeAlreadyExistsException>();
        _currencies.DidNotReceive().Add(Arg.Any<Currency>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CodeNotAvailable_ThrowsCurrencyNotAvailableExceptionAndDoesNotPersist()
    {
        _available.GetByCodeAsync("BTC", Arg.Any<CancellationToken>()).Returns((AvailableCurrency?)null);

        var act = () => CreateHandler().Handle(new ActivateCurrencyCommand("BTC"), CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyNotAvailableException>();
        _currencies.DidNotReceive().Add(Arg.Any<Currency>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AvailableCode_CreatesCurrencyWithServiceNameAndPersists()
    {
        _available.GetByCodeAsync("EUR", Arg.Any<CancellationToken>())
            .Returns(AvailableCurrency.Create("EUR", "Euro", DateTimeOffset.UtcNow));

        var dto = await CreateHandler().Handle(new ActivateCurrencyCommand(" eur "), CancellationToken.None);

        dto.Code.Should().Be("EUR");
        dto.Name.Should().Be("Euro");
        _currencies.Received(1).Add(Arg.Is<Currency>(c => c.Code == "EUR" && c.Name == "Euro"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
