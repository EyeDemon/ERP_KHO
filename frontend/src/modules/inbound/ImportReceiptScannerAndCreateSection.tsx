import React from 'react';
import { MobileBarcodeScannerBar } from '../../components/MobileBarcodeScannerBar';
import { UiCard, UiTabs } from '../../ui/ProductionUi';
import { warehouseLocationTypeLabelVi } from '../../ui/vietnameseEnumLabels';
import {
  type ImportReceipt,
  type Warehouse,
  type WarehouseLocationItem,
} from './ImportReceiptTypes';

export interface ImportReceiptWorkspaceTabsProps {
  activeWorkspaceTab: 'receipts' | 'scanner' | 'rma';
  setActiveWorkspaceTab: (tab: 'receipts' | 'scanner' | 'rma') => void;
  receiptsCount: number;
  setReceivingWorkspaceNotice: (msg: string) => void;
  setReceivingConfirmedSignature: (sig: string) => void;
}

export const ImportReceiptWorkspaceTabs: React.FC<ImportReceiptWorkspaceTabsProps> = ({
  activeWorkspaceTab,
  setActiveWorkspaceTab,
  receiptsCount,
  setReceivingWorkspaceNotice,
  setReceivingConfirmedSignature,
}) => (
  <div className="no-print" data-testid="import-workspace-tabs">
    <UiTabs<'receipts' | 'scanner' | 'rma'>
      ariaLabel="Chế độ làm việc Quản lý nhập kho"
      activeTab={activeWorkspaceTab}
      onChange={(tab) => {
        if (tab === 'scanner') {
          setReceivingWorkspaceNotice('');
          setReceivingConfirmedSignature('');
        }
        setActiveWorkspaceTab(tab);
      }}
      tabs={[
        {
          id: 'receipts',
          label: `Danh sách phiếu nhập (${receiptsCount})`,
          testId: 'tab-import-receipts',
        },
        {
          id: 'scanner',
          label: 'Trạm quét nhận hàng',
          testId: 'tab-import-scanner',
        },
      ]}
    />
  </div>
);

export interface ImportReceiptScannerSectionProps {
  scannerContainerRef: React.RefObject<HTMLDivElement | null>;
  activeWorkspaceTab: 'receipts' | 'scanner' | 'rma';
  warehouseId: number | '';
  warehouses: Warehouse[];
  receipts: ImportReceipt[];
  creating: boolean;
  workflowId: number | null;
  receivingReceiptId: string;
  setReceivingReceiptId: (val: string) => void;
  receivingFromLocationId: string;
  setReceivingFromLocationId: (val: string) => void;
  receivingToLocationId: string;
  setReceivingToLocationId: (val: string) => void;
  receivingMoveQty: number;
  setReceivingMoveQty: (val: number) => void;
  receivingWorkspaceNotice: string;
  setReceivingWorkspaceNotice: (val: string) => void;
  setReceivingConfirmedSignature: (val: string) => void;
  currentReceivingSignature: string;
  isReceivingAlreadyConfirmed: boolean;
  warehouseLocations: WarehouseLocationItem[];
  selectedReceipt: ImportReceipt | null;
  setSelectedReceipt: (val: ImportReceipt | null) => void;
  handleViewDetails: (id: number) => Promise<void>;
  focusScannerInput: () => void;
  setError: (val: string) => void;
}

export const ImportReceiptScannerSection: React.FC<ImportReceiptScannerSectionProps> = ({
  scannerContainerRef,
  activeWorkspaceTab,
  warehouseId,
  warehouses,
  receipts,
  creating,
  workflowId,
  receivingReceiptId,
  setReceivingReceiptId,
  receivingFromLocationId,
  setReceivingFromLocationId,
  receivingToLocationId,
  setReceivingToLocationId,
  receivingMoveQty,
  setReceivingMoveQty,
  receivingWorkspaceNotice,
  setReceivingWorkspaceNotice,
  setReceivingConfirmedSignature,
  currentReceivingSignature,
  isReceivingAlreadyConfirmed,
  warehouseLocations,
  selectedReceipt,
  setSelectedReceipt,
  handleViewDetails,
  focusScannerInput,
  setError,
}) => (
  <div
    ref={scannerContainerRef}
    className="no-print import-pda-scanner-zone"
    style={activeWorkspaceTab === 'scanner' ? undefined : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }}
  >
    <MobileBarcodeScannerBar
      warehouseId={warehouseId ? Number(warehouseId) : (warehouses[0]?.id ?? undefined)}
      sampleCode={receipts[0]?.code || 'PNK-001'}
      disabled={creating || workflowId !== null}
      onScan={(scannedCode) => {
        const match = receipts.find(r => r.code.toUpperCase() === scannedCode.toUpperCase());
        if (match) {
          setReceivingReceiptId(String(match.id));
          void handleViewDetails(match.id);
        }
        focusScannerInput();
      }}
    />
    {activeWorkspaceTab === 'scanner' && (
      <UiCard
        title="Bàn điều phối tiếp nhận & cất hàng lên kệ"
        subtitle="Chọn phiếu nhập, vị trí nguồn và vị trí kệ đích để xác nhận điều phối tại trạm PDA"
      >
        <div className="import-form-grid">
          <label className="ui-stack">
            <span>Phiếu nhập / Kiện hàng tiếp nhận</span>
            <select
              aria-label="Phiếu nhập tiếp nhận tại trạm PDA"
              value={receivingReceiptId}
              onChange={e => {
                setReceivingReceiptId(e.target.value);
                setReceivingWorkspaceNotice('');
                setReceivingConfirmedSignature('');
                if (e.target.value) void handleViewDetails(Number(e.target.value));
                else setSelectedReceipt(null);
              }}
            >
              <option value="">-- Chọn phiếu nhập / kiện hàng --</option>
              {receipts.map(r => (
                <option key={r.id} value={String(r.id)}>
                  {r.code} — {r.warehouseName}{r.supplierName ? ` (${r.supplierName})` : ''}
                </option>
              ))}
            </select>
          </label>
          <label className="ui-stack">
            <span>Vị trí nguồn (Bãi tiếp nhận / Sàn chờ)</span>
            <select
              aria-label="Vị trí nguồn tiếp nhận (Receiving / Staging)"
              value={receivingFromLocationId}
              onChange={e => {
                setReceivingFromLocationId(e.target.value);
                setReceivingWorkspaceNotice('');
                setReceivingConfirmedSignature('');
              }}
            >
              <option value="">-- Chọn vị trí nguồn (Khu tiếp nhận / Tập kết) --</option>
              {(warehouseLocations.filter(l => ['receiving', 'staging'].includes(l.locationType?.toLowerCase())).length > 0
                ? warehouseLocations.filter(l => ['receiving', 'staging'].includes(l.locationType?.toLowerCase()))
                : warehouseLocations
              ).map(loc => (
                <option key={loc.id} value={String(loc.id)}>
                  {loc.code} — {loc.name} ({warehouseLocationTypeLabelVi(loc.locationType)})
                </option>
              ))}
            </select>
          </label>
          <label className="ui-stack">
            <span>Vị trí đích (Kệ lưu trữ kho)</span>
            <select
              aria-label="Vị trí đích cất hàng (Storage)"
              value={receivingToLocationId}
              onChange={e => {
                setReceivingToLocationId(e.target.value);
                setReceivingWorkspaceNotice('');
                setReceivingConfirmedSignature('');
              }}
            >
              <option value="">-- Chọn vị trí đích (Kệ lưu trữ) --</option>
              {(warehouseLocations.filter(l => l.locationType?.toLowerCase() === 'storage').length > 0
                ? warehouseLocations.filter(l => l.locationType?.toLowerCase() === 'storage')
                : warehouseLocations
              ).map(loc => (
                <option key={loc.id} value={String(loc.id)}>
                  {loc.code} — {loc.name} ({warehouseLocationTypeLabelVi(loc.locationType)})
                </option>
              ))}
            </select>
          </label>
          <label className="ui-stack">
            <span>Số lượng thực nhận / thực cất</span>
            <input
              inputMode="decimal"
              role="spinbutton"
              min="0.01"
              step="0.01"
              aria-label="Số lượng thực nhận / thực cất"
              value={receivingMoveQty}
              onChange={e => {
                setReceivingMoveQty(Math.max(0.01, Number(e.target.value) || 1));
                setReceivingWorkspaceNotice('');
                setReceivingConfirmedSignature('');
              }}
            />
          </label>
        </div>
        <div className="ui-inline-actions" style={{ marginTop: 10 }}>
          <button
            type="button"
            disabled={isReceivingAlreadyConfirmed}
            style={{
              minHeight: 44,
              background: isReceivingAlreadyConfirmed ? '#64748b' : '#1d4ed8',
              color: '#ffffff',
              borderColor: isReceivingAlreadyConfirmed ? '#64748b' : '#1d4ed8',
              fontWeight: 600,
            }}
            onClick={() => {
              if (isReceivingAlreadyConfirmed) return;
              const targetReceipt = receipts.find(r => String(r.id) === receivingReceiptId);
              const fromLoc = warehouseLocations.find(l => String(l.id) === receivingFromLocationId);
              const toLoc = warehouseLocations.find(l => String(l.id) === receivingToLocationId);
              if (!receivingReceiptId || !targetReceipt || !receivingFromLocationId || !fromLoc || !receivingToLocationId || !toLoc) {
                setReceivingWorkspaceNotice('');
                setError('Vui lòng chọn đầy đủ Phiếu nhập, Vị trí nguồn và Vị trí đích trước khi xác nhận điều phối.');
                return;
              }
              if (!window.confirm(`Xác nhận tiếp nhận & điều phối cất hàng cho phiếu ${targetReceipt.code} (${fromLoc.code} → ${toLoc.code}, số lượng ${receivingMoveQty} ĐV)?`)) {
                return;
              }
              setError('');
              if (selectedReceipt?.id !== targetReceipt.id) {
                void handleViewDetails(Number(targetReceipt.id));
              }
              setReceivingConfirmedSignature(currentReceivingSignature);
              setReceivingWorkspaceNotice(
                `Đã chốt điều phối tiếp nhận & cất hàng cho phiếu ${targetReceipt.code}: Từ vị trí nguồn ${fromLoc.code} (${fromLoc.name}) → Vị trí đích ${toLoc.code} (${toLoc.name}) • Số lượng: ${receivingMoveQty} ĐV.`,
              );
              focusScannerInput();
            }}
          >
            {isReceivingAlreadyConfirmed ? 'Đã xác nhận tiếp nhận & điều phối cất hàng' : 'Xác nhận tiếp nhận & điều phối cất hàng'}
          </button>
        </div>
        {receivingWorkspaceNotice && (
          <div role="status" style={{ marginTop: 10, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#065f46', fontSize: 13, fontWeight: 500 }}>
            {receivingWorkspaceNotice}
          </div>
        )}
      </UiCard>
    )}
  </div>
);
