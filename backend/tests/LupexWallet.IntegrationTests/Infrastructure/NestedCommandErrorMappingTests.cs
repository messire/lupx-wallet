using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Debts.Domain;
using LupexWallet.Funds.Domain;
using LupexWallet.IntegrationTests.Debts;
using LupexWallet.Operations.Domain;
using MediatR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Review v0.4.0, R1 (docs/api/api-design.md, «Ошибки вложенных команд»): an error of another module raised inside a
/// command — here the Funds subscriber of a debt repayment or of an operation deletion losing a concurrent update of
/// the fund (FundModifiedConcurrentlyException) — is answered with that module's own ProblemDetails (409 fund-conflict),
/// not a 500, and the whole transaction is rolled back. The concurrent update is simulated by an extra subscriber that
/// throws the exception the Funds unit of work throws on DbUpdateConcurrencyException.
/// </summary>
public sealed class NestedCommandErrorMappingTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record Problem(string Type, string Title, int Status, string? Detail);

    private sealed class ConcurrentFundUpdateOnRepayment : INotificationHandler<DomainEventNotification<DebtRepaymentRecorded>>
    {
        public Task Handle(DomainEventNotification<DebtRepaymentRecorded> notification, CancellationToken cancellationToken) =>
            throw new FundModifiedConcurrentlyException(new DbUpdateConcurrencyException("simulated"));
    }

    private sealed class ConcurrentFundUpdateOnOperationDeleted : INotificationHandler<DomainEventNotification<OperationDeleted>>
    {
        public Task Handle(DomainEventNotification<OperationDeleted> notification, CancellationToken cancellationToken) =>
            throw new FundModifiedConcurrentlyException(new DbUpdateConcurrencyException("simulated"));
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> FactoryWith(Action<IServiceCollection> configure) =>
        Fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(configure));

    private HttpClient ClientOf(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Fixture.Token);
        return client;
    }

    [Fact]
    public async Task DebtRepayment_FundSubscriberConflict_Returns409FundConflict_AndRollsBack()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(1000m, name: "Основной");
        var creditor = await Client.CreateCreditorAsync();
        var debt = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 300m);
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense", "Погашение");
        await using var factory = FactoryWith(services =>
            services.AddScoped<INotificationHandler<DomainEventNotification<DebtRepaymentRecorded>>, ConcurrentFundUpdateOnRepayment>());
        using var faulty = ClientOf(factory);

        var response = await faulty.PayAsync($"/api/v1/debts/{debt.Id}/repayments", wallet.Id, expenseTypeId, 100m);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        var problem = (await response.Content.ReadFromJsonAsync<Problem>())!;
        problem.Type.Should().Be("https://lupexwallet/errors/fund-conflict");
        problem.Status.Should().Be(409);

        var after = await Client.GetFromJsonAsync<DebtResponse>($"/api/v1/debts/{debt.Id}");
        after!.Repayments.Should().BeEmpty("the repayment, its operation and the reserve release are one transaction");
        (await Client.ListOperationsAsync(wallet.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteOperation_FundSubscriberConflict_Returns409FundConflict_AndRollsBack()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(1000m, name: "Основной");
        var creditor = await Client.CreateCreditorAsync();
        var debt = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 300m);
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense", "Погашение");
        var repaid = await (await Client.PayAsync($"/api/v1/debts/{debt.Id}/repayments", wallet.Id, expenseTypeId, 100m)).ReadAsync<DebtResponse>();
        var operationId = repaid.Repayments.Single().OperationId;
        await using var factory = FactoryWith(services =>
            services.AddScoped<INotificationHandler<DomainEventNotification<OperationDeleted>>, ConcurrentFundUpdateOnOperationDeleted>());
        using var faulty = ClientOf(factory);

        var response = await faulty.DeleteAsync($"/api/v1/operations/{operationId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<Problem>())!.Type.Should().Be("https://lupexwallet/errors/fund-conflict");
        var operation = await Client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/operations/{operationId}");
        operation.GetProperty("isDeleted").GetBoolean().Should().BeFalse();
    }
}
