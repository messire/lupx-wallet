namespace LupexWallet.Audit.Application;

/// <summary>
/// Anchor type for MediatR.RegisterServicesFromAssembly in Host/Program.cs — lets the Audit
/// module assembly be located without referencing a specific handler.
/// </summary>
public static class AssemblyMarker
{
}
