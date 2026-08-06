using LupexWallet.Wallets.Domain;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Инвариант «ровно один основной кошелек» на уровне всей коллекции агрегатов
/// (ddd-model.md, §2.1) — не может быть обеспечен логикой одного Wallet, поэтому
/// вынесен в доменный сервис уровня Application, работающий через IWalletRepository.
/// </summary>
public sealed class PrimaryWalletPolicy(IWalletRepository repository, IWalletsUnitOfWork unitOfWork)
{
    /// <summary>Есть ли уже хотя бы один кошелек — определяет, станет ли новый основным.</summary>
    public async Task<bool> IsFirstWalletAsync(CancellationToken cancellationToken) =>
        !await repository.AnyExistsAsync(cancellationToken);

    /// <summary>
    /// UC-05: переносит признак «основной» на <paramref name="wallet"/> — снимает его с
    /// прежнего основного кошелька (если это другой кошелек) через Wallet.UnmarkAsPrimary,
    /// затем помечает целевой кошелек через Wallet.MarkAsPrimary (которое само бросает
    /// CannotSetArchivedWalletAsPrimaryException для архивного кошелька — здесь не
    /// дублируется). Идемпотентно: если кошелек уже основной, ничего не делает и не
    /// поднимает лишнее событие PrimaryWalletChanged.
    ///
    /// Снятие флага с прежнего основного сохраняется отдельным SaveChangesAsync ДО
    /// назначения нового — из-за ux_wallets_single_primary (частичный уникальный индекс
    /// schema.md, не deferrable): если бы UPDATE, выставляющий is_primary=true у нового
    /// кошелька, ушел в БД раньше или в одном batch с UPDATE, снимающим флаг со старого,
    /// на мгновение существовали бы две строки с is_primary=true, что нарушило бы индекс.
    /// EF Core не гарантирует порядок UPDATE между несвязанными сущностями одного типа
    /// (наблюдалось на практике: без разделения на два SaveChangesAsync запрос падал с
    /// нарушением уникального индекса). Оба вызова остаются в одной транзакции команды
    /// (TransactionBehavior), поэтому откат по-прежнему атомарен.
    /// </summary>
    public async Task SetPrimaryAsync(Wallet wallet, CancellationToken cancellationToken)
    {
        if (wallet.IsPrimary)
        {
            return;
        }

        var currentPrimary = await repository.GetCurrentPrimaryAsync(cancellationToken);
        if (currentPrimary is not null && currentPrimary.Id != wallet.Id)
        {
            currentPrimary.UnmarkAsPrimary();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        wallet.MarkAsPrimary(currentPrimary?.Id);
    }
}
