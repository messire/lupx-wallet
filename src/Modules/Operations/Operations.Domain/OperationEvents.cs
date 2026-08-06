using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Domain;

/// <summary>
/// События агрегатов Operation/Transfer (ddd-model.md, §6). Эффект на баланс кошелька
/// обязателен для корректности команды и потому реализован синхронным контрактом, а не
/// подпиской на эти события (ADR-0007). BalanceHistory — первый реальный подписчик
/// (ADR-0008): реагирует на OperationCreated/Updated/Deleted, чтобы каскадно пересчитать
/// историю баланса с датой операции (OperationDate/PreviousOperationDate — минимальная
/// затронутая дата) до сегодняшнего дня (ADR-0003). TransferCreated/TransferDeleted не
/// нужны отдельному подписчику: обе операции перевода уже поднимают свои
/// OperationCreated/OperationDeleted (см. Operation.CreateForTransfer, DeleteTransferCommand).
/// </summary>
public sealed record OperationCreated(OperationId OperationId, WalletId WalletId, DateOnly OperationDate) : DomainEvent;

/// <summary>
/// PreviousWalletId — UC-13, решение пользователя от 2026-09-11 (перенос операции на другой
/// кошелек): равен WalletId, если кошелек не менялся (обычное редактирование). Если отличается,
/// подписчик BalanceHistory.Infrastructure.OperationUpdatedHandler обязан пересчитать оба
/// кошелька — старый (с PreviousOperationDate, откуда его баланс больше не включает эту
/// операцию) и новый (с OperationDate, откуда он начинает её включать), т.к. каждый кошелек
/// пересчитывается независимо (IWalletOperationsLookup фильтрует по WalletId).
/// </summary>
public sealed record OperationUpdated(
    OperationId OperationId, WalletId WalletId, DateOnly OperationDate, DateOnly PreviousOperationDate, WalletId PreviousWalletId) : DomainEvent;
public sealed record OperationDeleted(OperationId OperationId, WalletId WalletId, DateOnly OperationDate) : DomainEvent;

public sealed record TransferCreated(TransferId TransferId, WalletId SourceWalletId, WalletId TargetWalletId) : DomainEvent;
public sealed record TransferDeleted(TransferId TransferId) : DomainEvent;
