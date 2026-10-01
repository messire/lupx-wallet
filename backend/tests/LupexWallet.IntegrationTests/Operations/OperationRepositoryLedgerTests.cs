using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.Operations.Domain;
using LupexWallet.Operations.Infrastructure;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// IOperationRepository.GetRegularLedgerAsync against the real database: the date bound, soft-deleted rows,
/// corrections and the excluded ids are all applied in SQL.
/// </summary>
public sealed class OperationRepositoryLedgerTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep10 = new(2026, 9, 10);
    private static readonly DateOnly Sep20 = new(2026, 9, 20);

    private static readonly CurrencyId Currency = new(Guid.NewGuid());

    private static Operation Income(WalletId walletId, decimal amount, DateOnly date) => Operation.Create(
        OperationId.New(), walletId, OperationTypeId.New(), OperationEffectKind.Income,
        new Money(amount, Currency), date, adjustmentMode: null, appliedDelta: new Money(amount, Currency));

    private OperationsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(Fixture.ConnectionString).Options);

    private async Task<(WalletId WalletId, Operation First, Operation Second, Operation Third)> ArrangeAsync()
    {
        var walletId = WalletId.New();
        var first = Income(walletId, 500m, Sep1);
        var second = Income(walletId, 200m, Sep10);
        var third = Income(walletId, 400m, Sep20);
        var deleted = Income(walletId, 1_000m, Sep1);
        deleted.SoftDelete();
        var correction = Operation.CreateCorrection(OperationId.New(), walletId, Currency, Sep10, 800m);
        var foreign = Income(WalletId.New(), 7_000m, Sep1);

        await using var context = CreateContext();
        context.Operations.AddRange(first, second, third, deleted, correction, foreign);
        await context.SaveChangesAsync();
        return (walletId, first, second, third);
    }

    [Fact]
    public async Task WithoutBound_ReturnsEveryActiveRegularOperationOfTheWallet()
    {
        var (walletId, _, _, _) = await ArrangeAsync();
        await using var context = CreateContext();

        var ledger = await new OperationRepository(context).GetRegularLedgerAsync(walletId, [], null, CancellationToken.None);

        ledger.Select(x => x.AppliedDelta).Should().BeEquivalentTo(new[] { 500m, 200m, 400m }, "no deleted, correction or foreign rows");
    }

    [Fact]
    public async Task WithBound_SkipsOperationsDatedAfterIt_AndIncludesTheBoundDay()
    {
        var (walletId, _, _, _) = await ArrangeAsync();
        await using var context = CreateContext();

        var ledger = await new OperationRepository(context).GetRegularLedgerAsync(walletId, [], Sep10, CancellationToken.None);

        ledger.Select(x => x.OperationDate).Should().BeEquivalentTo(new[] { Sep1, Sep10 });
        ledger.Sum(x => x.AppliedDelta).Should().Be(700m);
    }

    [Fact]
    public async Task WithBoundAndExcludedIds_AppliesBothFilters()
    {
        var (walletId, first, _, third) = await ArrangeAsync();
        await using var context = CreateContext();

        var ledger = await new OperationRepository(context).GetRegularLedgerAsync(
            walletId, [first.Id, third.Id], Sep10, CancellationToken.None);

        ledger.Should().ContainSingle().Which.AppliedDelta.Should().Be(200m);
    }
}
