export const BARCODE_PATTERN = /^[A-Za-z0-9._-]{1,64}$/;

export const normalizeBarcodeInput = (value: string): string => {
  const normalized = value.trim();
  if (!BARCODE_PATTERN.test(normalized)) throw new Error('Barcode phải có từ 1 đến 64 ký tự hợp lệ.');
  return normalized;
};

export const categoryRequest = (value: number | ''): { categoryId: number | null } => ({ categoryId: value === '' ? null : value });
export const categoryChanged = (current: number | null | undefined, next: number | ''): boolean => (current ?? '') !== next;
