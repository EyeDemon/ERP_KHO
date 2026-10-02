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

const includesQuery = (values: string[], query: string) =>
  values.some((value) => normalize(value).includes(query));

const searchProducts = (query: string): MockSearchResult[] => {
  const results: MockSearchResult[] = [];

  for (const product of mockProducts) {
    if (includesQuery([product.code, product.name, product.category], query)) {
      results.push({
        id: `product:${product.code}`,
        kind: 'Product',
        reference: product.code,
        title: product.name,
        detail: `${product.category} • ${product.baseUom} • Tracking ${product.tracking}`,
        exact: normalize(product.code) === query || normalize(product.name) === query,
      });
    }

    for (const barcode of product.barcodes) {
      if (!normalize(barcode).includes(query)) continue;
      results.push({
        id: `barcode:${barcode}`,
        kind: 'Barcode',
        reference: barcode,
        title: `${product.code} • ${product.name}`,
        detail: `Barcode → ${product.baseUom}`,
        exact: normalize(barcode) === query,
      });
    }
  }

  return results;
};

const searchPartners = (query: string): MockSearchResult[] => {
  const results: MockSearchResult[] = [];

  for (const partner of mockPartners) {
    if (!includesQuery([partner.code, partner.name], query)) continue;
    results.push({
      id: `partner:${partner.code}`,
      kind: 'Partner',
      reference: partner.code,
      title: partner.name,
      detail: partner.roles.join(' / '),
      exact: normalize(partner.code) === query || normalize(partner.name) === query,
    });
  }

  return results;
};

const searchOperationalRecords = (
  query: string,
  allowedWarehouses?: string[],
): MockSearchResult[] => {
  const results: MockSearchResult[] = [];
  const warehouseAllowed = (warehouse: string) =>
    !allowedWarehouses || allowedWarehouses.includes(warehouse);

  for (const workCenter of Object.values(mockWorkCenters)) {
    for (const record of workCenter.records) {
      if (!warehouseAllowed(record.warehouse)) continue;

      const searchableValues = [
        record.reference,
        record.id,
        record.subject,
        record.type,
        record.productCode ?? '',
        record.partnerCode ?? '',
        record.location ?? '',
      ];
      if (!includesQuery(searchableValues, query)) continue;

      results.push({
        id: `record:${record.id}`,
        kind: recordKind(record),
        reference: record.reference,
        title: record.subject,
        detail: [record.type, record.productCode, record.partnerCode, record.location]
          .filter(Boolean)
          .join(' • '),
        warehouse: record.warehouse,
        status: record.status,
        exact: normalize(record.reference) === query || normalize(record.id) === query,
      });
    }
  }

  return results;
};

const kindOrder: Record<MockSearchKind, number> = {
  Barcode: 0,
  Product: 1,
  Partner: 2,
  'Lot/Serial': 3,
  'Document/Task': 4,
};

const uniqueById = (results: MockSearchResult[]) =>
  results.filter(
    (result, index, all) => all.findIndex((item) => item.id === result.id) === index,
  );

const uniqueByReferenceAndTitle = (results: MockSearchResult[]) =>
  results.filter((result, index, all) => {
    const semanticKey = `${normalize(result.reference)}|${normalize(result.title)}`;
    return all.findIndex(
      (item) => `${normalize(item.reference)}|${normalize(item.title)}` === semanticKey,
    ) === index;
  });

export const searchMockSystem = (
  query: string,
  allowedWarehouses?: string[],
): MockSearchResult[] => {
  const normalizedQuery = normalize(query);
  if (!normalizedQuery) return [];

  const results = [
    ...searchProducts(normalizedQuery),
    ...searchPartners(normalizedQuery),
    ...searchOperationalRecords(normalizedQuery, allowedWarehouses),
  ];

  const ordered = uniqueById(results).sort(
    (a, b) =>
      Number(b.exact) - Number(a.exact) ||
      kindOrder[a.kind] - kindOrder[b.kind] ||
      a.reference.localeCompare(b.reference),
  );

  return uniqueByReferenceAndTitle(ordered);
};
