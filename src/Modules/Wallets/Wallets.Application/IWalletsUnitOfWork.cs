namespace LupexWallet.Wallets.Application;

/// <summary>
/// Тонкая абстракция над SaveChangesAsync модульного DbContext — command-хендлеры
/// вызывают её явно в конце обработки. Пока в решении нет сценария, где одна команда
/// синхронно затрагивает несколько модулей (кросс-модульные подписчики Audit/BalanceHistory
/// — предмет последующих срезов), поэтому единого межмодульного Unit of Work сейчас не
/// вводим — см. заметку в WalletsModuleExtensions.
/// </summary>
public interface IWalletsUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
