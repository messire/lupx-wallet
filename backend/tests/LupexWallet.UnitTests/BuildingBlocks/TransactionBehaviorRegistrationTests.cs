using FluentAssertions;
using LupexWallet.Api;
using LupexWallet.BalanceHistory.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.UnitTests.BuildingBlocks;

/// <summary>
/// Open-generic registration of TransactionBehavior (Program.cs: AddOpenBehavior) is silently
/// skipped by the DI container for any closed type that violates the generic constraints —
/// no error, the command then simply runs without a transaction (ADR-0001, §4).
/// In MediatR 12 the void IRequest does not derive from IRequest&lt;Unit&gt; (the pipeline
/// substitutes TResponse = Unit), so the behavior must be constrained by the ICommand&lt;TResponse&gt;
/// marker only; otherwise every `IRequest, ICommand&lt;Unit&gt;` command loses its transaction.
/// MediatR requests IPipelineBehavior&lt;TRequest, TResponse&gt; by the command's IRequest response,
/// so the sweep derives TResponse from IRequest and separately checks it equals ICommand's.
/// These tests mirror the real MediatR registration and walk every command of the Host's
/// ModuleAssemblies.Application.
/// </summary>
public sealed class TransactionBehaviorRegistrationTests
{
    private sealed record ResultCommand : IRequest<int>, ICommand<int>;

    private sealed record VoidCommand : IRequest, ICommand<Unit>;

    private sealed record ResultQuery : IRequest<int>, IQuery<int>;

    private sealed record MismatchedCommand : IRequest<int>, ICommand<string>;

    private sealed record NotARequestCommand : ICommand<int>;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(ModuleAssemblies.Application.ToArray());
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });
        return services.BuildServiceProvider();
    }

    private static bool IsTransactionBehavior(object behavior) =>
        behavior.GetType() is { IsGenericType: true } type
        && type.GetGenericTypeDefinition() == typeof(TransactionBehavior<,>);

    private static IEnumerable<Type> ApplicationTypes() =>
        ModuleAssemblies.Application.SelectMany(a => a.GetTypes());

    /// <summary>
    /// Every concrete type implementing ICommand&lt;T&gt; among <paramref name="types"/>: the
    /// response MediatR requests the pipeline with (IRequest&lt;X&gt; -> X, void IRequest -> Unit;
    /// null if the type is not a MediatR request) and the TResponse declared by ICommand.
    /// </summary>
    private static IEnumerable<(Type Command, Type? RequestResponse, Type CommandResponse)> DescribeCommands(
        IEnumerable<Type> types) =>
        types
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>))
                .Select(i => (Command: t, CommandResponse: i.GetGenericArguments()[0])))
            .Select(c => (c.Command, RequestResponse: RequestResponseOf(c.Command), c.CommandResponse));

    private static Type? RequestResponseOf(Type command)
    {
        var generic = command.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));
        if (generic is not null)
        {
            return generic.GetGenericArguments()[0];
        }

        return typeof(IRequest).IsAssignableFrom(command) ? typeof(Unit) : null;
    }

    [Fact]
    public void CommandWithResult_GetsTransactionBehavior()
    {
        using var provider = BuildProvider();

        provider.GetServices<IPipelineBehavior<ResultCommand, int>>()
            .Should().ContainSingle(b => b is TransactionBehavior<ResultCommand, int>);
    }

    [Fact]
    public void VoidCommand_GetsTransactionBehavior()
    {
        using var provider = BuildProvider();

        // Checked via the generic type definition, not `is TransactionBehavior<VoidCommand, Unit>`:
        // the latter would not even compile under an IRequest<TResponse> constraint, which would
        // hide the runtime symptom (the behavior silently absent from DI).
        provider.GetServices<IPipelineBehavior<VoidCommand, Unit>>()
            .Should().ContainSingle(b => IsTransactionBehavior(b));
    }

    [Fact]
    public void Query_DoesNotGetTransactionBehavior()
    {
        using var provider = BuildProvider();

        provider.GetServices<IPipelineBehavior<ResultQuery, int>>().Should().BeEmpty();
    }

    [Fact]
    public void Discovery_FindsKnownCommands_SoTheSweepIsNotVacuous()
    {
        var commands = DescribeCommands(ApplicationTypes()).Select(c => c.Command).ToList();

        // Void IRequest + ICommand<Unit> and IRequest<Unit> + ICommand<Unit> and a result command.
        commands.Should().Contain(typeof(LupexWallet.Wallets.Application.DeleteWalletCommand));
        commands.Should().Contain(typeof(BackfillWalletBalanceSnapshotsCommand));
        commands.Should().Contain(typeof(LupexWallet.Operations.Application.DeleteOperationCommand));
        commands.Count.Should().BeGreaterThan(15);
    }

    [Fact]
    public void Description_ExposesResponseMismatchAndMissingRequest()
    {
        var described = DescribeCommands([typeof(MismatchedCommand), typeof(NotARequestCommand), typeof(VoidCommand)])
            .ToDictionary(c => c.Command);

        described[typeof(MismatchedCommand)].RequestResponse.Should().Be(typeof(int));
        described[typeof(MismatchedCommand)].CommandResponse.Should().Be(typeof(string));
        described[typeof(NotARequestCommand)].RequestResponse.Should().BeNull();
        described[typeof(VoidCommand)].RequestResponse.Should().Be(typeof(Unit));
    }

    [Fact]
    public void EveryCommandInApplicationAssemblies_DeclaresTheSameResponseInIRequestAndICommand()
    {
        var inconsistent = DescribeCommands(ApplicationTypes())
            .Where(c => c.RequestResponse != c.CommandResponse)
            .Select(c => $"{c.Command.FullName} (IRequest: {c.RequestResponse?.Name ?? "none"}, ICommand: {c.CommandResponse.Name})");

        string.Join(", ", inconsistent).Should().BeEmpty(
            "MediatR requests IPipelineBehavior by the IRequest response, so a mismatch with ICommand<T> "
            + "would silently drop TransactionBehavior");
    }

    [Fact]
    public void EveryCommandInApplicationAssemblies_GetsTransactionBehavior()
    {
        using var provider = BuildProvider();

        var withoutTransaction = new List<string>();
        foreach (var (command, requestResponse, _) in DescribeCommands(ApplicationTypes()))
        {
            // The type MediatR asks the container for: the IRequest response, not ICommand's.
            var behaviorServiceType = typeof(IPipelineBehavior<,>).MakeGenericType(command, requestResponse ?? typeof(Unit));

            var behaviors = provider.GetServices(behaviorServiceType);
            if (!behaviors.Any(b => b is not null && IsTransactionBehavior(b)))
            {
                withoutTransaction.Add($"{command.FullName} ({requestResponse?.Name ?? "none"})");
            }
        }

        string.Join(", ", withoutTransaction).Should().BeEmpty(
            "every ICommand must run inside TransactionBehavior (ADR-0001, §4); "
            + "a command missing here would silently execute without a TransactionScope");
    }

    [Fact]
    public void BackfillWalletBalanceSnapshotsCommand_GetsTransactionBehavior()
    {
        using var provider = BuildProvider();

        provider.GetServices<IPipelineBehavior<BackfillWalletBalanceSnapshotsCommand, Unit>>()
            .Should().ContainSingle(b => b is TransactionBehavior<BackfillWalletBalanceSnapshotsCommand, Unit>);
    }
}
