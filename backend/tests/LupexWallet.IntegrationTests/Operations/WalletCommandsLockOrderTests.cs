using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// Lock order of the Wallets commands whose subscribers (Operations, BalanceHistory) take the wallet advisory lock in
/// the same transaction (ADR-0001, §4): DELETE /wallets/{id} and PUT /wallets/{id}/currency must take that lock before
/// they touch the wallet row. Operations commands take the lock first and the row second, so a Wallets command that
/// updates the row first and takes the lock in a subscriber forms a cycle (PostgreSQL 40P01, a 500).
/// The interleaving is forced: the test holds the wallet lock, the Operations request queues on it first, the Wallets
/// request second; on release the queue is served in that order. A command that locks first queues without holding
/// the row and then runs after the Operations request; a command that updates the row first queues holding it, and the
/// Operations request, granted the lock first, blocks on that row: a deadlock.
/// </summary>
public sealed class WalletCommandsLockOrderTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static DateOnly Today => RecordLifecycleTestHelpers.Today;

    private async Task<(WalletResponse Wallet, Guid IncomeTypeId)> CreateWalletWithStatedBalanceOnlyAsync()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной");
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: Today.AddDays(-20), name: "Второй");
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");

        // A stated balance is not wallet history, so the wallet can still be deleted or change currency.
        var (_, correction) = await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-10), 150m);
        correction.IsCorrection.Should().BeTrue();
        return (wallet, incomeTypeId);
    }

    private Task<HttpResponseMessage> PutStatedBalance(Guid walletId, DateOnly date, decimal amount) =>
        Client.PutAsJsonAsync($"/api/v1/wallets/{walletId}/stated-balances/{date:yyyy-MM-dd}", new { Amount = amount });

    private Task<HttpResponseMessage> PostIncome(Guid walletId, Guid typeId, decimal amount, DateOnly date) =>
        Client.PostAsJsonAsync("/api/v1/operations", new { WalletId = walletId, OperationTypeId = typeId, Amount = amount, OperationDate = date });

    private async Task<bool> WalletExistsAsync(Guid walletId) =>
        (await Client.GetAsync($"/api/v1/wallets/{walletId}")).StatusCode == HttpStatusCode.OK;

    [Fact]
    public async Task DeleteWallet_QueuedBehindStatedBalanceOfTheSameWallet_BothSucceedWithoutDeadlock()
    {
        var (wallet, _) = await CreateWalletWithStatedBalanceOnlyAsync();

        var responses = await Fixture.SendQueuedBehindWalletLockAsync(
            wallet.Id,
            () => PutStatedBalance(wallet.Id, Today.AddDays(-5), 500m),
            () => Client.DeleteAsync($"/api/v1/wallets/{wallet.Id}"));

        responses[0].StatusCode.Should().Be(HttpStatusCode.Created, "the statement ran first, under the lock");
        responses[1].StatusCode.Should().Be(HttpStatusCode.NoContent, "a stated balance is not history: the wallet is deleted after it");
        (await WalletExistsAsync(wallet.Id)).Should().BeFalse();
        (await Fixture.CorrectionRowCountAsync(wallet.Id)).Should().Be(0, "the corrections are removed with the wallet");
    }

    [Fact]
    public async Task DeleteWallet_QueuedBehindOperationOfTheSameWallet_OperationWinsAndDeleteIsRejectedAsConflict()
    {
        var (wallet, incomeTypeId) = await CreateWalletWithStatedBalanceOnlyAsync();

        var responses = await Fixture.SendQueuedBehindWalletLockAsync(
            wallet.Id,
            () => PostIncome(wallet.Id, incomeTypeId, 30m, Today.AddDays(-3)),
            () => Client.DeleteAsync($"/api/v1/wallets/{wallet.Id}"));

        responses[0].StatusCode.Should().Be(HttpStatusCode.Created);
        responses[1].StatusCode.Should().Be(HttpStatusCode.Conflict, "the operation created under the lock is history, seen by the delete that follows it");
        (await WalletExistsAsync(wallet.Id)).Should().BeTrue();
        await Fixture.AssertWalletInvariantAsync(wallet.Id);
    }

    [Fact]
    public async Task ChangeWalletCurrency_QueuedBehindStatedBalanceOfTheSameWallet_BothSucceedWithoutDeadlock()
    {
        var (wallet, _) = await CreateWalletWithStatedBalanceOnlyAsync();
        var newCurrency = await Client.CreateCurrencyAsync("CT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");

        var responses = await Fixture.SendQueuedBehindWalletLockAsync(
            wallet.Id,
            () => PutStatedBalance(wallet.Id, Today.AddDays(-5), 500m),
            () => Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = newCurrency.Id }));

        responses[0].StatusCode.Should().Be(HttpStatusCode.Created, "the statement ran first, under the lock");
        responses[1].StatusCode.Should().Be(HttpStatusCode.OK, "a stated balance is not history: the currency changes after it");
        var changed = (await responses[1].Content.ReadFromJsonAsync<WalletResponse>())!;
        changed.CurrencyId.Should().Be(newCurrency.Id);
        var corrections = (await Client.ListOperationsAsync(wallet.Id)).Where(x => x.IsCorrection).ToList();
        corrections.Should().OnlyContain(x => x.Amount.CurrencyId == newCurrency.Id, "the corrections follow the wallet currency");
        await Fixture.AssertWalletInvariantAsync(wallet.Id);
    }

    [Fact]
    public async Task ChangeWalletCurrency_QueuedBehindOperationOfTheSameWallet_OperationWinsAndChangeIsRejectedAsConflict()
    {
        var (wallet, incomeTypeId) = await CreateWalletWithStatedBalanceOnlyAsync();
        var newCurrency = await Client.CreateCurrencyAsync("CT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");

        var responses = await Fixture.SendQueuedBehindWalletLockAsync(
            wallet.Id,
            () => PostIncome(wallet.Id, incomeTypeId, 30m, Today.AddDays(-3)),
            () => Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = newCurrency.Id }));

        responses[0].StatusCode.Should().Be(HttpStatusCode.Created);
        responses[1].StatusCode.Should().Be(HttpStatusCode.Conflict, "the operation created under the lock is history, seen by the change that follows it");
        var unchanged = (await (await Client.GetAsync($"/api/v1/wallets/{wallet.Id}")).Content.ReadFromJsonAsync<WalletResponse>())!;
        unchanged.CurrencyId.Should().Be(wallet.CurrencyId);
        await Fixture.AssertWalletInvariantAsync(wallet.Id);
    }
}
