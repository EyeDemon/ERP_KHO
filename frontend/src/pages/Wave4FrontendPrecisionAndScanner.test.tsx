// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ReceiptInventoryIdentityEditor from '../components/ReceiptInventoryIdentityEditor';
import apiClient from '../services/apiClient';
import { normalizeBarcodeInput, roundQty4, sanitizeScannerCode } from './productCatalog';

describe('Wave-4 Sector D (REM-16): Frontend Precision, Scanner Hygiene & Idempotency', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('W4-SEC-D-01: roundQty4 eliminates IEEE-754 floating-point drift at scale 4', () => {
    expect(0.1 + 0.2).not.toBe(0.3);
    expect(roundQty4(0.1 + 0.2)).toBe(0.3);
    expect(roundQty4(0.1 + 0.1 + 0.1)).toBe(0.3);
    expect(roundQty4(1.0 - 0.9)).toBe(0.1);
    expect(roundQty4(1.3 - 1.1)).toBe(0.2);
    expect(roundQty4(Number.NaN)).toBe(0);
    expect(roundQty4(Number.POSITIVE_INFINITY)).toBe(0);
  });

  it('W4-SEC-D-02: sanitizeScannerCode and normalizeBarcodeInput strip GS1-128 FNC1, CR/LF, BOM/ZWSP and preserve leading zeros', () => {
    const rawGs1Scan = '\u001d0012389\r\n\u200b\ufeff';
    expect(sanitizeScannerCode(rawGs1Scan)).toBe('0012389');
    expect(normalizeBarcodeInput(rawGs1Scan)).toBe('0012389');

    const rawLotScan = ' \u0002LOT-2026\u001d-A01\u0003\r\n ';
    expect(sanitizeScannerCode(rawLotScan)).toBe('LOT-2026-A01');
  });

  it('W4-SEC-D-01 & W4-SEC-D-02: ReceiptInventoryIdentityEditor sums 0.1 + 0.1 + 0.1 as 0.3 without float drift and sanitizes GS1 control chars on save', async () => {
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: [
        { id: 1, lineId: 10, productId: 100, targetStatus: 'AVAILABLE', baseQuantity: 0.1, lotNumber: 'LOT-01' },
        { id: 2, lineId: 10, productId: 100, targetStatus: 'AVAILABLE', baseQuantity: 0.1, lotNumber: 'LOT-02' },
      ],
    });
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValue({
      data: [
        { id: 1, lineId: 10, productId: 100, targetStatus: 'AVAILABLE', baseQuantity: 0.1, lotNumber: 'LOT-01' },
        { id: 2, lineId: 10, productId: 100, targetStatus: 'AVAILABLE', baseQuantity: 0.1, lotNumber: 'LOT-02' },
        { id: 3, lineId: 10, productId: 100, targetStatus: 'AVAILABLE', baseQuantity: 0.1, lotNumber: '009988' },
      ],
    });

    render(
      <ReceiptInventoryIdentityEditor
        receiptId={55}
        status="Received"
        canUpdate={true}
        lines={[
          {
            id: 10,
            productId: 100,
            productCode: 'SKU-DEC-01',
            productName: 'Hóa chất chuẩn độ',
            trackingType: 'Lot',
            expiryControl: false,
            baseAcceptedQuantity: 0.3,
            baseDamagedQuantity: 0,
            baseRejectedQuantity: 0,
            baseUnitCode: 'KG',
          },
        ]}
      />,
    );

    await waitFor(() => expect(getSpy).toHaveBeenCalledWith('/api/importreceipts/55/inventory-identities'));
    expect(screen.getByText('AVAILABLE 0.2/0.3 KG')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Thêm lot' }));

    const qtyInput2 = screen.getByLabelText('Identity quantity SKU-DEC-01 2') as HTMLInputElement;
    expect(qtyInput2.value).toBe('0.1');
    expect(screen.getByText('AVAILABLE 0.3/0.3 KG')).toBeTruthy();

    const lotInput2 = screen.getByLabelText('Identity lot SKU-DEC-01 2');
    fireEvent.change(lotInput2, { target: { value: '\u001d009988\r\n\u200b' } });

    fireEvent.click(screen.getByRole('button', { name: 'Lưu Lot / Serial / Expiry' }));

    await waitFor(() => expect(postSpy).toHaveBeenCalledTimes(1));
    expect(postSpy.mock.calls[0]?.[1]).toEqual({
      lines: [
        { lineId: 10, targetStatus: 'AVAILABLE', baseQuantity: 0.1, lotNumber: 'LOT-01', manufactureDate: null, expiryDate: null, serialNumber: null },
        { lineId: 10, targetStatus: 'AVAILABLE', baseQuantity: 0.1, lotNumber: 'LOT-02', manufactureDate: null, expiryDate: null, serialNumber: null },
        { lineId: 10, targetStatus: 'AVAILABLE', baseQuantity: 0.1, lotNumber: '009988', manufactureDate: null, expiryDate: null, serialNumber: null },
      ],
    });
  });

  it('W4-SEC-D-04: apiClient request interceptor auto-attaches Idempotency-Key on write requests when missing', async () => {
    const handlers = (apiClient.interceptors.request as unknown as {
      handlers: Array<{ fulfilled: (config: Record<string, unknown>) => Record<string, unknown> }>;
    }).handlers;
    const requestInterceptor = handlers[0]?.fulfilled;
    expect(requestInterceptor).toBeTypeOf('function');

    const postConfig = requestInterceptor({
      method: 'post',
      url: '/api/products',
      headers: {},
    }) as { headers: Record<string, string> };
    expect(postConfig.headers['Idempotency-Key']).toMatch(/^auto-post-/);

    const existingKeyConfig = requestInterceptor({
      method: 'put',
      url: '/api/products/1',
      headers: { 'Idempotency-Key': 'custom-key-123' },
    }) as { headers: Record<string, string> };
    expect(existingKeyConfig.headers['Idempotency-Key']).toBe('custom-key-123');

    const getConfig = requestInterceptor({
      method: 'get',
      url: '/api/products',
      headers: {},
    }) as { headers: Record<string, string> };
    expect(getConfig.headers['Idempotency-Key']).toBeUndefined();
  });
});
