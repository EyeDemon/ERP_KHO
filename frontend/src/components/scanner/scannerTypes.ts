export interface ResolvedBarcodeResult {
  barcode: string;
  entityType: 'Location' | 'HandlingUnit' | 'Serial' | 'Lot' | 'Product' | 'Unknown';
  entityId?: number | null;
  code?: string | null;
  name?: string | null;
  warehouseId?: number | null;
  productId?: number | null;
  productCode?: string | null;
  productName?: string | null;
  unitId?: number | null;
  unitCode?: string | null;
  conversionFactor?: number | null;
  status?: string | null;
}

export interface ParsedGs1Data {
  raw: string;
  gtin?: string;
  lotNumber?: string;
  expiryDate?: string;
  expiryDisplay?: string;
  serialNumber?: string;
  sscc?: string;
}

export interface MobileBarcodeScannerBarProps {
  label?: string;
  placeholder?: string;
  sampleCode?: string;
  warehouseId?: number;
  disabled?: boolean;
  resolveViaApi?: boolean;
  stepLabels?: [string, string, string];
  onScan: (code: string, resolved?: ResolvedBarcodeResult | null) => void | Promise<void>;
  onUndoLastScan?: (undoneCode: string) => void;
}

export interface LastScanFeedback {
  step: 1 | 2 | 3;
  code: string;
  transitionText: string;
}

export const ENTITY_LABELS: Record<string, string> = {
  Location: 'Vị trí kệ',
  HandlingUnit: 'Đơn vị đóng gói (Kiện/Pallet)',
  Serial: 'Số sê-ri',
  Lot: 'Số lô',
  Product: 'Sản phẩm (SKU)',
  Unknown: 'Chưa nhận diện',
};

export const SAMPLE_GS1_CODE = '(01)08938505000517(10)LOT-QA-002(17)271231(21)SER-QA-01';
