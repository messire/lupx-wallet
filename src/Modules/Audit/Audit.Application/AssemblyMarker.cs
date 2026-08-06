namespace LupexWallet.Audit.Application;

/// <summary>
/// Опорный тип для MediatR.RegisterServicesFromAssembly в Host/Program.cs —
/// позволяет найти сборку модуля Audit без обращения к конкретному хендлеру.
/// </summary>
public static class AssemblyMarker
{
}
