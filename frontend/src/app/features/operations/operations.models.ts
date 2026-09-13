// Модель состояния экрана Operations (не часть контракта API — производный вид
// OperationType, дополненный кодом поведения behaviorKind, для формы/фильтра).

/** Тип операции, дополненный кодом поведения (behaviorKind) для формы/фильтра. */
export interface OperationTypeOption {
  id: string;
  name: string;
  behaviorKindCode: string;
}
