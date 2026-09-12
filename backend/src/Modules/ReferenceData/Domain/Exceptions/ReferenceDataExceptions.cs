namespace LupexWallet.ReferenceData.Domain;

public abstract class ReferenceDataDomainException(string message) : Exception(message);

public sealed class ReferenceItemNameRequiredException() : ReferenceDataDomainException("Название элемента справочника обязательно.");

public sealed class CurrencyCodeRequiredException() : ReferenceDataDomainException("Код валюты обязателен.");

public sealed class CurrencyCodeAlreadyExistsException(string code)
    : ReferenceDataDomainException($"Валюта с кодом '{code}' уже существует (код уникален глобально, включая неактивные — schema.md).");

public sealed class ReferenceItemInUseException(string entityName, Guid id)
    : ReferenceDataDomainException($"{entityName} ({id}) уже используется — удаление невозможно, используйте деактивацию.");
