namespace LupexWallet.ReferenceData.Domain;

public abstract class ReferenceDataDomainException(string message) : Exception(message);

public sealed class ReferenceItemNameRequiredException() : ReferenceDataDomainException("Название элемента справочника обязательно.");

public sealed class CurrencyCodeRequiredException() : ReferenceDataDomainException("Код валюты обязателен.");

public sealed class CurrencyCodeAlreadyExistsException(string code)
    : ReferenceDataDomainException($"Валюта {code} уже добавлена в активные.")
{
    public string Code { get; } = code;
}

public sealed class CurrencyNotAvailableException(string code)
    : ReferenceDataDomainException($"Валюта с кодом '{code}' недоступна: её нет в списке, полученном от сервиса курсов.")
{
    public string Code { get; } = code;
}

/// <summary>The id stays in <see cref="ItemId"/>; the message is shown to the user and carries no identifiers.</summary>
public class ReferenceItemInUseException : ReferenceDataDomainException
{
    public ReferenceItemInUseException(string itemKind, Guid id, string name)
        : this(id, $"{itemKind} «{name}» уже используется — удаление невозможно, используйте деактивацию.")
    {
    }

    protected ReferenceItemInUseException(Guid id, string message)
        : base(message)
    {
        ItemId = id;
    }

    public Guid ItemId { get; }
}

/// <summary>Currency has no deactivated state (ADR-0004 §7), so the message does not suggest it.</summary>
public sealed class CurrencyInUseException(Guid id, string code)
    : ReferenceItemInUseException(id, $"Валюта {code} используется кошельком или операцией (включая удалённые) — убрать её из активных нельзя.");
