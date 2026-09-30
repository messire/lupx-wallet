using System.Reflection;

namespace LupexWallet.Api;

/// <summary>
/// Application assemblies scanned for MediatR handlers. Shared by Program.cs and the tests that
/// verify the pipeline registration, so both see the same list.
/// </summary>
public static class ModuleAssemblies
{
    public static readonly IReadOnlyList<Assembly> Application =
    [
        typeof(LupexWallet.Wallets.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.ReferenceData.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.Operations.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.BalanceHistory.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.ExchangeRates.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.Audit.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.Reporting.Application.AssemblyMarker).Assembly,
    ];
}
