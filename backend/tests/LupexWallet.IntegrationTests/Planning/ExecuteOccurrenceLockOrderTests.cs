using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Planning;

/// <summary>
/// Review v0.4.0, R7: executing a plan occurrence and deleting the plan's wallet take the same two locks — the wallet
/// advisory lock and the plan row (the Planning subscriber of WalletDeleted unlinks the wallet from the plan). The
/// execution must take them in the order the Wallets command does: the wallet first, then the row. The interleaving is
/// forced: the test holds the wallet lock, DELETE /wallets/{id} queues on it first, the execution second. Taking the
/// plan row before the wallet lock would make the execution hold the row the delete's subscriber needs while waiting
/// for the wallet lock the delete holds — PostgreSQL 40P01, a 500.
/// </summary>
public sealed class ExecuteOccurrenceLockOrderTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task ExecuteOccurrence_QueuedBehindDeleteOfThePlanWallet_NoDeadlock_ExecutionIsRejectedAsConflict()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной");
        var wallet = await Client.CreateWalletAsync(accountingStartDate: Today.AddDays(-20), name: "Под план");
        var typeId = await Client.CreateOperationTypeIdAsync("Expense", $"План-{Guid.NewGuid():N}");
        var date = Today.AddDays(-5);
        var created = await Client.PostAsJsonAsync("/api/v1/planned-transactions", new
        {
            OperationTypeId = typeId,
            Amount = "40",
            CurrencyId = wallet.CurrencyId,
            WalletId = wallet.Id,
            StartDate = date,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var planId = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();

        var responses = await Fixture.SendQueuedBehindWalletLockAsync(
            wallet.Id,
            () => Client.DeleteAsync($"/api/v1/wallets/{wallet.Id}"),
            () => Client.PostAsync(
                $"/api/v1/planned-transactions/{planId}/occurrences/{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}/execute", null));

        responses[0].StatusCode.Should().Be(HttpStatusCode.NoContent, "the empty wallet is deleted first, under the lock");
        responses[1].StatusCode.Should().Be(
            HttpStatusCode.Conflict, "the plan lost its wallet while the execution waited: " + await responses[1].Content.ReadAsStringAsync());

        var plan = await Client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/planned-transactions/{planId}");
        plan.GetProperty("walletId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        plan.GetProperty("hasExecutions").GetBoolean().Should().BeFalse();
    }
}
