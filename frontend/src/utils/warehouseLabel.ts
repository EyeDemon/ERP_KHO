export const CANONICAL_WH01_LABEL = 'Kho Trung tâm (WH01)';

/**
 * Chuẩn hóa tên hiển thị kho trên toàn bộ giao diện quản lý kho.
 * Mọi biến thể của kho trung tâm WH01 ("Kho Chính TP.HCM", "Kho Chính", "Kho trung tâm",
 * "Kho Trung Tâm", "WH01 — Kho Chính TP.HCM", "Kho Trung tâm WH01", "WH01")
 * đều quy về duy nhất một nhãn chuẩn: "Kho Trung tâm (WH01)" (không lặp mã kho).
 */
export function formatWarehouseLabel(
  rawName?: string | null,
  rawCode?: string | null,
  warehouseId?: number | null,
): string {
  const name = (rawName ?? '').replace(/\bERP_KHO\b/gi, 'Đại Phát').trim();
  const code = (rawCode ?? '').trim().toUpperCase();

  if (!name && !code) {
    return warehouseId === 1 ? CANONICAL_WH01_LABEL : '';
  }

  const normalizedLower = name.toLowerCase();
  const hasOtherWhCode = /\bwh0?[2-9]\b/i.test(name) || /^WH0?[2-9]$/i.test(code);

  if (!hasOtherWhCode) {
    if (
      code === 'WH01' ||
      code === 'WH-MAIN' ||
      normalizedLower === 'wh01' ||
      normalizedLower === 'wh-main' ||
      /\bwh0?1\b/i.test(name) ||
      /kho\s*chính/i.test(name) ||
      /kho\s*trung\s*tâm/i.test(name)
    ) {
      return CANONICAL_WH01_LABEL;
    }
  }

  if (code && name && !name.toUpperCase().includes(code)) {
    return `${name} (${code})`;
  }

  return name || code;
}
