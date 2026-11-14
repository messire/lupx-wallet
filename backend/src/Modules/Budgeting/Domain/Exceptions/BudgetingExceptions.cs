namespace LupexWallet.Budgeting.Domain;

/// <summary>Validation failure of a Budgeting command (HTTP 400).</summary>
public class BudgetingValidationException(string message) : Exception(message);

/// <summary>Conflict with the current state (HTTP 409).</summary>
public class BudgetingConflictException(string message) : Exception(message);

public sealed class ExpenseCategoryNameRequiredException()
    : BudgetingValidationException("Название категории расходов обязательно.");

public sealed class ExpenseCategoryNameTooLongException(int maxLength)
    : BudgetingValidationException($"Название категории расходов длиннее {maxLength} символов.");

public sealed class ExpenseCategoryNameAlreadyExistsException(string name)
    : BudgetingConflictException($"Активная категория расходов «{name}» уже есть.");

public sealed class OperationTypeNotFoundForCategoryException(Guid operationTypeId)
    : BudgetingValidationException($"Тип операции {operationTypeId} не найден.");

public sealed class OperationTypeInactiveForCategoryException(string name)
    : BudgetingValidationException($"Тип операции «{name}» деактивирован — добавить его в категорию нельзя.");

public sealed class OperationTypeNotExpenseException(string name)
    : BudgetingValidationException($"Тип операции «{name}» не является расходом — в категорию расходов входят только типы с поведением «Расход».");

public sealed class OperationTypeAlreadyInCategoryException(string operationTypeName, string categoryName)
    : BudgetingConflictException($"Тип операции «{operationTypeName}» уже входит в категорию «{categoryName}» — тип может входить только в одну категорию.");

/// <summary>Thrown when the database unique index catches a concurrent claim of the same type.</summary>
public sealed class OperationTypeClaimedConcurrentlyException()
    : BudgetingConflictException("Тип операции уже входит в другую категорию (изменено параллельно). Обновите список категорий.");

public sealed class ExpenseCategoryInUseException(string name)
    : BudgetingConflictException($"Категория расходов «{name}» используется в бюджете — удаление невозможно, используйте деактивацию.");

public sealed class ExpenseCategoryInactiveException(string name)
    : BudgetingValidationException($"Категория расходов «{name}» деактивирована — лимит по ней задать нельзя.");

public sealed class ExpenseCategoryNotFoundException(Guid id)
    : BudgetingValidationException($"Категория расходов {id} не найдена.");

public sealed class BudgetMonthInvalidException(string? value)
    : BudgetingValidationException($"Месяц бюджета «{value}» должен быть в формате ГГГГ-ММ.");

public sealed class BudgetCurrencyNotActiveException(Guid currencyId)
    : BudgetingValidationException($"Валюта {currencyId} не найдена среди активных.");

public sealed class BudgetAlreadyExistsException(string month, string currencyCode)
    : BudgetingConflictException($"Бюджет на {month} в валюте {currencyCode} уже есть.");

public sealed class BudgetLimitInvalidException()
    : BudgetingValidationException("Лимит должен быть числом не меньше нуля.");
