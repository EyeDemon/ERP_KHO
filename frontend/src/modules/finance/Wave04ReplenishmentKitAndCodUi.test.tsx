// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../services/apiClient';
import { setCurrentPermissions } from '../../services/authorization';
import { MinMaxReplenishmentPanel } from '../inventory/MinMaxReplenishmentPanel';
import { ProductBundleAndGiftSection } from '../catalog/ProductBundleAndGiftSection';
import CodReconciliation from '../../pages/CodReconciliation';

vi.mock('../../services/apiClient', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const mockedGet = vi.mocked(apiClient.get);
const mockedPost = vi.mocked(apiClient.post);
const mockedPut = vi.mocked(apiClient.put);

describe('Wave 04 — Min/Max Replenishment, KitWorkOrder FEFO & RBAC Cost, and COD Packing Evidence UI', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  afterEach(() => {
    cleanup();
  });

  it('renders MinMaxReplenishmentPanel FEFO suggestions, saves Min/Max rule, and executes 1-click FEFO replenishment', async () => {
    mockedGet.mockResolvedValue({
      data: {
        suggestions: [
          {
            productId: 101,
            productCode: 'SKU-FEFO-101',
            productName: 'Sữa Tươi Tiệt Trùng 1L',
            sourceLocationId: 10,
            sourceLocationCode: 'BULK-RES-01',
            sourceLotNumber: 'LOT-2026-EARLY',
            sourceExpiryDate: '2026-11-15T00:00:00Z',
            targetLocationId: 5,
            targetLocationCode: 'PICK-FACE-A01',
            currentPickFaceQty: 4,
            minBaseQty: 15,
            maxBaseQty: 80,
            neededQty: 76,
            priority: 'HIGH',
          },
        ],
        configuredLocations: [
          {
            locationId: 5,
            locationCode: 'PICK-FACE-A01',
            minBaseQty: 15,
            maxBaseQty: 80,
            isPickable: true,
          },
        ],
      },
    } as never);

    mockedPut.mockResolvedValue({
      data: { warehouseId: 1, locationId: 5, minBaseQty: 20, maxBaseQty: 120, isPickable: true },
    } as never);

    mockedPost.mockResolvedValue({
      data: {
        movedQty: 76,
        sourceLotNumber: 'LOT-2026-EARLY',
        sourceExpiryDate: '2026-11-15T00:00:00Z',
        targetQuantityAfter: 80,
        targetLocationCode: 'PICK-FACE-A01',
      },
    } as never);

    const onExecuted = vi.fn();
    render(<MinMaxReplenishmentPanel warehouseId={1} onExecuted={onExecuted} />);

    await screen.findByText(/LOT-2026-EARLY/);
    expect(screen.getByTestId('replenishment-minmax-panel')).toBeTruthy();

    fireEvent.change(screen.getByTestId('input-replenish-min-qty'), { target: { value: '20' } });
    fireEvent.change(screen.getByTestId('input-replenish-max-qty'), { target: { value: '120' } });
    fireEvent.click(screen.getByTestId('btn-save-minmax-rule'));

    await waitFor(() => {
      expect(mockedPut).toHaveBeenCalledWith(
        '/api/v1/warehouses/1/locations/5/replenishment-rule',
        expect.objectContaining({ warehouseId: 1, locationId: 5, minBaseQty: 20, maxBaseQty: 120, isPickable: true }),
        expect.objectContaining({ headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }) }),
      );
    });

    fireEvent.click(screen.getByTestId('btn-execute-minmax-replenish'));

    await waitFor(() => {
      expect(mockedPost).toHaveBeenCalledWith(
        '/api/v1/inventory-control/replenishment/generate-and-execute',
        expect.objectContaining({ warehouseId: 1, productId: 101, targetLocationId: 5 }),
        expect.objectContaining({ headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }) }),
      );
    });

    const resultBanner = await screen.findByTestId('replenish-execution-result');
    expect(resultBanner.textContent).toContain('LOT-2026-EARLY');
    expect(resultBanner.textContent).toContain('76');
    expect(onExecuted).toHaveBeenCalledTimes(1);
  });

  it('displays EarliestComponentExpiryDate and RolledUpUnitCost when user has inventory.cost.read, and masks cost for Staff', async () => {
    mockedGet.mockResolvedValue({
      data: [
        {
          id: 7,
          workOrderCode: 'KWO-2026-007',
          workOrderType: 'Assemble',
          kitProductId: 1,
          kitProductCode: 'SKU-KIT-TET',
          kitProductName: 'Giỏ Quà Tết Đặc Biệt',
          baseUomCode: 'SET',
          conversionRateSnapshot: 1,
          plannedKitQty: 5,
          completedKitQty: 5,
          rolledUpUnitCost: 145000,
          canReadCost: true,
          status: 'Completed',
          earliestComponentExpiryDate: '2026-12-20T00:00:00Z',
          completedLotNumber: 'LOT-KIT-007',
        },
      ],
    } as never);

    localStorage.setItem('role', 'Admin');
    setCurrentPermissions(['inventory.cost.read']);

    const { unmount } = render(
      <ProductBundleAndGiftSection
        activeTab="bundle-gift"
        products={[]}
        activeUomProduct={null}
        mutating={false}
        mutate={async (fn) => await fn()}
        setNotice={vi.fn()}
        setError={vi.fn()}
      />,
    );

    const expiryCell = await screen.findByTestId('kit-wo-earliest-expiry');
    expect(expiryCell.textContent).toContain('LOT-KIT-007');
    const costCell = screen.getByTestId('kit-wo-rolledup-cost');
    expect(costCell.textContent).toContain('145.000');

    unmount();
    cleanup();

    localStorage.setItem('role', 'Staff');
    setCurrentPermissions(['products.read']);

    render(
      <ProductBundleAndGiftSection
        activeTab="bundle-gift"
        products={[]}
        activeUomProduct={null}
        mutating={false}
        mutate={async (fn) => await fn()}
        setNotice={vi.fn()}
        setError={vi.fn()}
      />,
    );

    const maskedCostCell = await screen.findByTestId('kit-wo-rolledup-cost');
    expect(maskedCostCell.textContent).toContain('Ẩn theo quyền RBAC');
  });

  it('opens CodPackingEvidenceDrawer from CodReconciliation and displays weight variance, HU code, and SHA-256 hash', async () => {
    mockedGet.mockResolvedValue({
      data: {
        items: [
          {
            id: 301,
            trackingNumber: 'GHN-DISPUTE-301',
            carrierCode: 'GHN',
            expectedCod: 650000,
            actualCod: 650000,
            expectedShippingFee: 28000,
            actualShippingFee: 52000,
            shippingFeeVariance: 24000,
            packedGrossWeightKg: 1.35,
            carrierBilledWeightKg: 3.1,
            weightVarianceKg: 1.75,
            handlingUnitCode: 'HU-PACK-00301',
            packingStationCode: 'PACK-ST-02',
            packedAtUtc: '2026-10-31T09:15:00Z',
            packingVideoHashSha256: 'a9f5b2d4c8e1f03456789abcdef0123456789abcdef0123456789abcdef01234',
            status: 'FEE_DISCREPANCY',
          },
        ],
      },
    } as never);

    render(<CodReconciliation />);

    const evidenceBtn = await screen.findByTestId('btn-view-cod-packing-evidence');
    fireEvent.click(evidenceBtn);

    const drawer = await screen.findByTestId('cod-packing-evidence-drawer');
    expect(drawer).toBeTruthy();
    expect(screen.getByTestId('evidence-packed-weight').textContent).toBe('1.35 kg');
    expect(screen.getByTestId('evidence-carrier-weight').textContent).toBe('3.10 kg');
    expect(screen.getByTestId('evidence-weight-variance').textContent).toBe('1.75 kg');
    expect(screen.getByTestId('evidence-hu-code').textContent).toBe('HU-PACK-00301');
    expect(screen.getByTestId('evidence-video-sha256').textContent).toBe(
      'a9f5b2d4c8e1f03456789abcdef0123456789abcdef0123456789abcdef01234',
    );

    fireEvent.click(screen.getByTestId('btn-copy-dispute-evidence'));
    expect(await screen.findByTestId('cod-dispute-copied-notice')).toBeTruthy();
  });
});
