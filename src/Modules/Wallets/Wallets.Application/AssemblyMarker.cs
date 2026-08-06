namespace LupexWallet.Wallets.Application;

/// <summary>
/// Опорный тип для MediatR.RegisterServicesFromAssembly в Host/Program.cs —
/// позволяет найти сборку модуля Wallets без обращения к конкретному хендлеру.
/// </summary>
public static class AssemblyMarker
{
}
