import {
  mockPartners,
  mockProducts,
  mockWorkCenters,
  type MockOperationalRecord,
} from './erpWmsMockData';

export type MockSearchKind = 'Product' | 'Barcode' | 'Partner' | 'Lot/Serial' | 'Document/Task';

export interface MockSearchResult {
  id: string;
  kind: MockSearchKind;
  reference: string;
  title: string;
  detail: string;
  warehouse?: string;
  status?: string;
  exact: boolean;
}

const normalize = (value: string) => value.trim().toLocaleLowerCase('vi');

const recordKind = (record: MockOperationalRecord): MockSearchKind =>
  record.type === 'Lot' || record.type === 'Serial' ? 'Lot/Serial' : 'Document/Task';

export const searchMockSystem = (query: string, allowedWarehouses?: string[]): MockSearchResult[] => {
  const q = normalize(query);
  if (!q) return [];

  const results: MockSearchResult[] = [];
  const warehouseAllowed = (warehouse: string) => !allowedWarehouses || allowedWarehouses.includes(warehouse);

  for (const product of mockProducts) {
    const values = [product.code, product.name, product.category].map(normalize);
    if (values.some((value) => value.includes(q))) {
      results.push({
        id: `product:${product.code}`,
        kind: 'Product',
        reference: product.code,
        title: product.name,
        detail: `${product.category} • ${product.baseUom} • Tracking ${product.tracking}`,
        exact: normalize(product.code) === q || normalize(product.name) === q,
      });
    }

    for (const barcode of product.barcodes) {
      if (normalize(barcode).includes(q)) {
        results.push({
          id: `barcode:${barcode}`,
          kind: 'Barcode',
          reference: barcode,
          title: `${product.code} • ${product.name}`,
          detail: `Barcode → ${product.baseUom}`,
          exact: normalize(barcode) === q,
        });
      }
    }
  }

  for (const partner of mockPartners) {
    if ([partner.code, partner.name].map(normalize).some((value) => value.includes(q))) {
      results.push({
        id: `partner:${partner.code}`,
        kind: 'Partner',
        reference: partner.code,
        title: partner.name,
        detail: partner.roles.join(' / '),
        exact: normalize(partner.code) === q || normalize(partner.name) === q,
      });
    }
  }

  for (const workCenter of Object.values(mockWorkCenters)) {
    for (const record of workCenter.records) {
      if (!warehouseAllowed(record.warehouse)) continue;
      const values = [
        record.reference,
        record.id,
        record.subject,
        record.type,
        record.productCode ?? '',
        record.partnerCode ?? '',
        record.location ?? '',
      ].map(normalize);
      if (!values.some((value) => value.includes(q))) continue;
      results.push({
        id: `record:${record.id}`,
        kind: recordKind(record),
        reference: record.reference,
        title: record.subject,
        detail: [record.type, record.productCode, record.partnerCode, record.location].filter(Boolean).join(' • '),
        warehouse: record.warehouse,
        status: record.status,
        exact: normalize(record.reference) === q || normalize(record.id) === q,
      });
    }
  }

  const kindOrder: Record<MockSearchKind, number> = {
    Barcode: 0,
    Product: 1,
    'Lot/Serial': 2,
    'Document/Task': 3,
    Partner: 4,
  };

  return results
    .filter((result, index, all) => all.findIndex((item) => item.id === result.id) === index)
    .sort((a, b) => Number(b.exact) - Number(a.exact) || kindOrder[a.kind] - kindOrder[b.kind] || a.reference.localeCompare(b.reference));
};
