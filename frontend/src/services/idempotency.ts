const pendingKeys = new Map<string, string>();

const newKey = (): string => globalThis.crypto.randomUUID();

export const idempotencyKeyFor = (logicalAction: string): string => {
  const existing = pendingKeys.get(logicalAction);
  if (existing) return existing;
  const key = newKey();
  pendingKeys.set(logicalAction, key);
  return key;
};

export const completeIdempotentAction = (logicalAction: string): void => {
  pendingKeys.delete(logicalAction);
};

export const idempotencyHeaders = (logicalAction: string) => ({
  'Idempotency-Key': idempotencyKeyFor(logicalAction),
});
