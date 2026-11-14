using FluentAssertions;
using LupexWallet.Audit.Application;
using LupexWallet.Audit.Domain;
using LupexWallet.Audit.Infrastructure;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Audit;

/// <summary>
/// Audit subscribers for balance-correction events (ADR-0003, §6; ADR-0006): the diff comes from the
/// ChangeTracker (covered by the integration tests), so here only the entity type/id/action routing
/// and the handler registration are checked.
/// </summary>
public sealed class CorrectionAuditHandlersTests
{
    private readonly IAuditEntryRepository _repository = Substitute.For<IAuditEntryRepository>();
    private readonly IAuditUnitOfWork _unitOfWork = Substitute.For<IAuditUnitOfWork>();
    private readonly List<AuditEntry> _recorded = [];

    private AuditRecorder CreateRecorder()
    {
        var actorAccessor = Substitute.For<IAuditActorAccessor>();
        actorAccessor.Current.Returns(AuditActorContext.User);
        _repository.When(r => r.Add(Arg.Any<AuditEntry>())).Do(call => _recorded.Add(call.Arg<AuditEntry>()));
        return new AuditRecorder(_repository, _unitOfWork, actorAccessor, new DispatchDomainEventsInterceptor(Substitute.For<IDomainEventDispatcher>()));
    }

    [Fact]
    public async Task CorrectionRecalculated_RecordsRecalculatedActionForOperationEntity()
    {
        var operationId = OperationId.New();
        var handler = new CorrectionRecalculatedAuditHandler(CreateRecorder());

        await handler.Handle(
            new DomainEventNotification<CorrectionRecalculated>(new CorrectionRecalculated(operationId, WalletId.New(), new DateOnly(2026, 1, 1))),
            CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("Operation");
        entry.EntityId.Should().Be(operationId.Value);
        entry.Action.Should().Be("Recalculated");
        entry.Actor.Kind.Should().Be(AuditActorKind.User);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorrectionRemoved_RecordsDeletedActionForOperationEntity()
    {
        var operationId = OperationId.New();
        var handler = new CorrectionRemovedAuditHandler(CreateRecorder());

        await handler.Handle(
            new DomainEventNotification<CorrectionRemoved>(new CorrectionRemoved(operationId, WalletId.New(), new DateOnly(2026, 1, 1))),
            CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("Operation");
        entry.EntityId.Should().Be(operationId.Value);
        entry.Action.Should().Be("Deleted");
    }

    [Fact]
    public async Task CorrectionCurrencyChanged_RecordsUpdatedActionForOperationEntity()
    {
        var operationId = OperationId.New();
        var handler = new CorrectionCurrencyChangedAuditHandler(CreateRecorder());

        await handler.Handle(
            new DomainEventNotification<CorrectionCurrencyChanged>(new CorrectionCurrencyChanged(operationId, WalletId.New())),
            CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("Operation");
        entry.EntityId.Should().Be(operationId.Value);
        entry.Action.Should().Be("Updated");
    }

    [Fact]
    public void AddAuditModule_RegistersHandlersForAllCorrectionEvents()
    {
        var services = new ServiceCollection();
        var connectionStrings = Substitute.For<IConfigurationSection>();
        connectionStrings["LupexWallet"].Returns("Host=localhost;Database=unused");
        var configuration = Substitute.For<IConfiguration>();
        configuration.GetSection("ConnectionStrings").Returns(connectionStrings);

        services.AddAuditModule(configuration);

        services.Should().Contain(d => d.ServiceType == typeof(INotificationHandler<DomainEventNotification<CorrectionRecalculated>>)
            && d.ImplementationType == typeof(CorrectionRecalculatedAuditHandler));
        services.Should().Contain(d => d.ServiceType == typeof(INotificationHandler<DomainEventNotification<CorrectionRemoved>>)
            && d.ImplementationType == typeof(CorrectionRemovedAuditHandler));
        services.Should().Contain(d => d.ServiceType == typeof(INotificationHandler<DomainEventNotification<CorrectionCurrencyChanged>>)
            && d.ImplementationType == typeof(CorrectionCurrencyChangedAuditHandler));
    }
}
