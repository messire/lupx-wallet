using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// CreateTransferCommandHandler — согласование валют кошельков (раздел 4 требований),
/// выбор более строгой AccountingStartDate из двух кошельков и атомарное применение
/// дельты к обоим кошелькам.
/// </summary>
public sealed class CreateTransferCommandHandlerTests
{
    private static readonly CurrencyId CurrencyA = new(Guid.NewGuid());
    private static readonly CurrencyId CurrencyB = new(Guid.NewGuid());

    private readonly IOperationRepository _operationRepository = Substitute.For<IOperationRepository>();
    private readonly ITransferRepository _transferRepository = Substitute.For<ITransferRepository>();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly IOperationTypeLookup _operationTypeLookup = Substitute.For<IOperationTypeLookup>();
    private readonly IWalletBalanceGateway _walletBalanceGateway = Substitute.For<IWalletBalanceGateway>();

    private CreateTransferCommandHandler CreateHandler() =>
        new(_operationRepository, _transferRepository, _unitOfWork, _operationTypeLookup, _walletBalanceGateway);

    private static WalletBalanceInfo Wallet(CurrencyId currency, DateOnly accountingStartDate) => new(
        WalletId.New(), currency, new Money(0m, currency), new Money(0m, currency), accountingStartDate, IsArchived: false);

    private static OperationTypeLookupResult TransferType() =>
        new(OperationTypeId.New(), "Перевод", new OperationBehaviorKindId(Guid.NewGuid()), "Transfer", IsActive: true);

    [Fact]
    public async Task Handle_MismatchedWalletCurrencies_ThrowsTransferCurrencyMismatchException()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1);
        var source = Wallet(CurrencyA, start);
        var target = Wallet(CurrencyB, start);
        _walletBalanceGateway.GetAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
        _walletBalanceGateway.GetAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        var command = new CreateTransferCommand(source.Id.Value, target.Id.Value, 10m, DateOnly.FromDateTime(DateTime.UtcNow));

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<TransferCurrencyMismatchException>();
    }

    [Fact]
    public async Task Handle_SourceWalletNotFound_ThrowsWalletReferenceNotFoundException()
    {
        _walletBalanceGateway.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns((WalletBalanceInfo?)null);
        var command = new CreateTransferCommand(Guid.NewGuid(), Guid.NewGuid(), 10m, DateOnly.FromDateTime(DateTime.UtcNow));

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<WalletReferenceNotFoundException>();
    }

    [Fact]
    public async Task Handle_NoActiveTransferOperationType_ThrowsNoActiveOperationTypeForTransferException()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1);
        var source = Wallet(CurrencyA, start);
        var target = Wallet(CurrencyA, start);
        _walletBalanceGateway.GetAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
        _walletBalanceGateway.GetAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _operationTypeLookup.FindActiveByBehaviorKindCodeAsync("Transfer", Arg.Any<CancellationToken>())
            .Returns((OperationTypeLookupResult?)null);
        var command = new CreateTransferCommand(source.Id.Value, target.Id.Value, 10m, DateOnly.FromDateTime(DateTime.UtcNow));

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NoActiveOperationTypeForTransferException>();
    }

    [Fact]
    public async Task Handle_DateBeforeStricterAccountingStartDate_ThrowsOperationDateOutOfRangeException()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var source = Wallet(CurrencyA, today.AddDays(-5));
        var target = Wallet(CurrencyA, today.AddDays(-2)); // Более строгая (поздняя) граница.
        _walletBalanceGateway.GetAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
        _walletBalanceGateway.GetAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _operationTypeLookup.FindActiveByBehaviorKindCodeAsync("Transfer", Arg.Any<CancellationToken>()).Returns(TransferType());
        var command = new CreateTransferCommand(source.Id.Value, target.Id.Value, 10m, today.AddDays(-3));

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<OperationDateOutOfRangeException>();
    }

    [Fact]
    public async Task Handle_Valid_AppliesOppositeDeltasToBothWalletsAndPersistsAll()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1);
        var source = Wallet(CurrencyA, start);
        var target = Wallet(CurrencyA, start);
        _walletBalanceGateway.GetAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
        _walletBalanceGateway.GetAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _operationTypeLookup.FindActiveByBehaviorKindCodeAsync("Transfer", Arg.Any<CancellationToken>()).Returns(TransferType());
        var command = new CreateTransferCommand(source.Id.Value, target.Id.Value, 60m, DateOnly.FromDateTime(DateTime.UtcNow));

        var dto = await CreateHandler().Handle(command, CancellationToken.None);

        dto.Amount.Should().Be(60m);
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(source.Id, Arg.Is<Money>(m => m.Amount == -60m), Arg.Any<CancellationToken>());
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(target.Id, Arg.Is<Money>(m => m.Amount == 60m), Arg.Any<CancellationToken>());
        _operationRepository.Received(2).Add(Arg.Any<Operation>());
        _transferRepository.Received(1).Add(Arg.Any<Transfer>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
