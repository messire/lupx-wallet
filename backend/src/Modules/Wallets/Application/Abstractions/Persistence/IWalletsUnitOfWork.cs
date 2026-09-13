namespace LupexWallet.Wallets.Application;

/// <summary>Thin abstraction over the module DbContext's SaveChangesAsync; command handlers call it explicitly at the end of processing.</summary>
public interface IWalletsUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
