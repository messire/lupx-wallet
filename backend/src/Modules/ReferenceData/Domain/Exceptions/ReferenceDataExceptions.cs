namespace LupexWallet.ReferenceData.Domain;

public abstract class ReferenceDataDomainException(string message) : Exception(message);

public sealed class ReferenceItemNameRequiredException() : ReferenceDataDomainException("Название элемента справочника обязательно.");

public sealed class CurrencyCodeRequiredException() : ReferenceDataDomainException("Код валюты обязателен.");

public sealed class CurrencyCodeAlreadyExistsException(string code)
    : ReferenceDataDomainException($"Валюта с кодом '{code}' уже существует (код уникален глобально, schema.md).");

public sealed class CurrencyNotAvailableException(string code)
    : ReferenceDataDomainException($"Валюта с кодом '{code}' недоступна: её нет в списке, полученном от сервиса курсов.");

public class ReferenceItemInUseException : ReferenceDataDomainException
{
    public ReferenceItemInUseException(string entityName, Guid id)
        : base($"{entityName} ({id}) уже используется — удаление невозможно, используйте деактивацию.")
    {
    }

    protected ReferenceItemInUseException(string message)
        : base(message)
    {
    }
}

/// <summary>Currency has no deactivated state (ADR-0004 §7), so the message does not suggest it.</summary>
public sealed class CurrencyInUseException(Guid id)
    : ReferenceItemInUseException($"Валюта ({id}) используется кошельком или операцией (включая удалённые) — убрать её из активных нельзя.");
