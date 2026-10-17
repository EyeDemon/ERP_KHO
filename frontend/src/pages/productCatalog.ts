export const BARCODE_PATTERN = /^[A-Za-z0-9._-]{1,64}$/;

export const roundQty4 = (value: number): number =>
  Number.isFinite(value) ? Math.round((value + Number.EPSILON) * 10000) / 10000 : 0;

export const sanitizeScannerCode = (value: string): string =>
  value
    .replace(/[\u0000-\u001f\u007f\u200b\ufeff]+/g, '')
    .trim()
    .replace(/^(\][A-Za-z][0-9])+/, '')
    .trim();

export const normalizeBarcodeInput = (value: string): string => {
  const normalized = sanitizeScannerCode(value);
  if (!BARCODE_PATTERN.test(normalized)) throw new Error('Barcode phải có từ 1 đến 64 ký tự hợp lệ.');
  return normalized;
};

export const categoryRequest = (value: number | ''): { categoryId: number | null } => ({ categoryId: value === '' ? null : value });
export const categoryChanged = (current: number | null | undefined, next: number | ''): boolean => (current ?? '') !== next;
