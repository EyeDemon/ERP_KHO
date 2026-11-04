import { useState } from 'react';
import apiClient from '../../services/apiClient';
import { permissionError } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  dateTime,
  labels,
  normalizeUnitCode,
  tone,
  translateStagingSignal,
  type AsnDetail,
  type AsnList,
} from './AsnComponents';

export function AsnDetailAndStagingCards({
  items,
  selected,
  warehouseId,
  canConfirm,
  canUpdate,
  canReceive,
  canCancel,
  busy,
  creating,
  arrivalSummary,
  command,
  setError,
}: {
  items: AsnList[];
  selected: AsnDetail | null;
  warehouseId: number | '';
  canConfirm: boolean;
  canUpdate: boolean;
  canReceive: boolean;
  canCancel: boolean;
  busy: string;
  creating: boolean;
  arrivalSummary: {
    overdueArrivalCount: number;
    linkedPoAsnCount: number;
    unlinkedPoAsnCount: number;
    inboundExpectedVolume: number;
  };
  command: (path: string, action: string, successMessage: string) => Promise<void>;
  setError: (msg: string) => void;
}) {
  const [stagingBusy, setStagingBusy] = useState(false);
  const [stagingReservation, setStagingReservation] = useState<{ stagingReservationId: string; stagingLaneCode: string } | null>(null);

  const preAllocateStaging = async () => {
    if (stagingBusy) return;
    setStagingBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/v1/asns/pre-allocate-staging', {
        warehouseId: warehouseId ? Number(warehouseId) : (items[0]?.warehouseId ?? undefined),
        asnNumber: selected?.code || items[0]?.code || 'ASN-NEXT-ARRIVAL',
      });
      if (res?.data?.stagingReservationId && res?.data?.stagingLaneCode) {
        setStagingReservation({
          stagingReservationId: String(res.data.stagingReservationId),
          stagingLaneCode: translateStagingSignal(String(res.data.stagingLaneCode)),
        });
      } else {
        setStagingReservation(null);
        setError('Phản hồi cấp phát bãi đệm tiếp nhận không hợp lệ từ máy chủ.');
      }
    } catch (err: unknown) {
      setStagingReservation(null);
      setError(permissionError(err, 'Không thể cấp phát trước bãi đệm tiếp nhận.'));
    } finally {
      setStagingBusy(false);
    }
  };

  return (
    <div className="ui-stack">
      {selected && (
        <UiCard title={'Chi tiết ' + selected.code}>
          <div className="inbound-planning-detail-meta">
            <div><strong>Trạng thái</strong><br /><UiBadge tone={tone(selected.status)}><span style={{ whiteSpace: 'nowrap' }}>{labels[selected.status] ?? selected.status}</span></UiBadge></div>
            <div><strong>Đơn mua (PO)</strong><br />{selected.purchaseOrderCode || 'Không gắn đơn mua'}</div>
            <div><strong>Nhà cung cấp</strong><br />{selected.supplierCode} — {selected.supplierName}</div>
            <div><strong>Kho nhận</strong><br />{selected.warehouseName}</div>
            <div><strong>Dự kiến đến</strong><br />{dateTime(selected.expectedArrivalAtUtc)}</div>
            <div><strong>Đơn vị vận chuyển</strong><br />{selected.carrierName || '—'}</div>
            <div><strong>Biển số xe</strong><br />{selected.vehiclePlate || '—'}</div>
            <div><strong>Tổng SL cơ sở</strong><br />{selected.baseExpectedQuantity.toLocaleString('vi-VN')} THÙNG</div>
          </div>
          {selected.note && <p><strong>Ghi chú:</strong> {selected.note}</p>}
          <div className="inbound-planning-actions">
            {canConfirm && selected.status === 'Draft' && <button className="ui-primary-button" style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8' }} disabled={!!busy || !selected.rowVersion} onClick={() => void command('/api/v1/asns/' + selected.id + '/confirm', 'asn-confirm:' + selected.id, 'ASN đã được xác nhận.')}>Xác nhận ASN</button>}
            {canUpdate && selected.status === 'Confirmed' && <button style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8' }} disabled={!!busy || !selected.rowVersion} onClick={() => void command('/api/v1/asns/' + selected.id + '/mark-in-transit', 'asn-transit:' + selected.id, 'ASN đã chuyển sang trạng thái đang vận chuyển.')}>Đánh dấu đang vận chuyển</button>}
            {canReceive && selected.status === 'InTransit' && <button style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8' }} disabled={!!busy || !selected.rowVersion} onClick={() => void command('/api/v1/asns/' + selected.id + '/arrive', 'asn-arrive:' + selected.id, 'Đã ghi nhận ASN đến kho.')}>Ghi nhận đến kho</button>}
            {canReceive && selected.status === 'Arrived' && <button style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8' }} disabled={!!busy || !selected.rowVersion} onClick={() => void command('/api/v1/asns/' + selected.id + '/start-receiving', 'asn-receiving:' + selected.id, 'Đã bắt đầu tiếp nhận ASN.')}>Bắt đầu tiếp nhận</button>}
            {canReceive && selected.status === 'Receiving' && <button className="ui-primary-button" style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8' }} disabled={!!busy || !selected.rowVersion} onClick={() => void command('/api/v1/asns/' + selected.id + '/complete', 'asn-complete:' + selected.id, 'ASN đã hoàn tất. Tồn kho vẫn chưa thay đổi cho tới khi ghi sổ phiếu nhập.')}>Hoàn tất ASN</button>}
            {canCancel && ['Draft', 'Confirmed'].includes(selected.status) && <button style={{ border: '1px solid #dc2626', color: '#dc2626', background: '#fff5f5', fontWeight: 600 }} disabled={!!busy || !selected.rowVersion} onClick={() => window.confirm('Xác nhận hủy thông báo ASN này?') && void command('/api/v1/asns/' + selected.id + '/cancel', 'asn-cancel:' + selected.id, 'ASN đã được hủy.')}>Hủy ASN</button>}
          </div>
          <UiTableScroll>
            <table className="ui-table-compact" aria-label={'Dòng ASN ' + selected.code}>
              <thead><tr><th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Dòng</th><th>Sản phẩm</th><th>Đơn vị</th><th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL dự kiến</th><th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL cơ sở</th></tr></thead>
              <tbody>{selected.lines.map(line => {
                const opUnit = normalizeUnitCode(line.operationUnitCode);
                const baseUnit = normalizeUnitCode(line.baseUnitCode);
                return (
                  <tr key={line.id}>
                    <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.lineNo}</td>
                    <td><strong style={{ whiteSpace: 'nowrap' }}>{line.productCode}</strong><br /><small>{line.productName}</small></td>
                    <td>{opUnit || '—'}</td>
                    <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.expectedQuantity.toLocaleString('vi-VN')}</td>
                    <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.baseExpectedQuantity.toLocaleString('vi-VN')}{(line.baseUnitCode === 'QA_UNIT' || line.baseUnitCode === 'QA-PCS') && baseUnit === opUnit ? '' : ' ' + baseUnit}</td>
                  </tr>
                );
              })}</tbody>
            </table>
          </UiTableScroll>
        </UiCard>
      )}

      <UiCard title="Giám sát lịch đến kho" subtitle="Theo dõi ASN trễ giờ dự kiến, liên kết đơn mua nguồn và cấp phát trước bãi đệm tiếp nhận">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 12, marginBottom: 12 }}>
          <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
            <div style={{ fontSize: 12, color: '#475569', marginBottom: 4 }}>ASN trễ giờ dự kiến đến</div>
            <UiBadge tone={arrivalSummary.overdueArrivalCount > 0 ? 'danger' : 'success'}>
              {arrivalSummary.overdueArrivalCount} ASN trễ lịch
            </UiBadge>
          </div>
          <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
            <div style={{ fontSize: 12, color: '#475569', marginBottom: 4 }}>Liên kết đơn mua nguồn</div>
            <strong>{arrivalSummary.linkedPoAsnCount} gắn PO / {arrivalSummary.unlinkedPoAsnCount} độc lập</strong>
          </div>
          <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
            <div style={{ fontSize: 12, color: '#475569', marginBottom: 4 }}>Tổng SL cơ sở đang đến kho</div>
            <strong style={{ fontSize: 16, fontVariantNumeric: 'tabular-nums' }}>{arrivalSummary.inboundExpectedVolume.toLocaleString('vi-VN')} THÙNG</strong>
          </div>
        </div>
        <div className="ui-inline-actions" style={{ marginTop: 8 }}>
          <button
            type="button"
            style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8' }}
            disabled={!!busy || creating || stagingBusy}
            onClick={() => void preAllocateStaging()}
          >
            Cấp phát Trước Bãi đệm Tiếp nhận
          </button>
        </div>
        {stagingReservation && (
          <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word' }}>
            {`Mã cấp phát: ${stagingReservation.stagingReservationId} • Vị trí đệm: ${stagingReservation.stagingLaneCode}`}
          </div>
        )}
      </UiCard>
    </div>
  );
}
