using FluentAssertions;
using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ReferenceData;

/// <summary>
/// CreateCurrencyCommandHandler — код валюты уникален глобально, включая неактивные записи
/// (schema.md, см. CurrencyCodeAlreadyExistsException).
/// </summary>
public sealed class CreateCurrencyCommandHandlerTests
{
    private readonly ICurrencyRepository _repository = Substitute.For<ICurrencyRepository>();
    private readonly IReferenceDataUnitOfWork _unitOfWork = Substitute.For<IReferenceDataUnitOfWork>();

    private CreateCurrencyCommandHandler CreateHandler() => new(_repository, _unitOfWork);

    [Fact]
    public async Task Handle_CodeAlreadyExists_ThrowsCurrencyCodeAlreadyExistsExceptionAndDoesNotPersist()
    {
        _repository.CodeExistsAsync("USD", Arg.Any<CancellationToken>()).Returns(true);
        var command = new CreateCurrencyCommand("USD", "Доллар США");

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyCodeAlreadyExistsException>();
        _repository.DidNotReceive().Add(Arg.Any<Currency>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewCode_CreatesAndPersistsCurrency()
    {
        _repository.CodeExistsAsync("EUR", Arg.Any<CancellationToken>()).Returns(false);
        var command = new CreateCurrencyCommand("EUR", "Евро");

        var dto = await CreateHandler().Handle(command, CancellationToken.None);

        dto.Code.Should().Be("EUR");
        _repository.Received(1).Add(Arg.Any<Currency>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
