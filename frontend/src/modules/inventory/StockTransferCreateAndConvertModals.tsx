import { useState, type FormEvent, type RefObject } from 'react';
import { ArrowRight, Plus, X } from 'lucide-react';
import type { Line, Product, Warehouse } from './stockTransfersShared';

export interface StockTransferDraftModalProps {
  draftDialogRef: RefObject<HTMLFormElement | null>;
  editingId: number | null;
  editingRevision: number | null;
  draftConflict: boolean;
  createInFlight: boolean;
  error: string;
  sourceId: number | '';
  setSourceId: (v: number | '') => void;
  destinationId: number | '';
  setDestinationId: (v: number | '') => void;
  note: string;
  setNote: (v: string) => void;
  internalTransferOrderNo?: string;
  setInternalTransferOrderNo?: (v: string) => void;
  transporterName?: string;
  setTransporterName?: (v: string) => void;
  vehiclePlateNumber?: string;
  setVehiclePlateNumber?: (v: string) => void;
  lines: Line[];
  setLines: (v: Line[]) => void;
  warehouses: Warehouse[];
  products: Product[];
  onCloseDraftEditor: () => void;
  onReloadConflictedDraft: () => void;
  onSubmitCreate: (event: FormEvent) => void;
}

export function StockTransferDraftModal(props: StockTransferDraftModalProps) {
  const {
    draftDialogRef, editingId, editingRevision, draftConflict, createInFlight, error,
    sourceId, setSourceId, destinationId, setDestinationId, note, setNote,
    internalTransferOrderNo, setInternalTransferOrderNo,
    transporterName, setTransporterName,
    vehiclePlateNumber, setVehiclePlateNumber,
    lines, setLines, warehouses, products, onCloseDraftEditor, onReloadConflictedDraft, onSubmitCreate,
  } = props;
  const [localOrderNo, setLocalOrderNo] = useState('');
  const [localTransporter, setLocalTransporter] = useState('');
  const [localPlate, setLocalPlate] = useState('');

  const orderNoVal = internalTransferOrderNo !== undefined ? internalTransferOrderNo : localOrderNo;
  const setOrderNoVal = setInternalTransferOrderNo ?? setLocalOrderNo;
  const transporterVal = transporterName !== undefined ? transporterName : localTransporter;
  const setTransporterVal = setTransporterName ?? setLocalTransporter;
  const plateVal = vehiclePlateNumber !== undefined ? vehiclePlateNumber : localPlate;
  const setPlateVal = setVehiclePlateNumber ?? setLocalPlate;

  const labelStyle = { display: 'flex', flexDirection: 'column' as const, gap: 6, alignItems: 'stretch', justifyContent: 'flex-start', height: 'auto', margin: 0 };

  return (
    <div className="transfer-modal" role="presentation">
      <form
        ref={draftDialogRef}
        className="transfer-dialog"
        style={{ maxWidth: 720, width: '100%' }}
        role="dialog"
        aria-modal="true"
        aria-labelledby="transfer-create-title"
        onSubmit={onSubmitCreate}
        onKeyDown={(event) => {
          if (event.key === 'Escape' && !createInFlight) {
            event.stopPropagation();
            onCloseDraftEditor();
          }
        }}
      >
        <div className="dialog-title">
          <h2 id="transfer-create-title">{editingId === null ? 'Tạo phiếu điều chuyển' : 'Chỉnh sửa phiếu nháp'}</h2>
          <button type="button" aria-label="Đóng tạo phiếu" disabled={createInFlight} onClick={onCloseDraftEditor}>
            <X aria-hidden="true" />
          </button>
        </div>

        {error && <p role="alert" className="transfer-error">{error}</p>}
        {editingId !== null && editingRevision !== null && (
          <p className="ui-muted-text">Phiên bản nháp: {editingRevision}. Hệ thống từ chối lưu nếu người khác đã chỉnh sửa phiếu.</p>
        )}
        {editingId !== null && draftConflict && (
          <p role="status" className="ui-muted-text">
            Phiếu đã thay đổi ở phiên khác. Tải lại dữ liệu mới nhất trước khi chỉnh sửa tiếp.
            <button type="button" disabled={createInFlight} onClick={onReloadConflictedDraft}>
              Tải lại phiên bản mới
            </button>
          </p>
        )}
        <div className="warehouse-pair" style={{ display: 'grid', gridTemplateColumns: '1fr auto 1fr', alignItems: 'start', gap: 12, margin: '16px 0' }}>
          <label style={labelStyle}>
            <span>Kho nguồn</span>
            <select
              aria-label="Kho nguồn"
              required
              disabled={createInFlight}
              value={sourceId}
              style={{ marginTop: 0 }}
              onChange={(event) => {
                const nextSource = Number(event.target.value) || '';
                if (nextSource !== sourceId) setDestinationId('');
                setSourceId(nextSource);
              }}
            >
              <option value="">Chọn kho</option>
              {warehouses.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </label>
          <ArrowRight aria-hidden="true" style={{ marginTop: 28 }} />
          <label style={labelStyle}>
            <span>Kho đích</span>
            <select
              aria-label="Kho đích"
              required
              disabled={createInFlight}
              value={destinationId}
              style={{ marginTop: 0 }}
              onChange={(event) => setDestinationId(Number(event.target.value) || '')}
            >
              <option value="">Chọn kho</option>
              {warehouses.filter((w) => w.id !== sourceId).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </label>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 10, margin: '10px 0' }}>
          <label style={labelStyle}>
            <span>Số Lệnh điều động nội bộ (PXKKVCNB - NĐ 70/2025/NĐ-CP)</span>
            <input
              aria-label="Số Lệnh điều động nội bộ (PXKKVCNB - NĐ 70/2025/NĐ-CP)"
              data-testid="input-transfer-internal-order-no"
              maxLength={100}
              disabled={createInFlight}
              placeholder="VD: LDD-2026-001"
              value={orderNoVal}
              onChange={(e) => setOrderNoVal(e.target.value)}
            />
          </label>
          <label style={labelStyle}>
            <span>Họ tên / Đơn vị vận chuyển</span>
            <input
              aria-label="Họ tên / Đơn vị vận chuyển"
              data-testid="input-transfer-transporter-name"
              maxLength={150}
              disabled={createInFlight}
              placeholder="VD: Đội xe nội bộ / GHN"
              value={transporterVal}
              onChange={(e) => setTransporterVal(e.target.value)}
            />
          </label>
          <label style={labelStyle}>
            <span>Biển kiểm soát phương tiện</span>
            <input
              aria-label="Biển kiểm soát phương tiện"
              data-testid="input-transfer-vehicle-plate"
              maxLength={50}
              disabled={createInFlight}
              placeholder="VD: 51C-889.99"
              value={plateVal}
              onChange={(e) => setPlateVal(e.target.value)}
            />
          </label>
        </div>

        <label style={{ ...labelStyle, margin: '12px 0' }}>
          <span>Ghi chú</span>
          <textarea aria-label="Ghi chú điều chuyển" maxLength={500} disabled={createInFlight} style={{ marginTop: 0 }} value={note} onChange={(event) => setNote(event.target.value)} />
        </label>

        <div className="line-header">
          <h3>Sản phẩm</h3>
          <button type="button" disabled={createInFlight} onClick={() => setLines([...lines, { productId: '', quantity: '', note: '' }])}>
            <Plus size={16} aria-hidden="true" /> Thêm dòng
          </button>
        </div>

        {lines.map((line, index) => (
          <div className="transfer-line" key={index}>
            <select
              aria-label={`Sản phẩm dòng ${index + 1}`}
              required
              disabled={createInFlight}
              value={line.productId}
              onChange={(event) => setLines(lines.map((entry, row) => (row === index ? { ...entry, productId: Number(event.target.value) || '' } : entry)))}
            >
              <option value="">Chọn sản phẩm</option>
              {products.map((product) => <option key={product.id} value={product.id}>{product.code} - {product.name}</option>)}
            </select>
            <input
              aria-label={`Số lượng dòng ${index + 1}`}
              required
              inputMode="decimal"
              role="spinbutton"
              min="0.0001"
              step="0.0001"
              disabled={createInFlight}
              placeholder="Số lượng"
              value={line.quantity}
              onChange={(event) => setLines(lines.map((entry, row) => (row === index ? { ...entry, quantity: event.target.value === '' ? '' : Number(event.target.value) } : entry)))}
            />
            <input
              aria-label={`Ghi chú dòng ${index + 1}`}
              placeholder="Ghi chú dòng (tùy chọn)"
              maxLength={500}
              disabled={createInFlight}
              value={line.note}
              onChange={(event) => setLines(lines.map((entry, row) => (row === index ? { ...entry, note: event.target.value } : entry)))}
            />
            <button type="button" aria-label={`Xóa dòng ${index + 1}`} disabled={createInFlight} onClick={() => setLines(lines.filter((_, rowIndex) => rowIndex !== index))}>
              <X size={17} aria-hidden="true" />
            </button>
          </div>
        ))}

        <div className="dialog-actions">
          <button type="button" disabled={createInFlight} onClick={onCloseDraftEditor}>Đóng</button>
          <button className="ui-primary-button" type="submit" disabled={createInFlight || (editingId !== null && draftConflict)}>
            {createInFlight ? (editingId === null ? 'Đang tạo phiếu...' : 'Đang lưu thay đổi...') : (editingId === null ? 'Tạo phiếu' : 'Lưu thay đổi')}
          </button>
        </div>
      </form>
    </div>
  );
}
export default StockTransferDraftModal;
