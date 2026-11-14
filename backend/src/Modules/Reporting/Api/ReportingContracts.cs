namespace LupexWallet.Reporting.Api;

// DTO по контракту docs/api/openapi.yaml (схема TotalAmount, дополнена полем excludedWallets —
// кошелёк без курса исключается из суммы с причиной, ADR-0004 §9).

public sealed record MoneyResponse(string Amount, Guid CurrencyId);

public sealed record ExcludedWalletResponse(Guid WalletId, string Reason);

public sealed record TotalAmountResponse(
    DateOnly? Date,
    MoneyResponse Amount,
    DateOnly RatesAsOfDate,
    IReadOnlyList<ExcludedWalletResponse> ExcludedWallets);
