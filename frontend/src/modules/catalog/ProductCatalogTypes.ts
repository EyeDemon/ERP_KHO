import { permissionError } from '../../services/permissionPresentation';

export interface Barcode {
  id: number;
  productId: number;
  value: string;
}

export interface Category {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
}

export interface Unit {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
}

export interface Product {
  id: number;
  code: string;
  name: string;
  description?: string;
  storageClass?: string | null;
  unitWeightKg?: number | null;
  unitVolumeM3?: number | null;
  unitPalletEquivalent?: number | null;
  trackingType?: 'None' | 'Lot' | 'Serial';
  expiryControl?: boolean;
  shelfLifeDays?: number | null;
  qcRequired?: boolean;
  receivingTolerancePercent?: number | null;
  uoms?: Array<{
    id?: number;
    unitId: number;
    unitCode?: string;
    unitName?: string;
    conversionFactor?: number;
    conversionFactorToBase?: number;
    isBase?: boolean;
  }>;
  unitId: number;
  unitName?: string;
  categoryId?: number | null;
  categoryName?: string | null;
  barcodes: Barcode[];
  isActive: boolean;
}

export interface ChannelSkuMappingItem {
  id: number;
  channelCode: string;
  shopName: string;
  externalSkuCode: string;
  externalSkuName: string;
  internalProductId: number;
  internalProductCode: string;
  internalProductName: string;
  conversionFactor: number;
  syncStatus: string;
}

export interface ProductFormState {
  code: string;
  name: string;
  description: string;
  storageClass: string;
  unitWeightKg: string;
  unitVolumeM3: string;
  unitPalletEquivalent: string;
  trackingType: 'None' | 'Lot' | 'Serial';
  expiryControl: boolean;
  shelfLifeDays: string;
  unitId: number;
  categoryId: number | '';
  isActive: boolean;
}

export type ProductCatalogTab = 'products' | 'categories-uom' | 'channel-mapping' | 'bundle-gift';

export const STORAGE_CLASS_TO_VI: Record<string, string> = {
  AMBIENT: 'Nhiệt độ thường',
  CHILLED: 'Mát',
  COLD: 'Mát',
  FROZEN: 'Đông lạnh',
  HAZMAT: 'Hàng nguy hại',
};

export const STORAGE_CLASS_FROM_VI: Record<string, string> = {
  'NHIỆT ĐỘ THƯỜNG': 'AMBIENT',
  'MÁT': 'CHILLED',
  'ĐÔNG LẠNH': 'FROZEN',
  'HÀNG NGUY HẠI': 'HAZMAT',
};

export const formatStorageClass = (sc?: string | null): string => {
  if (!sc) return '';
  const key = sc.trim().toUpperCase();
  return STORAGE_CLASS_TO_VI[key] || sc;
};

export const normalizeStorageClassPayload = (sc: string): string | null => {
  const trimmed = sc.trim();
  if (!trimmed) return null;
  const upper = trimmed.toUpperCase();
  return STORAGE_CLASS_FROM_VI[upper] || upper;
};

export const sanitizeProductDescription = (desc?: string | null): string => {
  if (!desc) return '';
  return desc.replace(/Dữ liệu QA phục vụ authenticated E2E/gi, 'Thùng linh kiện điện tử tiêu chuẩn xuất nhập kho');
};

export const messageOf = permissionError;
export const PAGE_SIZE = 10;

export const mapRawChannelMappings = (rawList: unknown[]): ChannelSkuMappingItem[] => {
  const items = Array.isArray(rawList) ? rawList : [];
  return items.map((raw: any, idx: number) => {
    const item = (raw ?? {}) as Record<string, unknown>;
    return {
      id: Number(item.id ?? idx + 1),
      channelCode: String(item.channelCode ?? item.channel ?? 'SHOPEE'),
      shopName: String(item.shopName ?? item.storeName ?? item.shopCode ?? 'Gian hàng chính hãng'),
      externalSkuCode: String(item.externalSkuCode ?? item.channelSkuCode ?? item.platformSku ?? `EXT-SKU-${idx + 1}`),
      externalSkuName: String(item.externalSkuName ?? item.channelSkuName ?? 'Sản phẩm trên sàn TMĐT'),
      internalProductId: Number(item.internalProductId ?? item.productId ?? 1),
      internalProductCode: String(item.internalProductCode ?? item.productCode ?? 'SKU-001'),
      internalProductName: String(item.internalProductName ?? item.productName ?? 'Sản phẩm kho nội bộ'),
      conversionFactor: Number(item.conversionFactor ?? item.multiplier ?? 1),
      syncStatus: String(item.syncStatus ?? item.status ?? 'ACTIVE'),
    };
  });
};
