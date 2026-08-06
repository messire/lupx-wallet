namespace LupexWallet.Reporting.Api;

// DTO по контракту docs/api/openapi.yaml (схема TotalAmount, дополнена полем excludedWallets —
// риск №6, docs/PROGRESS.md П.5.1: решение пользователя, основной путь).

public sealed record MoneyResponse(string Amount, Guid CurrencyId);

public sealed record ExcludedWalletResponse(Guid WalletId, string Reason);

public sealed record TotalAmountResponse(
    DateOnly? Date,
    MoneyResponse Amount,
    DateOnly RatesAsOfDate,
    IReadOnlyList<ExcludedWalletResponse> ExcludedWallets);
