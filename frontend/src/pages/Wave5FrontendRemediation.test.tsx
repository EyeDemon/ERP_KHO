// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import Login from './Login';
import StockTransferDetailsDialog from './StockTransferDetailsDialog';
import ReceiptPrintPreview from '../components/ReceiptPrintPreview';
import InventoryReversals from './InventoryReversals';
import PackingSessions from './PackingSessions';
import PutawayTasks from './PutawayTasks';
import BusinessPartners from './BusinessPartners';
import Permissions from './Permissions';

describe('Wave-5 Frontend Remediation (W5-12, W5-13, W5-22, W5-23, W5-24, W5-30)', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem('role', 'Admin');
    localStorage.setItem(
      'permissions',
      JSON.stringify([
        'inventory_reversal.create',
        'inventory_traceability.read',
        'packing.read',
        'packing.execute',
        'putaway.read',
        'putaway.assign',
        'putaway.execute',
        'putaway.override',
        'partner.read',
        'partner.create',
        'partner.update',
        'permission.read',
        'role.read',
      ]),
    );
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('W5-22: roundQty4 enforces decimal(18,4) precision and sanitizeScannerCode strips AIM prefixes and control characters', () => {
    expect(roundQty4(12.34567)).toBe(12.3457);
    expect(roundQty4(10.00004)).toBe(10);
    expect(sanitizeScannerCode('\u0002]C1003893000000000123\r\n')).toBe('003893000000000123');
    expect(sanitizeScannerCode(']d2SKU-001\t')).toBe('SKU-001');
  });

  it('W5-12: Login disables submit button while authenticating and surfaces backend error messages', async () => {
    let rejectLogin!: (reason?: unknown) => void;
    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(
      () =>
        new Promise((_, reject) => {
          rejectLogin = reject;
        }),
    );

    render(
      <MemoryRouter>
        <Login />
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByLabelText('Tên đăng nhập'), { target: { value: 'admin' } });
    fireEvent.change(screen.getByLabelText('Mật khẩu'), { target: { value: 'wrong-pass' } });

    const submitBtn = screen.getByRole('button', { name: 'Đăng nhập' });
    fireEvent.click(submitBtn);

    const loadingBtn = screen.getByRole('button', { name: 'Đang đăng nhập...' }) as HTMLButtonElement;
    expect(loadingBtn.disabled).toBe(true);
    expect(postSpy).toHaveBeenCalledTimes(1);

    rejectLogin({
      response: {
        status: 401,
        data: { message: 'Thông tin đăng nhập không hợp lệ từ máy chủ.' },
      },
    });

    const alertEl = await screen.findByRole('alert');
    expect(alertEl.textContent).toContain('Thông tin đăng nhập không hợp lệ từ máy chủ.');
    const retryBtn = screen.getByRole('button', { name: 'Đăng nhập' }) as HTMLButtonElement;
    expect(retryBtn.disabled).toBe(false);
  });

  it('W5-13: InventoryReversals fails closed when verify-chain-integrity returns an empty payload without verificationId', async () => {
    vi.spyOn(apiClient, 'get').mockImplementation((url: string) => {
      if (url.includes('/api/inventory/reversal-reasons')) return Promise.resolve({ data: [] });
      if (url.includes('/api/inventory/reversal-warehouses')) return Promise.resolve({ data: [] });
      return Promise.resolve({
        data: { items: [], totalRecords: 0, pageIndex: 1, pageSize: 20, totalPages: 1 },
      });
    });
    vi.spyOn(apiClient, 'post').mockResolvedValue({ data: {} });

    render(
      <MemoryRouter>
        <InventoryReversals />
      </MemoryRouter>,
    );

    const verifyBtn = await screen.findByRole('button', {
      name: /Xác minh Toàn vẹn Chuỗi Đảo giao dịch/i,
    });
    fireEvent.click(verifyBtn);

    const alertEl = await screen.findByRole('alert');
    expect(alertEl.textContent).toContain(
      'Phản hồi xác minh chuỗi đảo giao dịch không hợp lệ từ máy chủ.',
    );
    expect(screen.queryByText(/REV-INT-VERIFIED/i)).toBeNull();
  });

  it('W5-23: StockTransferDetailsDialog and ReceiptPrintPreview support Escape dismissal and focus restoration', async () => {
    const onCloseDialog = vi.fn();
    const opener = document.createElement('button');
    opener.textContent = 'Opener';
    document.body.appendChild(opener);
    opener.focus();

    const { unmount } = render(
      <StockTransferDetailsDialog
        onClose={onCloseDialog}
        selected={{
          id: 10,
          code: 'ST-010',
          sourceWarehouseId: 1,
          sourceWarehouseName: 'Kho Nguồn',
          destinationWarehouseId: 2,
          destinationWarehouseName: 'Kho Đích',
          status: 'Completed',
          createdBy: 1,
          createdAt: '2026-10-18T00:00:00Z',
          details: [
            {
              productId: 100,
              productCode: 'SKU-100',
              productName: 'Sản phẩm 100',
              requestedQuantity: 5,
              dispatchedQuantity: 5,
              receivedQuantity: 5,
              missingQuantity: 0,
              damagedQuantity: 0,
              inTransitQuantity: 0,
            },
          ],
        }}
        status="Completed"
        statusNames={['Draft', 'Approved', 'InTransit', 'Completed']}
        statusLabel={{ Completed: 'Hoàn thành' }}
        statusTone={() => 'success'}
        canWrite={false}
        canReturn={false}
        canApprove={false}
        userId={1}
        actionInFlight={false}
        receive={{}}
        setReceive={() => {}}
        onAction={async () => {}}
        onReceive={async () => {}}
        onOpenTransfer={async () => {}}
      />,
    );

    const dialog = screen.getByRole('dialog');
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    fireEvent.keyDown(dialog, { key: 'Escape' });
    expect(onCloseDialog).toHaveBeenCalledTimes(1);

    unmount();
    expect(document.activeElement).toBe(opener);

    const onClosePreview = vi.fn();
    opener.focus();
    const previewRender = render(
      <ReceiptPrintPreview
        kind="import"
        fetchedAt={new Date('2026-10-18T00:00:00Z')}
        onClose={onClosePreview}
        receipt={{
          code: 'PN-001',
          warehouseName: 'Kho Chính',
          createdAt: '2026-10-18T00:00:00Z',
          status: 'Posted',
          details: [
            {
              id: 1,
              productCode: 'SKU-1',
              productName: 'Sản phẩm 1',
              unitName: 'Cái',
              quantity: 10,
            },
          ],
        }}
      />,
    );

    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' });
    expect(onClosePreview).toHaveBeenCalledTimes(1);
    previewRender.unmount();
    expect(document.activeElement).toBe(opener);
    opener.remove();
  });

  it('W5-24: PackingSessions HU label preview never fabricates synthetic SSCC-18 when sscc is missing', async () => {
    const sessionDto = {
      id: 7,
      sessionCode: 'PACK-007',
      pickingTaskId: 1,
      pickingTaskCode: 'PICK-001',
      sourceType: 'ExportReceipt',
      sourceCode: 'ER-001',
      warehouseId: 1,
      warehouseName: 'Kho A',
      status: 'InProgress',
      requiredQuantity: 5,
      packedQuantity: 5,
      remainingQuantity: 0,
      handlingUnitCount: 1,
      createdAt: '2026-10-18T00:00:00Z',
      rowVersion: 'v1',
      lines: [],
      handlingUnits: [
        {
          id: 99,
          huCode: 'HU-0099',
          barcode: 'BC-0099',
          warehouseId: 1,
          packingSessionId: 7,
          type: 'Carton',
          status: 'Open',
          grossWeightKg: 2.5,
          createdAt: '2026-10-18T00:00:00Z',
          rowVersion: 'v1',
          contents: [
            {
              id: 1,
              pickingTaskLineId: 10,
              productId: 1,
              productCode: 'SKU-1',
              productName: 'Sản phẩm 1',
              quantity: 5,
              packedAt: '2026-10-18T00:00:00Z',
            },
          ],
        },
      ],
    };

    vi.spyOn(apiClient, 'get').mockImplementation((url: string) => {
      if (url === '/api/packing-sessions') return Promise.resolve({ data: [sessionDto] });
      if (url === '/api/packing-sessions/7') return Promise.resolve({ data: sessionDto });
      return Promise.resolve({ data: [] });
    });

    render(
      <MemoryRouter>
        <PackingSessions />
      </MemoryRouter>,
    );

    const openBtn = await screen.findByRole('button', { name: 'PACK-007' });
    fireEvent.click(openBtn);

    const previewBtn = await screen.findByRole('button', { name: 'Xem / In nhãn kiện' });
    fireEvent.click(previewBtn);

    const labelEl = await screen.findByText(/Nhãn (?:Logistics \/ Barcode \/ SSCC|Đơn vị xử lý & Mã vạch SSCC) — HU-0099/i);
    expect(labelEl).toBeTruthy();
    expect(screen.queryByText(/0038930000000000/i)).toBeNull();
  });

  it('W5-30: PutawayTasks disables confirm button for non-positive quantity and BusinessPartners + Permissions expose accessible controls and scroll containers', async () => {
    const taskDto = {
      id: 1,
      warehouseId: 1,
      receiptCode: 'IR-001',
      warehouseName: 'Kho Chính',
      status: 'InProgress',
      assignedUserId: 1,
      requiredBaseQuantity: 10,
      movedBaseQuantity: 0,
      rowVersion: 'v1',
      items: [
        {
          id: 11,
          productCode: 'SKU-01',
          productName: 'Sản phẩm 01',
          inventoryStatus: 'Available',
          sourceLocationCode: 'RCV-01',
          operationUnitCode: 'Cái',
          baseUnitCode: 'Cái',
          requiredOperationQuantity: 10,
          requiredBaseQuantity: 10,
          movedBaseQuantity: 0,
          remainingBaseQuantity: 10,
        },
      ],
    };

    vi.spyOn(apiClient, 'get').mockImplementation((url: string) => {
      if (url === '/api/putaway-tasks') return Promise.resolve({ data: [taskDto] });
      if (url === '/api/putaway-tasks/1') return Promise.resolve({ data: taskDto });
      if (url.includes('/destinations')) {
        return Promise.resolve({
          data: [{ id: 2, code: 'STG-01', name: 'Kệ 01', locationType: 'Bin', isPickable: true }],
        });
      }
      if (url.startsWith('/api/business-partners')) {
        return Promise.resolve({ data: { items: [], totalPages: 1 } });
      }
      if (url === '/api/permissions') {
        return Promise.resolve({
          data: [{ code: 'product.read', name: 'Xem sản phẩm', module: 'MasterData' }],
        });
      }
      if (url === '/api/permissions/roles') {
        return Promise.resolve({
          data: [{ id: 1, roleName: 'Admin', rowVersion: 'v1', permissions: ['product.read'] }],
        });
      }
      return Promise.resolve({ data: [] });
    });

    const putawayView = render(
      <MemoryRouter>
        <PutawayTasks />
      </MemoryRouter>,
    );

    fireEvent.click(await screen.findByRole('button', { name: 'IR-001' }));
    const destSelect = await screen.findByLabelText('Vị trí đích SKU-01');
    fireEvent.change(destSelect, { target: { value: '2' } });
    const qtyInput = await screen.findByLabelText('Số lượng cất SKU-01');
    fireEvent.change(qtyInput, { target: { value: '0' } });

    const confirmBtn = screen.getByRole('button', { name: 'Xác nhận số lượng' }) as HTMLButtonElement;
    expect(confirmBtn.disabled).toBe(true);
    putawayView.unmount();

    const bpView = render(
      <MemoryRouter>
        <BusinessPartners />
      </MemoryRouter>,
    );
    const addPartnerBtn = await screen.findByRole('button', { name: 'Thêm đối tác' });
    fireEvent.click(addPartnerBtn);
    expect(await screen.findByLabelText('Mã đối tác')).toBeTruthy();
    expect(screen.getByLabelText('Tên đối tác')).toBeTruthy();
    expect(screen.getByLabelText('Điện thoại đối tác')).toBeTruthy();
    expect(screen.getByLabelText('Email đối tác')).toBeTruthy();
    expect(screen.getByLabelText('Địa chỉ đối tác')).toBeTruthy();
    bpView.unmount();

    const permView = render(
      <MemoryRouter>
        <Permissions />
      </MemoryRouter>,
    );
    await waitFor(() => {
      expect(permView.container.querySelector('.ui-table-scroll')).not.toBeNull();
    });
    permView.unmount();
  });

  it('W6-02: apiClient reuses auto Idempotency-Key only while in-flight, and generates a fresh key once 200 OK or 4xx/5xx response is received', async () => {
    const capturedKeys: string[] = [];
    const resolvers: Array<(val: unknown) => void> = [];

    const originalAdapter = apiClient.defaults.adapter;
    try {
      // 1. Two concurrent in-flight POST requests with identical payload share the same auto-generated key
      apiClient.defaults.adapter = (config) => {
        capturedKeys.push(String(config.headers?.['Idempotency-Key'] ?? ''));
        return new Promise((resolve) => {
          resolvers.push((val) =>
            resolve({
              ...(val as Record<string, unknown>),
              data: { ok: true },
              status: 200,
              statusText: 'OK',
              headers: {},
              config,
            }),
          );
        });
      };

      const p1 = apiClient.post('/api/packing-sessions/1/items', { productId: 10, quantity: 1 });
      const p2 = apiClient.post('/api/packing-sessions/1/items', { productId: 10, quantity: 1 });
      await waitFor(() => {
        expect(capturedKeys).toHaveLength(2);
      });
      expect(capturedKeys[0]).toMatch(/^auto-post-/);
      expect(capturedKeys[1]).toBe(capturedKeys[0]);

      for (const r of resolvers) {
        r({
          data: { ok: true },
          status: 200,
          statusText: 'OK',
          headers: {},
        });
      }
      await Promise.all([p1, p2]);

      // 2. Sequential scan #2 right after 200 OK with the exact same payload gets a BRAND NEW key
      apiClient.defaults.adapter = async (config) => {
        capturedKeys.push(String(config.headers?.['Idempotency-Key'] ?? ''));
        return {
          data: { ok: true },
          status: 200,
          statusText: 'OK',
          headers: {},
          config,
        };
      };

      await apiClient.post('/api/packing-sessions/1/items', { productId: 10, quantity: 1 });
      expect(capturedKeys).toHaveLength(3);
      expect(capturedKeys[2]).not.toBe(capturedKeys[0]);

      // 3. W7-01: Truncated idempotent replay envelope is normalized with safe scalar & array defaults
      apiClient.defaults.adapter = async (config) => ({
        data: { idempotentReplay: true, truncated: true, statusCode: 200, id: 77, code: 'IMP-77' },
        status: 200,
        statusText: 'OK',
        headers: {},
        config,
      });
      const replayRes = await apiClient.post('/api/import-receipts/77/post', {});
      expect(replayRes.data.id).toBe(77);
      expect(replayRes.data.code).toBe('IMP-77');
      expect(replayRes.data.status).toBe('Completed');
      expect(Array.isArray(replayRes.data.lines)).toBe(true);
      expect(Array.isArray(replayRes.data.items)).toBe(true);
      expect(Array.isArray(replayRes.data.details)).toBe(true);
    } finally {
      apiClient.defaults.adapter = originalAdapter;
    }
  });
});

