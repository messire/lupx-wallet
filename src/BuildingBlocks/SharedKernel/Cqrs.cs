namespace LupexWallet.SharedKernel;

/// <summary>
/// Маркеры «команда» / «запрос» без зависимости от MediatR (Domain-слой не должен видеть
/// MediatR транзитивно через SharedKernel). Конкретные команды/запросы в Application-слое
/// модулей реализуют оба интерфейса: и MediatR.IRequest&lt;TResponse&gt; (для диспетчеризации),
/// и один из этих маркеров (чтобы TransactionBehavior в BuildingBlocks.Infrastructure мог
/// применяться только к командам, не к запросам).
/// </summary>
public interface ICommand<out TResponse>
{
}

public interface IQuery<out TResponse>
{
}
