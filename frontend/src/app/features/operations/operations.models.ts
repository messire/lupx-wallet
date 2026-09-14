// Operations screen state model (not part of the API contract — a derived view
// of OperationType, enriched with the behaviorKind code, for the form/filter).

/** Operation type enriched with its behaviorKind code, for the form/filter. */
export interface OperationTypeOption {
  id: string;
  name: string;
  behaviorKindCode: string;
}
