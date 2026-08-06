namespace LupexWallet.Wallets.Application;

/// <summary>
/// Инвариант «ровно один основной кошелек» на уровне всей коллекции агрегатов
/// (ddd-model.md, §2.1) — не может быть обеспечен логикой одного Wallet, поэтому
/// вынесен в доменный сервис уровня Application, работающий через IWalletRepository.
/// </summary>
public sealed class PrimaryWalletPolicy(IWalletRepository repository)
{
    /// <summary>Есть ли уже хотя бы один кошелек — определяет, станет ли новый основным.</summary>
    public async Task<bool> IsFirstWalletAsync(CancellationToken cancellationToken) =>
        !await repository.AnyExistsAsync(cancellationToken);
}
