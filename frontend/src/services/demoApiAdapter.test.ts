// @vitest-environment jsdom
import type { InternalAxiosRequestConfig } from 'axios';
import { describe, expect, it } from 'vitest';
import {
  CANONICAL_ALL_PERMISSION_CODES,
  CANONICAL_CUSTOMER_PERMISSION_CODES,
  CANONICAL_PLATFORM_PERMISSION_CODES,
  CANONICAL_ROLE_PROFILES,
  CANONICAL_TOTAL_INVENTORY_VALUE_VND,
  canonicalBackorders,
  canonicalInventoryCurrentRows,
  canonicalKitWorkOrders,
  canonicalSaasInvoices,
  canonicalTenantSubscription,
  demoSalesOrders,
  getMockWorkCenter,
  mockInventoryBalances,
  mockRecordCount,
  mockRecountAttempts,
  mockTransferConservation,
} from '../mocks/canonicalErpWmsMockData';
import { createReadOnlyDemoApiAdapter } from './demoApiAdapter';

const request = (
  url: string,
  method = 'get',
  params?: Record<string, unknown>,
  headers: Record<string, string> = {},
  data?: unknown,
) =>
  ({
    url,
    method,
    params,
    data,
    headers,
  }) as unknown as InternalAxiosRequestConfig;

describe('Read-only demo API adapter & Single Canonical Mock Dataset', () => {
  it('serves product and canonical WH01/WH02 warehouse reads without a network backend', async () => {
    const productConfig = request('/api/v1/products');
    const products = await createReadOnlyDemoApiAdapter(productConfig)(productConfig);
    expect(products.status).toBe(200);
    expect(Array.isArray(products.data)).toBe(true);
    expect((products.data as Array<{ code: string }>)[0].code).toBe('SKU-1001');
    expect((products.data as Array<{ code: string }>).some((item) => item.code === 'QA-E2E-001')).toBe(true);
    expect((products.data as Array<{ code: string }>).some((item) => item.code === 'QA-LOT-002')).toBe(true);

    const warehouseConfig = request('/api/v1/warehouses');
    const warehouses = await createReadOnlyDemoApiAdapter(warehouseConfig)(warehouseConfig);
    const whRows = warehouses.data as Array<{ code: string; name: string }>;
    expect(whRows.some((item) => item.code === 'WH01' && item.name === 'Kho Trung tâm (WH01)')).toBe(true);
    expect(whRows.some((item) => item.code === 'WH02' && item.name === 'Kho Phụ trợ (WH02)')).toBe(true);
  });

  it('preserves work-center, balance, transfer conservation, and recount mathematical invariants', () => {
    expect(mockRecordCount).toBeGreaterThan(0);
    expect(getMockWorkCenter('warehouse-structure')?.records.length).toBeGreaterThan(0);
    expect(getMockWorkCenter('count-adjustment')?.records.length).toBeGreaterThan(0);

    for (const bal of mockInventoryBalances) {
      expect(bal.onHand).toBe(bal.available + bal.reserved + bal.qcHold + bal.quarantine);
      expect(bal.allocated).toBeLessThanOrEqual(bal.reserved);
    }

    for (const trf of mockTransferConservation) {
      expect(trf.requested).toBe(trf.source + trf.transit + trf.destination);
    }

    for (const recount of mockRecountAttempts) {
      const accepted = recount.attempts.filter((a) => a.accepted);
      expect(accepted).toHaveLength(1);
      expect(accepted[0].countedQty).toBe(recount.finalAccepted);
    }
  });

  it('enforces exact 10-role canonical permission counts and SecurityDomain separation', () => {
    expect(CANONICAL_CUSTOMER_PERMISSION_CODES).toHaveLength(141);
    expect(CANONICAL_PLATFORM_PERMISSION_CODES).toHaveLength(10);
    expect(CANONICAL_ALL_PERMISSION_CODES).toHaveLength(151);

    const expectedCounts: Record<keyof typeof CANONICAL_ROLE_PROFILES, number> = {
      SuperAdmin: 151,
      BillingAdmin: 7,
      SupportAdmin: 5,
      SecurityAuditor: 4,
      Admin: 141,
      Manager: 98,
      Supervisor: 54,
      Accountant: 42,
      WarehouseStaff: 36,
      Viewer: 18,
    };

    for (const [role, expectedCount] of Object.entries(expectedCounts) as Array<
      [keyof typeof CANONICAL_ROLE_PROFILES, number]
    >) {
      const profile = CANONICAL_ROLE_PROFILES[role];
      expect(profile.permissionCount).toBe(expectedCount);
      expect(profile.permissions).toHaveLength(expectedCount);
      expect(profile.canReadInventoryCost).toBe(profile.permissions.includes('inventory.cost.read'));

      if (profile.securityDomain === 'CUSTOMER_TENANT') {
        expect(profile.permissions.some((code) => code.startsWith('platform.'))).toBe(false);
      }
      expect(profile.permissions.includes('inventory_opening_balance.import')).toBe(
        role === 'SuperAdmin' || role === 'Admin',
      );
    }
  });

  it('reconciles Đại Phát SaaS billing, 128.62M VND WH01 inventory valuation, SO-202611-001/002, 0 backorders, and KWO-2026-001', async () => {
    expect(canonicalTenantSubscription.tenantCode).toBe('TENANT-001');
    expect(canonicalTenantSubscription.companyName).toBe('Công ty TNHH Thương mại & Kho vận Đại Phát');
    expect(canonicalTenantSubscription.taxCode).toBe('0318294012');
    expect(canonicalTenantSubscription.status).toBe('ACTIVE');
    expect(canonicalTenantSubscription.maxWarehouses).toBe(3);
    expect(canonicalTenantSubscription.beneficiary).toEqual({
      bankName: 'TPBank',
      bankBin: '970423',
      accountNo: '98418102005',
      accountName: 'HUYNH PHI VAN',
    });

    const novInv = canonicalSaasInvoices.find((i) => i.invoiceCode === 'INV-SAAS-202611-002');
    const octInv = canonicalSaasInvoices.find((i) => i.invoiceCode === 'INV-SAAS-202610-001');
    expect(novInv).toBeDefined();
    expect(novInv?.status).toBe('UNPAID');
    expect((novInv?.subTotalVnd ?? 0) + (novInv?.vatAmountVnd ?? 0)).toBe(1_490_000);
    expect(novInv?.subTotalVnd).toBe(1_354_545);
    expect(novInv?.vatAmountVnd).toBe(135_455);
    expect(novInv?.beneficiaryAccountNo).toBe('98418102005');
    expect(novInv?.beneficiaryAccountName).toBe('HUYNH PHI VAN');
    expect(octInv?.status).toBe('PAID');
    expect(octInv?.bankTransactionRef).toBe('FT2627690123');

    const vietQrCfg = request('/api/v1/permissions/saas/subscription-billing/create-vietqr-invoice', 'post');
    const vietQrRes = await createReadOnlyDemoApiAdapter(vietQrCfg)(vietQrCfg);
    expect(vietQrRes.status).toBe(200);
    expect(vietQrRes.data).toMatchObject({
      bankName: 'TPBank',
      bankBin: '970423',
      accountNo: '98418102005',
      accountName: 'HUYNH PHI VAN',
      subTotalVnd: 1_354_545,
      vatAmountVnd: 135_455,
      amountVnd: 1_490_000,
    });

    const qa001 = canonicalInventoryCurrentRows.find((r) => r.productCode === 'QA-E2E-001');
    const qa002 = canonicalInventoryCurrentRows.find((r) => r.productCode === 'QA-LOT-002');
    const qaPaper = canonicalInventoryCurrentRows.find((r) => r.productCode === 'QA-PAPER');
    expect(qa001?.totalValue).toBe(40_500_000);
    expect(qa002?.totalValue).toBe(78_120_000);
    expect(qaPaper?.totalValue).toBe(10_000_000);
    expect(CANONICAL_TOTAL_INVENTORY_VALUE_VND).toBe(128_620_000);

    const soCodes = demoSalesOrders.map((o) => o.orderCode);
    expect(soCodes).toContain('SO-202611-001');
    expect(soCodes).toContain('SO-202611-002');
    expect(canonicalBackorders).toHaveLength(0);

    const kwo = canonicalKitWorkOrders.find((k) => k.workOrderCode === 'KWO-2026-001');
    expect(kwo?.status).toBe('Completed');
    expect(kwo?.completedKitQty).toBe(20);
    expect(kwo?.rolledUpUnitCost).toBe(500_000);

    const rolesCfg = request('/api/v1/permissions/roles', 'get');
    const rolesRes = await createReadOnlyDemoApiAdapter(rolesCfg)(rolesCfg);
    const rolesData = rolesRes.data as Array<{ roleName: string; securityDomain: string }>;
    expect(rolesData.map((r) => r.roleName)).toEqual([
      'Admin',
      'Manager',
      'Supervisor',
      'Accountant',
      'WarehouseStaff',
      'Viewer',
    ]);
    expect(rolesData.some((r) => r.securityDomain === 'PLATFORM')).toBe(false);

    const permsCfg = request('/api/v1/permissions', 'get');
    const permsRes = await createReadOnlyDemoApiAdapter(permsCfg)(permsCfg);
    const permsData = permsRes.data as Array<{ code: string }>;
    expect(permsData).toHaveLength(141);
    expect(permsData.some((p) => p.code.startsWith('platform.'))).toBe(false);
  });

  it('enforces inventory.cost.read masking, Platform 403 isolation, WH02 scope, and SuperAdmin 1-step reconcile', async () => {
    for (const maskedRole of ['Supervisor', 'WarehouseStaff', 'Viewer']) {
      const cfg = request('/api/v1/inventory/current', 'get', undefined, { 'X-Mock-Role': maskedRole });
      const res = await createReadOnlyDemoApiAdapter(cfg)(cfg);
      const data = res.data as {
        costHiddenByRbac: boolean;
        totalInventoryValueVnd: number;
        items: Array<{ unitCost: number; totalValue: number; costHiddenByRbac: boolean }>;
      };
      expect(data.costHiddenByRbac).toBe(true);
      expect(data.totalInventoryValueVnd).toBe(0);
      expect(data.items.every((item) => item.unitCost === 0 && item.totalValue === 0 && item.costHiddenByRbac)).toBe(true);
    }

    for (const costRole of ['SuperAdmin', 'Admin', 'Manager', 'Accountant']) {
      const cfg = request('/api/v1/inventory/current', 'get', undefined, { 'X-Mock-Role': costRole });
      const res = await createReadOnlyDemoApiAdapter(cfg)(cfg);
      const data = res.data as { costHiddenByRbac: boolean; totalInventoryValueVnd: number };
      expect(data.costHiddenByRbac).toBe(false);
      expect(data.totalInventoryValueVnd).toBe(128_620_000);
    }

    for (const customerRole of ['Admin', 'Manager', 'Supervisor', 'Accountant', 'WarehouseStaff', 'Viewer']) {
      const platformCfg = request('/api/v1/platform/tenants', 'get', undefined, { 'X-Mock-Role': customerRole });
      await expect(createReadOnlyDemoApiAdapter(platformCfg)(platformCfg)).rejects.toMatchObject({
        response: { status: 403 },
      });
    }

    for (const wh1OnlyRole of ['WarehouseStaff', 'Viewer']) {
      const readWh2 = request('/api/v1/warehouses/2', 'get', undefined, { 'X-Mock-Role': wh1OnlyRole });
      await expect(createReadOnlyDemoApiAdapter(readWh2)(readWh2)).rejects.toMatchObject({
        response: { status: 404 },
      });
      const mutateWh2 = request(
        '/api/v1/import-receipts',
        'post',
        undefined,
        { 'X-Mock-Role': wh1OnlyRole },
        { warehouseId: 2 },
      );
      await expect(createReadOnlyDemoApiAdapter(mutateWh2)(mutateWh2)).rejects.toMatchObject({
        response: { status: 403 },
      });
    }

    const okReconcile = request(
      '/api/v1/platform/billing/invoices/2/reconcile',
      'post',
      undefined,
      { 'X-Mock-Role': 'SuperAdmin' },
      { bankTransactionRef: 'FT2630899881', bankCreditTimestampVi: '03/11/2026 21:00', receivedAmountVnd: 1_490_000 },
    );
    const reconcileRes = await createReadOnlyDemoApiAdapter(okReconcile)(okReconcile);
    expect(reconcileRes.status).toBe(200);
    expect((reconcileRes.data as { reconciliationAuditNote: string }).reconciliationAuditNote).toContain(
      'Xác nhận thủ công bởi Chủ dự án lúc 03/11/2026 21:00 (UNC: FT2630899881)',
    );

    const dupReconcile = request(
      '/api/v1/platform/billing/invoices/2/reconcile',
      'post',
      undefined,
      { 'X-Mock-Role': 'SuperAdmin' },
      { bankTransactionRef: 'FT2627690123' },
    );
    await expect(createReadOnlyDemoApiAdapter(dupReconcile)(dupReconcile)).rejects.toMatchObject({
      response: { status: 409 },
    });

    const previewDenied = request(
      '/api/v1/platform/billing/invoices/2/reconcile',
      'post',
      undefined,
      { 'X-Mock-Role': 'SuperAdmin', 'X-Platform-Role-Preview': 'SupportReadOnly' },
      { bankTransactionRef: 'FT2630899882' },
    );
    await expect(createReadOnlyDemoApiAdapter(previewDenied)(previewDenied)).rejects.toMatchObject({
      response: { status: 403 },
    });

    for (const readOnlyPlatformRole of ['SupportAdmin', 'SecurityAuditor']) {
      const subDenied = request(
        '/api/v1/platform/billing/invoices/2/reconcile',
        'post',
        undefined,
        { 'X-Mock-Role': readOnlyPlatformRole },
        { bankTransactionRef: 'FT2630899883' },
      );
      await expect(createReadOnlyDemoApiAdapter(subDenied)(subDenied)).rejects.toMatchObject({
        response: { status: 403 },
      });
    }
  });

  it('serves warehouse calendar data for the production screen', async () => {
    const config = request('/api/v1/warehouses/1/calendar');
    const response = await createReadOnlyDemoApiAdapter(config)(config);
    expect(response.status).toBe(200);
    const calendar = response.data as { warehouseId: number; timeZoneId: string; days: unknown[]; shifts: unknown[] };
    expect(calendar.warehouseId).toBe(1);
    expect(calendar.timeZoneId).toBe('Asia/Ho_Chi_Minh');
    expect(calendar.days).toHaveLength(7);
    expect(calendar.shifts.length).toBeGreaterThan(0);
  });

  it('serves Dock & Yard production reads without enabling writes', async () => {
    const warehouseConfig = request('/api/v1/dock-yard/warehouses');
    const warehouses = await createReadOnlyDemoApiAdapter(warehouseConfig)(warehouseConfig);
    expect((warehouses.data as Array<{ timeZoneId: string }>)[0].timeZoneId).toBe('Asia/Ho_Chi_Minh');

    const appointmentsConfig = request('/api/v1/dock-yard/appointments', 'get', { warehouseId: 1 });
    const appointments = await createReadOnlyDemoApiAdapter(appointmentsConfig)(appointmentsConfig);
    expect(appointments.status).toBe(200);
    expect(
      (appointments.data as Array<{ code: string; status: number }>).some(
        (item) => item.code === 'APT-2026-1042' && item.status === 3,
      ),
    ).toBe(true);

    const docksConfig = request('/api/v1/dock-yard/docks', 'get', { warehouseId: 1 });
    const docks = await createReadOnlyDemoApiAdapter(docksConfig)(docksConfig);
    expect((docks.data as Array<{ code: string }>).some((item) => item.code === 'D-02')).toBe(true);

    const detailConfig = request('/api/v1/dock-yard/appointments/1042');
    const detail = await createReadOnlyDemoApiAdapter(detailConfig)(detailConfig);
    expect((detail.data as { events: unknown[] }).events.length).toBeGreaterThan(0);

    const writeConfig = request('/api/v1/dock-yard/appointments/1042/check-in', 'post');
    await expect(createReadOnlyDemoApiAdapter(writeConfig)(writeConfig)).rejects.toMatchObject({
      response: { status: 405 },
    });
  });

  it('returns paged business partner and transfer shapes used by production pages', async () => {
    const partnerConfig = request('/api/v1/business-partners', 'get', { role: 'supplier', page: 1, pageSize: 10 });
    const partners = await createReadOnlyDemoApiAdapter(partnerConfig)(partnerConfig);
    expect((partners.data as { items: unknown[] }).items.length).toBeGreaterThan(0);
    expect((partners.data as { totalPages: number }).totalPages).toBeGreaterThan(0);

    const transferConfig = request('/api/v1/stock-transfers', 'get', { pageIndex: 1, pageSize: 15 });
    const transfers = await createReadOnlyDemoApiAdapter(transferConfig)(transferConfig);
    expect((transfers.data as { items: Array<{ code: string }> }).items[0].code).toMatch(/^TRF-/);
  });

  it('serves inventory, approval, receipt and putaway reads', async () => {
    const endpoints = [
      '/api/v1/inventory-stocks/current?',
      '/api/v1/approvals/queue?pageIndex=1&pageSize=20',
      '/api/v1/purchase-orders',
      '/api/v1/asns',
      '/api/v1/import-receipts',
      '/api/v1/export-receipts',
      '/api/v1/putaway-tasks',
      '/api/v1/stock-reservations?page=1&pageSize=20',
      '/api/v1/stocktakes',
      '/api/v1/products/kit-work-orders',
      '/api/v1/backorders',
    ];

    for (const url of endpoints) {
      const config = request(url);
      const response = await createReadOnlyDemoApiAdapter(config)(config);
      expect(response.status).toBe(200);
    }
  });

  it('serves location capacity and warehouse map snapshots from canonical data', async () => {
    const capConfig = request('/api/v1/putaway-tasks/location-capacity', 'get', { warehouseId: 1 });
    const capRes = await createReadOnlyDemoApiAdapter(capConfig)(capConfig);
    const rows = capRes.data as Array<{ code: string; state: string; maxWeightKg?: number | null }>;
    expect(capRes.status).toBe(200);
    expect(rows.some((item) => item.code === 'A01-R02-L03-B04' && item.state === 'NearCapacity')).toBe(true);
    expect(rows.find((item) => item.code === 'A01-R02-L03-B04')?.maxWeightKg).toBe(1500);

    const mapConfig = request('/api/v1/putaway-tasks/location-map', 'get', { warehouseId: 1 });
    const mapRes = await createReadOnlyDemoApiAdapter(mapConfig)(mapConfig);
    const mapData = mapRes.data as {
      warehouseId: number;
      generatedAtUtc: string;
      items: Array<{ code: string; activityLevel: string; mapX?: number | null }>;
    };
    expect(mapRes.status).toBe(200);
    expect(mapData.warehouseId).toBe(1);
    expect(mapData.generatedAtUtc).toBeTruthy();
    expect(
      mapData.items.some((item) => item.code === 'A01-R02-L03-B04' && item.activityLevel === 'High' && item.mapX === 6),
    ).toBe(true);
  });

  it('filters reversal candidates and serves reversal-aware inventory traceability', async () => {
    const moveConfig = request('/api/v1/inventory-transactions?transactionType=Move&page=1&pageSize=100');
    const moves = await createReadOnlyDemoApiAdapter(moveConfig)(moveConfig);
    const moveItems = (moves.data as { items: Array<{ transactionType: string; id: number }> }).items;
    expect(moveItems.length).toBeGreaterThan(0);
    expect(moveItems.every((item) => item.transactionType === 'Move')).toBe(true);

    const reversalConfig = request('/api/v1/inventory-transactions?transactionType=Reversal&page=1&pageSize=100');
    const reversals = await createReadOnlyDemoApiAdapter(reversalConfig)(reversalConfig);
    expect((reversals.data as { items: Array<{ referenceType: string; referenceId: number }> }).items).toContainEqual(
      expect.objectContaining({ referenceType: 'InventoryReversal', referenceId: 4 }),
    );

    const traceConfig = request('/api/v1/inventory/traceability?productId=1&lotNumber=LOT-ARABICA-2609&limit=200');
    const trace = await createReadOnlyDemoApiAdapter(traceConfig)(traceConfig);
    const data = trace.data as {
      currentBuckets: Array<{ productId: number; inventoryStatus: string }>;
      events: Array<{
        transactionId: number;
        transactionType: string;
        reversalOfTransactionId?: number | null;
        isReversed: boolean;
      }>;
    };
    expect(data.currentBuckets.some((item) => item.productId === 1 && item.inventoryStatus === 'AVAILABLE')).toBe(true);
    expect(data.events.find((item) => item.transactionId === 4)?.isReversed).toBe(true);
    expect(data.events).toContainEqual(
      expect.objectContaining({
        transactionType: 'Reversal',
        reversalOfTransactionId: 4,
      }),
    );
  });

  it('exposes a separate read-only warehouse list for traceability and paginates ledger/buckets safely', async () => {
    const choiceConfig = request('/api/v1/inventory/traceability-warehouses');
    const response = await createReadOnlyDemoApiAdapter(choiceConfig)(choiceConfig);
    const options = response.data as Array<{ id: number; code: string; name: string }>;
    expect(options.length).toBeGreaterThan(0);
    expect(options.every((item) => item.id > 0 && item.code && item.name)).toBe(true);
    const mutation = request('/api/v1/inventory/traceability-warehouses', 'post');
    await expect(createReadOnlyDemoApiAdapter(mutation)(mutation)).rejects.toMatchObject({
      response: { status: 405 },
    });

    const firstConfig = request('/api/v1/inventory/traceability?warehouseId=1&limit=1');
    const first = await createReadOnlyDemoApiAdapter(firstConfig)(firstConfig);
    const page = first.data as {
      currentBuckets: Array<{ inventoryStockId: number }>;
      events: Array<{ transactionId: number }>;
      eventsTruncated: boolean;
      bucketsTruncated: boolean;
    };
    expect(page.events).toHaveLength(1);
    expect(page.eventsTruncated).toBe(true);
    expect(page.currentBuckets.some((item) => item.inventoryStockId === 7003)).toBe(true);
    expect(page.bucketsTruncated).toBe(false);

    const secondConfig = request('/api/v1/inventory/traceability?warehouseId=1&limit=1&bucketOffset=500');
    const second = await createReadOnlyDemoApiAdapter(secondConfig)(secondConfig);
    expect((second.data as typeof page).currentBuckets).toHaveLength(0);

    const secondEventConfig = request('/api/v1/inventory/traceability?warehouseId=1&limit=1&eventOffset=1');
    const secondEvent = (await createReadOnlyDemoApiAdapter(secondEventConfig)(secondEventConfig)).data as typeof page;
    expect(secondEvent.events).toHaveLength(1);
    expect(secondEvent.events[0].transactionId).not.toBe(page.events[0].transactionId);

    for (const bad of ['-1', '3', '50500']) {
      const badCfg = request('/api/v1/inventory/traceability?warehouseId=1&limit=2&eventOffset=' + bad);
      await expect(createReadOnlyDemoApiAdapter(badCfg)(badCfg)).rejects.toMatchObject({
        response: { status: 400 },
      });
    }

    const unknownRef = request('/api/v1/inventory/traceability?warehouseId=1&referenceType=StockTransfer&referenceId=9999');
    const unknownRes = await createReadOnlyDemoApiAdapter(unknownRef)(unknownRef);
    expect((unknownRes.data as { events: unknown[]; currentBuckets: unknown[] }).events).toHaveLength(0);
    expect((unknownRes.data as { events: unknown[]; currentBuckets: unknown[] }).currentBuckets).toHaveLength(0);
  });

  it('does not expose warehouse structure pseudo-backend reads and fails closed on unmapped/write endpoints', async () => {
    for (const url of [
      '/api/v1/warehouses/1/structure',
      '/api/v1/warehouses/1/zones',
      '/api/v1/warehouses/1/locations',
      '/api/v1/locations?warehouseId=1',
      '/api/v1/not-mapped',
    ]) {
      const config = request(url);
      await expect(createReadOnlyDemoApiAdapter(config)(config)).rejects.toMatchObject({
        response: { status: 404 },
      });
    }

    const writeCfg = request('/api/v1/products', 'post');
    await expect(createReadOnlyDemoApiAdapter(writeCfg)(writeCfg)).rejects.toMatchObject({
      response: { status: 405 },
    });
  });

  it('serves report, reconciliation, transfer, permission, and diagnostic endpoints', async () => {
    const getEndpoints = [
      '/api/v1/reports/expiry-aging',
      '/api/v1/reports/analytics-summary',
      '/api/v1/reports/productivity-and-bi-snapshot?warehouseId=1',
      '/api/v1/inventory-reconciliation/investigation?warehouseId=1&productId=101',
      '/api/v1/stock-transfers/in-transit',
      '/api/v1/stock-transfers/return-reasons',
      '/api/v1/permissions/audit-logs',
      '/api/v1/permissions/compliance-evidence-bundle',
      '/api/v1/auth/sessions',
      '/api/v1/users/1/security',
      '/api/v1/users/1/warehouse-access',
    ];
    for (const url of getEndpoints) {
      const config = request(url, 'get');
      const res = await createReadOnlyDemoApiAdapter(config)(config);
      expect(res.status).toBe(200);
    }

    const postDiagnostics = [
      '/api/v1/warehouses/verify-topology-capacity',
      '/api/v1/import-receipts/initiate-rma-inspection',
      '/api/v1/inventory-reconciliation/export-erp-gl-journal',
      '/api/v1/user-warehouse-access/verify-sod-isolation',
    ];
    for (const url of postDiagnostics) {
      const config = request(url, 'post');
      const res = await createReadOnlyDemoApiAdapter(config)(config);
      expect(res.status).toBe(200);
    }
  });
});
