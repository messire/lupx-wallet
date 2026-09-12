using FluentAssertions;
using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ReferenceData;

/// <summary>
/// W2.5, случай c (ADR-0009) — Delete{WalletType|OperationType|Currency}CommandHandler
/// агрегируют IEnumerable&lt;IReferenceItemUsageProbe&gt; вместо захардкоженного isUsed:
/// false. Точечная маршрутизация (NotFound/isUsed/успех) — сами доменные инварианты
/// (EnsureCanBeDeleted) уже покрыты WalletTypeTests/OperationTypeTests/CurrencyTests.
/// </summary>
public sealed class DeleteCommandHandlersTests
{
    private static IEnumerable<IReferenceItemUsageProbe> Probes(params bool[] results)
    {
        return results.Select(result =>
        {
            var probe = Substitute.For<IReferenceItemUsageProbe>();
            probe.IsUsedAsync(Arg.Any<ReferenceItemKind>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(result);
            return probe;
        }).ToArray();
    }

    // ---- DeleteWalletTypeCommand ----

    [Fact]
    public async Task DeleteWalletType_NoRegisteredProbes_ThrowsInvalidOperationException()
    {
        var walletType = WalletType.Create(WalletTypeId.New(), "Наличные");
        var repository = Substitute.For<IWalletTypeRepository>();
        repository.GetByIdAsync(Arg.Any<WalletTypeId>(), Arg.Any<CancellationToken>()).Returns(walletType);
        var handler = new DeleteWalletTypeCommandHandler(
            repository, Substitute.For<IReferenceDataUnitOfWork>(), Array.Empty<IReferenceItemUsageProbe>());

        var act = () => handler.Handle(new DeleteWalletTypeCommand(walletType.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteWalletType_Used_ThrowsReferenceItemInUseException()
    {
        var walletType = WalletType.Create(WalletTypeId.New(), "Наличные");
        var repository = Substitute.For<IWalletTypeRepository>();
        repository.GetByIdAsync(Arg.Any<WalletTypeId>(), Arg.Any<CancellationToken>()).Returns(walletType);
        var handler = new DeleteWalletTypeCommandHandler(
            repository, Substitute.For<IReferenceDataUnitOfWork>(), Probes(false, true));

        var act = () => handler.Handle(new DeleteWalletTypeCommand(walletType.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<ReferenceItemInUseException>();
    }

    [Fact]
    public async Task DeleteWalletType_NotUsed_RemovesAndSaves()
    {
        var walletType = WalletType.Create(WalletTypeId.New(), "Наличные");
        var repository = Substitute.For<IWalletTypeRepository>();
        repository.GetByIdAsync(Arg.Any<WalletTypeId>(), Arg.Any<CancellationToken>()).Returns(walletType);
        var unitOfWork = Substitute.For<IReferenceDataUnitOfWork>();
        var handler = new DeleteWalletTypeCommandHandler(repository, unitOfWork, Probes(false, false));

        await handler.Handle(new DeleteWalletTypeCommand(walletType.Id.Value), CancellationToken.None);

        repository.Received(1).Remove(walletType);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- DeleteOperationTypeCommand ----

    [Fact]
    public async Task DeleteOperationType_Used_ThrowsReferenceItemInUseException()
    {
        var operationType = OperationType.Create(OperationTypeId.New(), "Зарплата", new OperationBehaviorKindId(Guid.NewGuid()));
        var repository = Substitute.For<IOperationTypeRepository>();
        repository.GetByIdAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(operationType);
        var handler = new DeleteOperationTypeCommandHandler(
            repository, Substitute.For<IReferenceDataUnitOfWork>(), Probes(true));

        var act = () => handler.Handle(new DeleteOperationTypeCommand(operationType.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<ReferenceItemInUseException>();
    }

    [Fact]
    public async Task DeleteOperationType_NotUsed_RemovesAndSaves()
    {
        var operationType = OperationType.Create(OperationTypeId.New(), "Зарплата", new OperationBehaviorKindId(Guid.NewGuid()));
        var repository = Substitute.For<IOperationTypeRepository>();
        repository.GetByIdAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(operationType);
        var unitOfWork = Substitute.For<IReferenceDataUnitOfWork>();
        var handler = new DeleteOperationTypeCommandHandler(repository, unitOfWork, Probes(false));

        await handler.Handle(new DeleteOperationTypeCommand(operationType.Id.Value), CancellationToken.None);

        repository.Received(1).Remove(operationType);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- DeleteCurrencyCommand ----

    [Fact]
    public async Task DeleteCurrency_Used_ThrowsReferenceItemInUseException()
    {
        var currency = Currency.Create(CurrencyId.New(), "USD", "Доллар США");
        var repository = Substitute.For<ICurrencyRepository>();
        repository.GetByIdAsync(Arg.Any<CurrencyId>(), Arg.Any<CancellationToken>()).Returns(currency);
        var handler = new DeleteCurrencyCommandHandler(
            repository, Substitute.For<IReferenceDataUnitOfWork>(), Probes(false, false, true));

        var act = () => handler.Handle(new DeleteCurrencyCommand(currency.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<ReferenceItemInUseException>();
    }

    [Fact]
    public async Task DeleteCurrency_NotUsed_RemovesAndSaves()
    {
        var currency = Currency.Create(CurrencyId.New(), "USD", "Доллар США");
        var repository = Substitute.For<ICurrencyRepository>();
        repository.GetByIdAsync(Arg.Any<CurrencyId>(), Arg.Any<CancellationToken>()).Returns(currency);
        var unitOfWork = Substitute.For<IReferenceDataUnitOfWork>();
        var handler = new DeleteCurrencyCommandHandler(repository, unitOfWork, Probes(false, false, false));

        await handler.Handle(new DeleteCurrencyCommand(currency.Id.Value), CancellationToken.None);

        repository.Received(1).Remove(currency);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
