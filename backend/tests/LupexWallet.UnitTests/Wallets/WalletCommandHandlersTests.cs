using FluentAssertions;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using LupexWallet.Wallets.Domain;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Wallets;

/// <summary>
/// Обработчики команд W1.2 (UC-02…UC-06): UpdateWallet/ArchiveWallet/SetPrimaryWallet/
/// DeleteWallet/ChangeWalletCurrency. Доменные инварианты уже покрыты WalletTests —
/// здесь проверяется маршрутизация (NotFound, делегирование в Wallet/PrimaryWalletPolicy,
/// вызов SaveChangesAsync, агрегация IWalletHistorySource).
/// </summary>
public sealed class WalletCommandHandlersTests
{
    private static Money Usd(decimal amount) => new(amount, new CurrencyId(Guid.NewGuid()));

    private static Wallet CreateWallet(bool isFirstWallet = false) =>
        Wallet.Create(
            id: new WalletId(Guid.NewGuid()),
            name: "Кошелек",
            walletTypeId: new WalletTypeId(Guid.NewGuid()),
            initialBalance: Usd(100m),
            accountingStartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            purposeDescription: null,
            includeInTotal: false,
            displayOrder: 0,
            color: null,
            icon: null,
            isFirstWallet: isFirstWallet);

    private static IWalletRepository RepositoryReturning(Wallet? wallet)
    {
        var repository = Substitute.For<IWalletRepository>();
        repository.GetByIdAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns(wallet);
        return repository;
    }

    private static IEnumerable<IWalletHistorySource> HistorySources(params bool[] results)
    {
        return results.Select(result =>
        {
            var source = Substitute.For<IWalletHistorySource>();
            source.HasHistoryAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns(result);
            return source;
        }).ToArray();
    }

    // W2.5, случай d: walletTypeId/currencyId проверяются на существование и активность.
    private static IWalletTypeLookup WalletTypeLookupReturning(bool? isActive)
    {
        var lookup = Substitute.For<IWalletTypeLookup>();
        lookup.GetAsync(Arg.Any<WalletTypeId>(), Arg.Any<CancellationToken>())
            .Returns(isActive is null ? null : new WalletTypeLookupResult(new WalletTypeId(Guid.NewGuid()), "Тип", isActive.Value));
        return lookup;
    }

    private static ICurrencyLookup CurrencyLookupReturning(bool? isActive)
    {
        var lookup = Substitute.For<ICurrencyLookup>();
        lookup.GetAsync(Arg.Any<CurrencyId>(), Arg.Any<CancellationToken>())
            .Returns(isActive is null ? null : new CurrencyLookupResult(new CurrencyId(Guid.NewGuid()), "USD", isActive.Value));
        return lookup;
    }

    // ---- CreateWalletCommand ----

    [Fact]
    public async Task CreateWallet_UnknownWalletType_ThrowsWalletTypeReferenceNotFoundException()
    {
        var repository = Substitute.For<IWalletRepository>();
        var policy = new PrimaryWalletPolicy(repository, Substitute.For<IWalletsUnitOfWork>());
        var handler = new CreateWalletCommandHandler(
            repository, Substitute.For<IWalletsUnitOfWork>(), policy,
            WalletTypeLookupReturning(null), CurrencyLookupReturning(true));

        var act = () => handler.Handle(
            new CreateWalletCommand("Кошелек", Guid.NewGuid(), Guid.NewGuid(), 0m, DateOnly.FromDateTime(DateTime.UtcNow), null, true, 0, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<WalletTypeReferenceNotFoundException>();
    }

    [Fact]
    public async Task CreateWallet_InactiveWalletType_ThrowsWalletTypeReferenceInactiveException()
    {
        var repository = Substitute.For<IWalletRepository>();
        var policy = new PrimaryWalletPolicy(repository, Substitute.For<IWalletsUnitOfWork>());
        var handler = new CreateWalletCommandHandler(
            repository, Substitute.For<IWalletsUnitOfWork>(), policy,
            WalletTypeLookupReturning(false), CurrencyLookupReturning(true));

        var act = () => handler.Handle(
            new CreateWalletCommand("Кошелек", Guid.NewGuid(), Guid.NewGuid(), 0m, DateOnly.FromDateTime(DateTime.UtcNow), null, true, 0, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<WalletTypeReferenceInactiveException>();
    }

    [Fact]
    public async Task CreateWallet_UnknownCurrency_ThrowsCurrencyReferenceNotFoundException()
    {
        var repository = Substitute.For<IWalletRepository>();
        var policy = new PrimaryWalletPolicy(repository, Substitute.For<IWalletsUnitOfWork>());
        var handler = new CreateWalletCommandHandler(
            repository, Substitute.For<IWalletsUnitOfWork>(), policy,
            WalletTypeLookupReturning(true), CurrencyLookupReturning(null));

        var act = () => handler.Handle(
            new CreateWalletCommand("Кошелек", Guid.NewGuid(), Guid.NewGuid(), 0m, DateOnly.FromDateTime(DateTime.UtcNow), null, true, 0, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyReferenceNotFoundException>();
    }

    [Fact]
    public async Task CreateWallet_InactiveCurrency_ThrowsCurrencyReferenceInactiveException()
    {
        var repository = Substitute.For<IWalletRepository>();
        var policy = new PrimaryWalletPolicy(repository, Substitute.For<IWalletsUnitOfWork>());
        var handler = new CreateWalletCommandHandler(
            repository, Substitute.For<IWalletsUnitOfWork>(), policy,
            WalletTypeLookupReturning(true), CurrencyLookupReturning(false));

        var act = () => handler.Handle(
            new CreateWalletCommand("Кошелек", Guid.NewGuid(), Guid.NewGuid(), 0m, DateOnly.FromDateTime(DateTime.UtcNow), null, true, 0, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyReferenceInactiveException>();
    }

    [Fact]
    public async Task CreateWallet_ValidReferences_CreatesAndSaves()
    {
        var repository = Substitute.For<IWalletRepository>();
        var unitOfWork = Substitute.For<IWalletsUnitOfWork>();
        var policy = new PrimaryWalletPolicy(repository, unitOfWork);
        var handler = new CreateWalletCommandHandler(
            repository, unitOfWork, policy, WalletTypeLookupReturning(true), CurrencyLookupReturning(true));

        var dto = await handler.Handle(
            new CreateWalletCommand("Кошелек", Guid.NewGuid(), Guid.NewGuid(), 100m, DateOnly.FromDateTime(DateTime.UtcNow), null, true, 0, null, null),
            CancellationToken.None);

        dto.Name.Should().Be("Кошелек");
        repository.Received(1).Add(Arg.Any<Wallet>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- UpdateWalletCommand ----

    [Fact]
    public async Task UpdateWallet_UnknownWallet_ThrowsWalletNotFoundException()
    {
        var repository = RepositoryReturning(null);
        var handler = new UpdateWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), WalletTypeLookupReturning(true));

        var act = () => handler.Handle(
            new UpdateWalletCommand(Guid.NewGuid(), "Имя", Guid.NewGuid(), null, true, 0, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<WalletNotFoundException>();
    }

    [Fact]
    public async Task UpdateWallet_UnknownWalletType_ThrowsWalletTypeReferenceNotFoundException()
    {
        var wallet = CreateWallet();
        var repository = RepositoryReturning(wallet);
        var handler = new UpdateWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), WalletTypeLookupReturning(null));

        var act = () => handler.Handle(
            new UpdateWalletCommand(wallet.Id.Value, "Имя", Guid.NewGuid(), null, true, 0, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<WalletTypeReferenceNotFoundException>();
    }

    [Fact]
    public async Task UpdateWallet_InactiveWalletType_ThrowsWalletTypeReferenceInactiveException()
    {
        var wallet = CreateWallet();
        var repository = RepositoryReturning(wallet);
        var handler = new UpdateWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), WalletTypeLookupReturning(false));

        var act = () => handler.Handle(
            new UpdateWalletCommand(wallet.Id.Value, "Имя", Guid.NewGuid(), null, true, 0, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<WalletTypeReferenceInactiveException>();
    }

    [Fact]
    public async Task UpdateWallet_ExistingWallet_UpdatesDetailsAndSaves()
    {
        var wallet = CreateWallet();
        var repository = RepositoryReturning(wallet);
        var unitOfWork = Substitute.For<IWalletsUnitOfWork>();
        var handler = new UpdateWalletCommandHandler(repository, unitOfWork, WalletTypeLookupReturning(true));
        var newWalletTypeId = Guid.NewGuid();

        var dto = await handler.Handle(
            new UpdateWalletCommand(wallet.Id.Value, "Новое имя", newWalletTypeId, "Описание", true, 5, "#fff", "icon"),
            CancellationToken.None);

        dto.Name.Should().Be("Новое имя");
        dto.WalletTypeId.Should().Be(newWalletTypeId);
        dto.DisplayOrder.Should().Be(5);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- ArchiveWalletCommand ----

    [Fact]
    public async Task ArchiveWallet_UnknownWallet_ThrowsWalletNotFoundException()
    {
        var repository = RepositoryReturning(null);
        var handler = new ArchiveWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>());

        var act = () => handler.Handle(new ArchiveWalletCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<WalletNotFoundException>();
    }

    [Fact]
    public async Task ArchiveWallet_PrimaryWallet_ThrowsCannotArchivePrimaryWalletException()
    {
        var wallet = CreateWallet(isFirstWallet: true);
        var repository = RepositoryReturning(wallet);
        var handler = new ArchiveWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>());

        var act = () => handler.Handle(new ArchiveWalletCommand(wallet.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<CannotArchivePrimaryWalletException>();
    }

    [Fact]
    public async Task ArchiveWallet_NonPrimaryWallet_ArchivesAndSaves()
    {
        var wallet = CreateWallet(isFirstWallet: false);
        var repository = RepositoryReturning(wallet);
        var unitOfWork = Substitute.For<IWalletsUnitOfWork>();
        var handler = new ArchiveWalletCommandHandler(repository, unitOfWork);

        var dto = await handler.Handle(new ArchiveWalletCommand(wallet.Id.Value), CancellationToken.None);

        dto.IsArchived.Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- SetPrimaryWalletCommand ----

    [Fact]
    public async Task SetPrimaryWallet_UnknownWallet_ThrowsWalletNotFoundException()
    {
        var repository = RepositoryReturning(null);
        var policy = new PrimaryWalletPolicy(repository, Substitute.For<IWalletsUnitOfWork>());
        var handler = new SetPrimaryWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), policy);

        var act = () => handler.Handle(new SetPrimaryWalletCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<WalletNotFoundException>();
    }

    [Fact]
    public async Task SetPrimaryWallet_ArchivedWallet_ThrowsCannotSetArchivedWalletAsPrimaryException()
    {
        var wallet = CreateWallet(isFirstWallet: false);
        wallet.Archive();
        var repository = RepositoryReturning(wallet);
        repository.GetCurrentPrimaryAsync(Arg.Any<CancellationToken>()).Returns((Wallet?)null);
        var policy = new PrimaryWalletPolicy(repository, Substitute.For<IWalletsUnitOfWork>());
        var handler = new SetPrimaryWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), policy);

        var act = () => handler.Handle(new SetPrimaryWalletCommand(wallet.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<CannotSetArchivedWalletAsPrimaryException>();
    }

    [Fact]
    public async Task SetPrimaryWallet_ActiveWallet_TransfersPrimaryFlagAndForcesIncludeInTotal()
    {
        var previousPrimary = CreateWallet(isFirstWallet: true);
        var target = CreateWallet(isFirstWallet: false);
        var repository = RepositoryReturning(target);
        repository.GetCurrentPrimaryAsync(Arg.Any<CancellationToken>()).Returns(previousPrimary);
        var unitOfWork = Substitute.For<IWalletsUnitOfWork>();
        var policy = new PrimaryWalletPolicy(repository, unitOfWork);
        var handler = new SetPrimaryWalletCommandHandler(repository, unitOfWork, policy);

        var dto = await handler.Handle(new SetPrimaryWalletCommand(target.Id.Value), CancellationToken.None);

        dto.IsPrimary.Should().BeTrue();
        dto.IncludeInTotal.Should().BeTrue("Q8: у основного кошелька IncludeInTotal нельзя выключить");
        previousPrimary.IsPrimary.Should().BeFalse("прежний основной теряет флаг при передаче другому кошельку");
        // Два отдельных SaveChangesAsync: снятие флага со старого основного (в
        // PrimaryWalletPolicy — обязательно ДО назначения нового из-за
        // ux_wallets_single_primary, частичного уникального индекса, не deferrable) и
        // назначение нового (в самом обработчике).
        await unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- DeleteWalletCommand ----

    [Fact]
    public async Task DeleteWallet_UnknownWallet_ThrowsWalletNotFoundException()
    {
        var repository = RepositoryReturning(null);
        var handler = new DeleteWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), HistorySources(false));

        var act = () => handler.Handle(new DeleteWalletCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<WalletNotFoundException>();
    }

    [Fact]
    public async Task DeleteWallet_NoRegisteredHistorySources_ThrowsInvalidOperationException()
    {
        var wallet = CreateWallet();
        var repository = RepositoryReturning(wallet);
        var handler = new DeleteWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), Array.Empty<IWalletHistorySource>());

        var act = () => handler.Handle(new DeleteWalletCommand(wallet.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteWallet_HasHistory_ThrowsWalletDeletionNotAllowedException()
    {
        var wallet = CreateWallet(isFirstWallet: false);
        var repository = RepositoryReturning(wallet);
        var handler = new DeleteWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), HistorySources(false, true));

        var act = () => handler.Handle(new DeleteWalletCommand(wallet.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<WalletDeletionNotAllowedException>();
    }

    [Fact]
    public async Task DeleteWallet_PrimaryWalletWithoutHistory_ThrowsCannotDeletePrimaryWalletException()
    {
        var wallet = CreateWallet(isFirstWallet: true);
        var repository = RepositoryReturning(wallet);
        var handler = new DeleteWalletCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), HistorySources(false, false));

        var act = () => handler.Handle(new DeleteWalletCommand(wallet.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<CannotDeletePrimaryWalletException>();
    }

    [Fact]
    public async Task DeleteWallet_NonPrimaryWithoutHistory_RemovesAndSaves()
    {
        var wallet = CreateWallet(isFirstWallet: false);
        var repository = RepositoryReturning(wallet);
        var unitOfWork = Substitute.For<IWalletsUnitOfWork>();
        var handler = new DeleteWalletCommandHandler(repository, unitOfWork, HistorySources(false, false));

        await handler.Handle(new DeleteWalletCommand(wallet.Id.Value), CancellationToken.None);

        repository.Received(1).Remove(wallet);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- ChangeWalletCurrencyCommand ----

    [Fact]
    public async Task ChangeWalletCurrency_UnknownWallet_ThrowsWalletNotFoundException()
    {
        var repository = RepositoryReturning(null);
        var handler = new ChangeWalletCurrencyCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), HistorySources(false));

        var act = () => handler.Handle(new ChangeWalletCurrencyCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<WalletNotFoundException>();
    }

    [Fact]
    public async Task ChangeWalletCurrency_HasHistory_ThrowsWalletCurrencyChangeNotAllowedException()
    {
        var wallet = CreateWallet();
        var repository = RepositoryReturning(wallet);
        var handler = new ChangeWalletCurrencyCommandHandler(repository, Substitute.For<IWalletsUnitOfWork>(), HistorySources(true, false));

        var act = () => handler.Handle(new ChangeWalletCurrencyCommand(wallet.Id.Value, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<WalletCurrencyChangeNotAllowedException>();
    }

    [Fact]
    public async Task ChangeWalletCurrency_NoHistory_ChangesCurrencyAndSaves()
    {
        var wallet = CreateWallet();
        var repository = RepositoryReturning(wallet);
        var unitOfWork = Substitute.For<IWalletsUnitOfWork>();
        var handler = new ChangeWalletCurrencyCommandHandler(repository, unitOfWork, HistorySources(false, false));
        var newCurrencyId = Guid.NewGuid();

        var dto = await handler.Handle(new ChangeWalletCurrencyCommand(wallet.Id.Value, newCurrencyId), CancellationToken.None);

        dto.CurrencyId.Should().Be(newCurrencyId);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
