namespace LupexWallet.SharedKernel;

/// <summary>
/// Command/query markers with no dependency on MediatR (the Domain layer must not see
/// MediatR transitively through SharedKernel). Concrete commands/queries in modules'
/// Application layer implement both MediatR.IRequest&lt;TResponse&gt; (for dispatching)
/// and one of these markers, so TransactionBehavior in BuildingBlocks.Infrastructure
/// applies only to commands, not queries.
/// </summary>
public interface ICommand<out TResponse>
{
}

public interface IQuery<out TResponse>
{
}
