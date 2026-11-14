using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.Funds.Application;
using LupexWallet.IntegrationTests.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.IntegrationTests.Funds;

/// <summary>
/// Funds API (docs/api/openapi.yaml, tag Funds; ADR-0010 §4): reserves never change balances,
/// the free-funds check, deficit, the wallet archive/delete guard, close/delete, audit.
/// </summary>
public sealed class FundsApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record Money(string Amount, Guid CurrencyId);
    private sealed record Allocation(Guid WalletId, Money Amount, Money Deficit);
    private sealed record Fund(
        Guid Id, string Name, Guid CurrencyId, Money TargetAmount, Money Reserved, string Progress, Money Deficit,
        Guid? LinkedDebtId, Guid? LinkedLoanId, string Status, bool HasAllocationHistory, List<Allocation> Allocations,
        DateTimeOffset? ClosedAt);
    private sealed record PageMeta(string? NextCursor, bool HasMore);
    private sealed record FundPage(List<Fund> Data, PageMeta Pagination);
    private sealed record WalletReservation(Guid WalletId, Guid CurrencyId, Money Balance, Money Reserved, Money Free, Money Deficit);
    private sealed record Problem(string Type, string Title, int Status, string? Detail);
    private sealed record AuditChange(string Field, string? OldValue, string? NewValue);
    private sealed record AuditEntry(string EntityType, Guid EntityId, string Action, List<AuditChange> Changes);
    private sealed record AuditPage(List<AuditEntry> Data);

    private async Task<Fund> CreateFundAsync(Guid currencyId, decimal target = 1000m, string name = "Отпуск", Guid? linkedDebtId = null)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/funds", new { Name = name, TargetAmount = target.ToString(System.Globalization.CultureInfo.InvariantCulture), CurrencyId = currencyId, LinkedDebtId = linkedDebtId });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Fund>())!;
    }

    private Task<HttpResponseMessage> ReserveAsync(Guid fundId, Guid walletId, string amount) =>
        Client.PostAsJsonAsync($"/api/v1/funds/{fundId}/reserve", new { WalletId = walletId, Amount = amount });

    private Task<HttpResponseMessage> ReleaseAsync(Guid fundId, Guid walletId, string amount) =>
        Client.PostAsJsonAsync($"/api/v1/funds/{fundId}/release", new { WalletId = walletId, Amount = amount });

    private async Task<Fund> ReserveOkAsync(Guid fundId, Guid walletId, string amount)
    {
        var response = await ReserveAsync(fundId, walletId, amount);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Fund>())!;
    }

    private async Task<Fund> GetFundAsync(Guid fundId) =>
        (await Client.GetFromJsonAsync<Fund>($"/api/v1/funds/{fundId}"))!;

    private async Task<WalletReservation> GetWalletReservationAsync(Guid walletId) =>
        (await Client.GetFromJsonAsync<WalletReservation>($"/api/v1/funds/wallet-reservations/{walletId}"))!;

    private static async Task<Problem> ProblemOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Problem>())!;

    private async Task SpendAsync(Guid walletId, decimal amount)
    {
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense", $"Расход-{Guid.NewGuid():N}");
        var response = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = walletId,
            OperationTypeId = expenseTypeId,
            Amount = amount,
            OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateAndGet_NewFund_HasNoReserve()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");

        var created = await CreateFundAsync(currency.Id, 1500.5m);
        var fetched = await GetFundAsync(created.Id);

        fetched.Name.Should().Be("Отпуск");
        fetched.TargetAmount.Amount.Should().Be("1500.5");
        fetched.Reserved.Amount.Should().Be("0");
        fetched.Progress.Should().Be("0");
        fetched.Status.Should().Be("Active");
        fetched.Allocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_InvalidInput_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");

        (await Client.PostAsJsonAsync("/api/v1/funds", new { Name = "", TargetAmount = "10", CurrencyId = currency.Id }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PostAsJsonAsync("/api/v1/funds", new { Name = "Фонд", CurrencyId = currency.Id }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the target amount is required");
        (await Client.PostAsJsonAsync("/api/v1/funds", new { Name = "Фонд", TargetAmount = "0", CurrencyId = currency.Id }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PostAsJsonAsync("/api/v1/funds", new { Name = "Фонд", TargetAmount = "10", CurrencyId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the currency must be active");
        (await Client.PostAsJsonAsync("/api/v1/funds", new { Name = "Фонд", TargetAmount = "10", CurrencyId = currency.Id, LinkedDebtId = Guid.NewGuid(), LinkedLoanId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reserve_DoesNotChangeTheWalletBalance_AndShowsProgress()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var walletA = await Client.CreateWalletAsync(1000m, currencyId: currency.Id, name: "A");
        var walletB = await Client.CreateWalletAsync(500m, currencyId: currency.Id, name: "B");
        var fund = await CreateFundAsync(currency.Id, 1000m);

        await ReserveOkAsync(fund.Id, walletA.Id, "300.25");
        var after = await ReserveOkAsync(fund.Id, walletB.Id, "199.75");

        after.Reserved.Amount.Should().Be("500.00");
        decimal.Parse(after.Progress, System.Globalization.CultureInfo.InvariantCulture).Should().Be(0.5m);
        after.Allocations.Should().HaveCount(2);
        (await Client.GetFromJsonAsync<WalletResponse>($"/api/v1/wallets/{walletA.Id}"))!.CurrentBalance.Amount.Should().Be("1000");

        var summary = await GetWalletReservationAsync(walletA.Id);
        summary.Balance.Amount.Should().Be("1000");
        summary.Reserved.Amount.Should().Be("300.25");
        summary.Free.Amount.Should().Be("699.75");
        summary.Deficit.Amount.Should().Be("0");
    }

    [Fact]
    public async Task Reserve_MoreThanFreeFunds_IncludingOtherFunds_ReturnsConflict()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        var first = await CreateFundAsync(currency.Id, name: "Первый");
        var second = await CreateFundAsync(currency.Id, name: "Второй");
        await ReserveOkAsync(first.Id, wallet.Id, "700");

        var response = await ReserveAsync(second.Id, wallet.Id, "300.01");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemOf(response)).Type.Should().Be("https://lupexwallet/errors/fund-insufficient-free-funds");
        await ReserveOkAsync(second.Id, wallet.Id, "300");
    }

    [Fact]
    public async Task Reserve_WalletInAnotherCurrency_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var usd = await Client.CreateCurrencyAsync("USD");
        var eur = await Client.CreateCurrencyAsync("EUR");
        var eurWallet = await Client.CreateWalletAsync(1000m, currencyId: eur.Id);
        var fund = await CreateFundAsync(usd.Id);

        var response = await ReserveAsync(fund.Id, eurWallet.Id, "1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reserve_UnknownFund_ReturnsNotFound_AndMissingAmount_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        var fund = await CreateFundAsync(currency.Id);

        (await ReserveAsync(Guid.NewGuid(), wallet.Id, "1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.PostAsJsonAsync($"/api/v1/funds/{fund.Id}/reserve", new { WalletId = wallet.Id }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReserveAsync(fund.Id, Guid.NewGuid(), "1")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "unknown wallet");
    }

    [Fact]
    public async Task Reserve_ArchivedWallet_ReturnsConflict()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        await Client.CreateWalletAsync(0m, currencyId: currency.Id, name: "Основной");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        (await Client.PostAsync($"/api/v1/wallets/{wallet.Id}/archive", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var fund = await CreateFundAsync(currency.Id);

        (await ReserveAsync(fund.Id, wallet.Id, "1")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Release_DecreasesAllocation_AndMoreThanAllocated_ReturnsConflict()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        var fund = await CreateFundAsync(currency.Id);
        await ReserveOkAsync(fund.Id, wallet.Id, "100");

        var tooMuch = await ReleaseAsync(fund.Id, wallet.Id, "100.5");
        tooMuch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemOf(tooMuch)).Type.Should().Be("https://lupexwallet/errors/fund-conflict");

        var response = await ReleaseAsync(fund.Id, wallet.Id, "40");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Fund>())!.Reserved.Amount.Should().Be("60");
    }

    [Fact]
    public async Task Expense_IsNotBlocked_AndBalanceBelowReserveShowsDeficit()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        var fund = await CreateFundAsync(currency.Id);
        await ReserveOkAsync(fund.Id, wallet.Id, "800");

        await SpendAsync(wallet.Id, 500m);

        var summary = await GetWalletReservationAsync(wallet.Id);
        summary.Balance.Amount.Should().Be("500");
        summary.Free.Amount.Should().Be("-300");
        summary.Deficit.Amount.Should().Be("300");

        var view = await GetFundAsync(fund.Id);
        view.Deficit.Amount.Should().Be("300");
        view.Allocations.Single().Deficit.Amount.Should().Be("300");

        (await ReserveAsync(fund.Id, wallet.Id, "1")).StatusCode.Should().Be(HttpStatusCode.Conflict, "no free funds left");

        var all = await Client.GetFromJsonAsync<List<WalletReservation>>("/api/v1/funds/wallet-reservations");
        all!.Should().ContainSingle(w => w.WalletId == wallet.Id).Which.Deficit.Amount.Should().Be("300");
    }

    [Fact]
    public async Task WalletWithReserve_CannotBeArchivedDeletedOrChangeCurrency_UntilReleased()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var otherCurrency = await Client.CreateCurrencyAsync("EUR");
        await Client.CreateWalletAsync(0m, currencyId: currency.Id, name: "Основной");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        var fund = await CreateFundAsync(currency.Id);
        await ReserveOkAsync(fund.Id, wallet.Id, "100");

        var archive = await Client.PostAsync($"/api/v1/wallets/{wallet.Id}/archive", null);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemOf(archive)).Type.Should().Be("https://lupexwallet/errors/wallet-has-reservations");

        var delete = await Client.DeleteAsync($"/api/v1/wallets/{wallet.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemOf(delete)).Type.Should().Be("https://lupexwallet/errors/wallet-has-reservations");

        var changeCurrency = await Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = otherCurrency.Id });
        changeCurrency.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ReleaseAsync(fund.Id, wallet.Id, "100")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Client.PostAsync($"/api/v1/wallets/{wallet.Id}/archive", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Close_ReleasesAllReserves_AndClosedFundIsFinal()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        var fund = await CreateFundAsync(currency.Id);
        await ReserveOkAsync(fund.Id, wallet.Id, "600");

        var close = await Client.PostAsync($"/api/v1/funds/{fund.Id}/close", null);
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = (await close.Content.ReadFromJsonAsync<Fund>())!;
        closed.Status.Should().Be("Closed");
        closed.Reserved.Amount.Should().Be("0");
        closed.ClosedAt.Should().NotBeNull();

        (await GetWalletReservationAsync(wallet.Id)).Free.Amount.Should().Be("1000");
        (await ReserveAsync(fund.Id, wallet.Id, "1")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Client.DeleteAsync($"/api/v1/funds/{fund.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "a fund with reserve history is closed, not deleted");

        var active = await Client.GetFromJsonAsync<FundPage>("/api/v1/funds");
        active!.Data.Should().NotContain(f => f.Id == fund.Id);
        var all = await Client.GetFromJsonAsync<FundPage>("/api/v1/funds?includeClosed=true");
        all!.Data.Should().Contain(f => f.Id == fund.Id);
    }

    [Fact]
    public async Task Delete_FundWithoutReserves_ReturnsNoContent()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var fund = await CreateFundAsync(currency.Id);

        (await Client.DeleteAsync($"/api/v1/funds/{fund.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetAsync($"/api/v1/funds/{fund.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_CurrencyAfterReserve_ReturnsConflict_OtherFieldsChange()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var other = await Client.CreateCurrencyAsync("EUR");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        var fund = await CreateFundAsync(currency.Id);
        await ReserveOkAsync(fund.Id, wallet.Id, "10");

        (await Client.PatchAsJsonAsync($"/api/v1/funds/{fund.Id}", new { Name = "Отпуск", TargetAmount = "1000", CurrencyId = other.Id }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var response = await Client.PatchAsJsonAsync($"/api/v1/funds/{fund.Id}", new { Name = "Море", TargetAmount = "20", CurrencyId = currency.Id });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<Fund>())!;
        updated.Name.Should().Be("Море");
        decimal.Parse(updated.Progress, System.Globalization.CultureInfo.InvariantCulture).Should().Be(0.5m);
    }

    [Fact]
    public async Task List_PaginatesByCreationOrder()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await CreateFundAsync(currency.Id, name: $"Фонд {i}")).Id);
        }

        var first = (await Client.GetFromJsonAsync<FundPage>("/api/v1/funds?limit=2"))!;
        first.Pagination.HasMore.Should().BeTrue();
        var second = (await Client.GetFromJsonAsync<FundPage>($"/api/v1/funds?limit=2&cursor={Uri.EscapeDataString(first.Pagination.NextCursor!)}"))!;

        first.Data.Concat(second.Data).Select(f => f.Id).Should().Equal(ids);
        second.Pagination.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task CurrencyUsedByFund_CannotBeRemovedFromActive()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        await CreateFundAsync(currency.Id);

        (await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Changes_AreAudited_OnTheFund()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        var fund = await CreateFundAsync(currency.Id);
        await ReserveOkAsync(fund.Id, wallet.Id, "100");
        (await ReleaseAsync(fund.Id, wallet.Id, "30")).EnsureSuccessStatusCode();
        (await Client.PostAsync($"/api/v1/funds/{fund.Id}/close", null)).EnsureSuccessStatusCode();

        var page = (await Client.GetFromJsonAsync<AuditPage>($"/api/v1/audit-entries?entityType=Fund&entityId={fund.Id}&limit=100"))!;

        page.Data.Select(e => e.Action).Should().BeEquivalentTo(["Created", "Reserved", "Released", "Released", "Closed"]);
        page.Data.Single(e => e.Action == "Reserved").Changes.Should()
            .ContainSingle(c => c.Field == $"Allocation[{wallet.Id}]" && c.OldValue == "0" && c.NewValue == "100");
        page.Data.Where(e => e.Action == "Released").SelectMany(e => e.Changes)
            .Should().Contain(c => c.Field == "ReleaseReason" && c.NewValue == "FundClosed");
        page.Data.Single(e => e.Action == "Closed").Changes.Should().Contain(c => c.Field == "Status" && c.NewValue == "Closed");
    }

    [Fact]
    public async Task ReleaseLinkedReserve_ReleasesTheLinkedFundOnThePayingWallet()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("USD");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: currency.Id);
        var debtId = Guid.NewGuid();
        var linked = await CreateFundAsync(currency.Id, name: "На долг", linkedDebtId: debtId);
        var unrelated = await CreateFundAsync(currency.Id, name: "Другое");
        await ReserveOkAsync(linked.Id, wallet.Id, "200");
        await ReserveOkAsync(unrelated.Id, wallet.Id, "100");

        decimal released;
        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            released = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new ReleaseLinkedReserveCommand(debtId, null, wallet.Id, 250m));
        }

        released.Should().Be(200m);
        (await GetFundAsync(linked.Id)).Reserved.Amount.Should().Be("0");
        (await GetFundAsync(unrelated.Id)).Reserved.Amount.Should().Be("100");
    }
}
