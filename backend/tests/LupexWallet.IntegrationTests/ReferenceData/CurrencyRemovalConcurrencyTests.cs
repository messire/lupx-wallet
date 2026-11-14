using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace LupexWallet.IntegrationTests.ReferenceData;

/// <summary>
/// Removing an active currency racing with a wallet created on it (ADR-0004 §7): both take the advisory lock of
/// the code, so the usage check cannot run before a wallet that commits afterwards. The race is forced: the test
/// holds the lock, both requests reach it and wait, then the lock is released. Whoever gets it first decides —
/// either the wallet is created and the removal is a 409, or the currency is removed and the wallet is rejected —
/// but a wallet must never be left on a removed currency.
/// </summary>
public sealed class CurrencyRemovalConcurrencyTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DeleteCurrency_ConcurrentWithWalletCreationOnIt_NeverLeavesAWalletOnARemovedCurrency()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR", "Euro");
        var walletType = await Client.CreateWalletTypeAsync($"Тип-{Guid.NewGuid():N}");
        using var secondClient = Fixture.Factory.CreateClient();
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Fixture.Token);

        await using var lockConnection = await Fixture.OpenConnectionAsync();
        await using var monitorConnection = await Fixture.OpenConnectionAsync();
        await using var transaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('currency:EUR'))", lockConnection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        var delete = Client.DeleteAsync($"/api/v1/currencies/{currency.Id}");
        var createWallet = secondClient.PostAsJsonAsync("/api/v1/wallets", new
        {
            Name = "Кошелек",
            WalletTypeId = walletType.Id,
            CurrencyCode = "EUR",
            InitialBalanceAmount = 0m,
            AccountingStartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
        });

        try
        {
            await WaitForBlockedBackendsAsync(monitorConnection, expected: 2, delete, createWallet);
        }
        finally
        {
            await transaction.CommitAsync();
        }

        var deleteResponse = await delete;
        var walletResponse = await createWallet;
        var outcome = $"delete {(int)deleteResponse.StatusCode}, wallet {(int)walletResponse.StatusCode}";

        if (deleteResponse.StatusCode == HttpStatusCode.NoContent)
        {
            walletResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, outcome);
            var page = await Client.GetFromJsonAsync<CurrencyPage>("/api/v1/currencies?limit=100");
            page!.Data.Should().BeEmpty(outcome);
        }
        else
        {
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, outcome);
            walletResponse.StatusCode.Should().Be(HttpStatusCode.Created, outcome);
            var wallet = (await walletResponse.Content.ReadFromJsonAsync<WalletResponse>())!;
            wallet.CurrencyId.Should().Be(currency.Id, outcome);
            var page = await Client.GetFromJsonAsync<CurrencyPage>("/api/v1/currencies?limit=100");
            page!.Data.Should().ContainSingle(c => c.Id == currency.Id, outcome);
        }
    }

    /// <summary>Polls pg_stat_activity outside any transaction (inside one the view is a frozen snapshot).</summary>
    private static async Task WaitForBlockedBackendsAsync(NpgsqlConnection monitorConnection, int expected, params Task[] requests)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() " +
                "AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()", monitorConnection);
            if ((long)(await command.ExecuteScalarAsync())! >= expected)
            {
                return;
            }

            requests.Any(r => r.IsCompleted).Should().BeFalse("both requests must reach the currency lock before it is released");
            await Task.Delay(20);
        }

        throw new TimeoutException($"Fewer than {expected} backends blocked on the currency lock within 30 s.");
    }

    private sealed record CurrencyPage(List<CurrencyResponse> Data);
}
