using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Единая точка обновления курсов (docs/architecture/high-level-architecture.md, §5) — и
/// плановый прогон (ExchangeRateRefreshBackgroundService), и ручная кнопка
/// (POST /exchange-rates/refresh) отправляют один и тот же MediatR-запрос, отличаясь только
/// <see cref="IsManualTrigger"/> (нужен, чтобы решить, сигналить ли фоновому сервису о
/// завершении: сам ручной вызов уже произошёл синхронно, фоновому сервису остаётся только
/// перезапустить суточный отсчёт — см. IExchangeRateRefreshSignal).
///
/// Обработчик (RefreshExchangeRatesCommandHandler) находится в ExchangeRates.Infrastructure,
/// а не здесь, как остальные command-хендлеры проекта — ему нужен IDomainEventDispatcher
/// (BuildingBlocks.Infrastructure) для публикации ExchangeRatesUpdated/ExchangeRateUpdateFailed
/// напрямую (события описывают результат прогона в целом, а не изменение состояния одного
/// отслеживаемого EF-агрегата — см. комментарий в ExchangeRates.Domain.ExchangeRateQuote), а
/// Application-слой в этом решении не ссылается на BuildingBlocks.Infrastructure (по аналогии
/// с BalanceHistory.Infrastructure.OperationEventHandlers — обработчики MediatR тоже могут жить
/// в Infrastructure, регистрируются вручную в Add&lt;Module&gt;Module, а не через сканирование сборки).
///
/// Намеренно НЕ реализует <see cref="ICommand{TResponse}"/>, поэтому TransactionBehavior
/// (BuildingBlocks.Infrastructure) не оборачивает обработку в ambient TransactionScope
/// (дефолтный таймаут TransactionManager.DefaultTimeout — 1 минута): обработчик последовательно
/// делает исходящие HTTP-вызовы к Frankfurter по каждой валютной паре (до 3 попыток x 10с на
/// пару с ретраями), что при нескольких недоступных парах суммарно легко превышает минуту —
/// вместо предписанного openapi.yaml 502 команда падала бы по таймауту транзакции с откатом уже
/// успешно полученных курсов. Обработчик сам управляет атомарностью через
/// IExchangeRatesUnitOfWork.SaveChangesAsync (один вызов на весь прогон, не per-pair) — этого
/// достаточно, т.к. побочный эффект самой команды ограничен её собственным DbContext, ambient-
/// транзакция других модулей ей не нужна (см. ADR-0011 — команда уже выполняется вне транзакции
/// SetPrimaryWallet, здесь тот же принцип распространён и на её собственный вызов).
/// </summary>
public sealed record RefreshExchangeRatesCommand(bool IsManualTrigger)
    : IRequest<RefreshExchangeRatesResult>;

public sealed record RefreshExchangeRatesResult(
    DateTimeOffset? LastSuccessfulUpdate,
    IReadOnlyList<ExchangeRateQuoteDto> Rates,
    bool HadFailures);
