using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Single entry point for refreshing rates (docs/architecture/high-level-architecture.md,
/// §5) — both the scheduled run (ExchangeRateRefreshBackgroundService) and the manual button
/// (POST /exchange-rates/refresh) send the same MediatR request, differing only in
/// <see cref="IsManualTrigger"/> (needed to decide whether to signal the background service
/// on completion: the manual call itself already happened synchronously, the background
/// service only needs to restart the daily countdown — see IExchangeRateRefreshSignal).
///
/// The handler (RefreshExchangeRatesCommandHandler) lives in ExchangeRates.Infrastructure,
/// not here like the project's other command handlers — it needs IDomainEventDispatcher
/// (BuildingBlocks.Infrastructure) to publish ExchangeRatesUpdated/ExchangeRateUpdateFailed
/// directly (the events describe the result of a whole run, not a state change of one
/// tracked EF aggregate — see the comment on ExchangeRates.Domain.ExchangeRateQuote), and by
/// design the Application layer does not reference BuildingBlocks.Infrastructure (like
/// BalanceHistory.Infrastructure.OperationEventHandlers — MediatR handlers can also live in
/// Infrastructure, registered manually in Add&lt;Module&gt;Module rather than via assembly
/// scanning).
///
/// Deliberately does NOT implement <see cref="ICommand{TResponse}"/>, so TransactionBehavior
/// (BuildingBlocks.Infrastructure) does not wrap handling in an ambient TransactionScope
/// (TransactionManager.DefaultTimeout defaults to 1 minute): the handler makes sequential
/// outgoing HTTP calls to Frankfurter per currency pair (up to 3 attempts x 10s per pair with
/// retries), which easily exceeds a minute in total when several pairs are unavailable —
/// instead of the openapi.yaml-mandated 502, the command would fail on a transaction timeout
/// and roll back rates already fetched successfully. The handler manages its own atomicity
/// via IExchangeRatesUnitOfWork.SaveChangesAsync (one call per whole run, not per pair) —
/// sufficient because the command's side effect is confined to its own DbContext; it does
/// not need another module's ambient transaction (see ADR-0011 — the command already runs
/// outside SetPrimaryWallet's transaction; the same principle extends here to its own call).
/// </summary>
public sealed record RefreshExchangeRatesCommand(bool IsManualTrigger)
    : IRequest<RefreshExchangeRatesResult>;

public sealed record RefreshExchangeRatesResult(
    DateTimeOffset? LastSuccessfulUpdate,
    IReadOnlyList<ExchangeRateQuoteDto> Rates,
    bool HadFailures);
