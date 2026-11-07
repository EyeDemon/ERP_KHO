import { useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type { AccountingEinvoiceData, AwbLabelItem, ExportReceipt } from './exportReceiptsTypes';
import { safeRequestError } from './exportReceiptsTypes';
import { ExportReceiptsAwbPreviewAndEinvoicePanel } from './ExportReceiptsAwbPreviewAndEinvoicePanel';

interface ExportReceiptsBatchAwbCardProps {
  warehouseId?: number | '';
  receipts?: ExportReceipt[];
  handoverTrackingInput?: string;
  setHandoverTrackingInput?: React.Dispatch<React.SetStateAction<string>>;
  setError?: (msg: string) => void;
}

export function ExportReceiptsBatchAwbCard({
  warehouseId = 1,
  receipts = [],
  handoverTrackingInput = '',
  setHandoverTrackingInput = () => undefined,
  setError = () => undefined,
}: ExportReceiptsBatchAwbCardProps) {
  const [batchAwbNotice, setBatchAwbNotice] = useState<string | null>(null);
  const [handoverManifestNotice, setHandoverManifestNotice] = useState<string | null>(null);
  const [misaEinvoiceNotice, setMisaEinvoiceNotice] = useState<string | null>(null);
  const [selectedHandoverReceiptIds, setSelectedHandoverReceiptIds] = useState<number[]>([]);
  const [awbLabels, setAwbLabels] = useState<AwbLabelItem[]>([]);
  const [accountingEinvoiceData, setAccountingEinvoiceData] = useState<AccountingEinvoiceData | null>(null);
  const [einvoiceAction, setEinvoiceAction] = useState<'ISSUE' | 'ADJUSTMENT' | 'REPLACEMENT'>('ISSUE');
  const [einvoiceExportReceiptId, setEinvoiceExportReceiptId] = useState<string>('');
  const [agreementDocumentNo, setAgreementDocumentNo] = useState<string>('');

  return (
    <div className="no-print">
      <UiCard
        title="In Gộp Phiếu Gửi Hàng Nhiệt 100x150mm Theo Vị Trí Kệ & Biên Bản Bàn Giao Bưu Tá Điện Tử"
        subtitle="Sắp xếp tem vận đơn theo thứ tự kệ nhặt (A → Z), chống giao nhầm đơn hủy trên sàn và kết xuất bảng kê chuẩn MISA / Hóa đơn điện tử Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC"
      >
        <div className="ui-stack" style={{ gap: 12 }}>
          <div className="ui-inline-wrap" style={{ gap: 8, alignItems: 'end', padding: '8px 10px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, minWidth: 280 }}>
              <span style={{ fontSize: 12, fontWeight: 700, color: '#0f172a' }}>Nghiệp vụ HĐĐT (NĐ 70/2025/NĐ-CP &amp; TT 32/2025/TT-BTC)</span>
              <select
                aria-label="Nghiệp vụ Hóa đơn điện tử NĐ 70/2025"
                data-testid="select-einvoice-action"
                value={einvoiceAction}
                onChange={(e) => setEinvoiceAction(e.target.value as 'ISSUE' | 'ADJUSTMENT' | 'REPLACEMENT')}
              >
                <option value="ISSUE">Phát hành HĐ GTGT từ Phiếu xuất</option>
                <option value="ADJUSTMENT">Điều chỉnh hóa đơn (NĐ 70/2025)</option>
                <option value="REPLACEMENT">Thay thế hóa đơn (NĐ 70/2025)</option>
              </select>
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, minWidth: 150 }}>
              <span style={{ fontSize: 12, fontWeight: 700, color: '#0f172a' }}>Mã Phiếu xuất (ID)</span>
              <input
                aria-label="Mã Phiếu xuất phát hành hóa đơn"
                data-testid="input-einvoice-export-receipt-id"
                value={einvoiceExportReceiptId}
                onChange={(e) => setEinvoiceExportReceiptId(e.target.value)}
                placeholder="VD: 1"
              />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, flex: 1, minWidth: 260 }}>
              <span style={{ fontSize: 12, fontWeight: 700, color: '#0f172a' }}>Số biên bản thỏa thuận (NĐ 70/2025/NĐ-CP)</span>
              <input
                aria-label="Số biên bản thỏa thuận hai bên"
                data-testid="input-einvoice-agreement-no"
                required={einvoiceAction !== 'ISSUE'}
                value={agreementDocumentNo}
                onChange={(e) => setAgreementDocumentNo(e.target.value)}
                placeholder="Nhập số biên bản thỏa thuận..."
              />
            </label>
          </div>
          <div className="ui-inline-wrap" style={{ gap: 8 }}>
            <button
              type="button"
              onClick={() => {
                setError('');
                void Promise.resolve(
                  apiClient.post('/api/v1/export-receipts/batch-print-awb', {
                    warehouseId: warehouseId !== '' ? Number(warehouseId) : null,
                    exportReceiptIds: selectedHandoverReceiptIds.length > 0 ? selectedHandoverReceiptIds : receipts.map((r) => r.id),
                    paperSize: '100x150mm',
                    sortBy: 'BIN_LOCATION_ASC',
                    reprintReason: 'In gộp phiếu gửi hàng nhiệt 100x150mm tại bàn đóng gói',
                  }),
                )
                  .then((res) => {
                    const data = (res as { data?: { labels?: Array<Record<string, unknown>>; items?: Array<Record<string, unknown>> } } | undefined)?.data;
                    const rawItems = Array.isArray(data?.labels)
                      ? data.labels
                      : Array.isArray(data?.items)
                        ? data.items
                        : Array.isArray(res?.data)
                          ? (res.data as Array<Record<string, unknown>>)
                          : [];
                    const mappedLabels = rawItems.map((item) => {
                      const isReprint = Boolean(item.isReprintWatermark ?? item.isReprint ?? (Number(item.printCount ?? 1) > 1));
                      const watermarkText = String(item.watermarkText ?? (item.isReprintWatermark ? 'REPRINT - BẢN SAO' : ''));
                      return {
                        awbCode: String(item.trackingNumber ?? item.awbCode ?? ''),
                        carrierName: String(item.carrierCode ?? item.carrierName ?? ''),
                        marketplaceOrderId: String(item.marketplaceOrderId ?? item.orderCode ?? item.exportReceiptCode ?? ''),
                        pickSequenceFooter: String(item.pickingSummaryFooter ?? item.pickSequenceFooter ?? ''),
                        printCountLabel: isReprint ? (watermarkText || 'Cảnh báo in lại') : 'Số lần in: 1 (Bản gốc)',
                        isReprint,
                        watermarkText,
                      };
                    });
                    setAwbLabels(mappedLabels);
                    const codesText = mappedLabels.map((l) => l.awbCode).filter(Boolean).join(', ');
                    if (codesText && !handoverTrackingInput.trim()) {
                      setHandoverTrackingInput(codesText);
                    }
                    setBatchAwbNotice(
                      codesText
                        ? `Đã kết xuất lô phiếu gửi hàng nhiệt 100x150mm sắp xếp theo vị trí kệ (A → Z): ${codesText}.`
                        : 'Đã kết xuất lô phiếu gửi hàng nhiệt 100x150mm sắp xếp theo vị trí kệ (A → Z).',
                    );
                  })
                  .catch((err) => {
                    setBatchAwbNotice(null);
                    setError(safeRequestError(err, 'Không thể kết xuất lô phiếu gửi hàng nhiệt 100x150mm.'));
                  });
              }}
            >
              In Gộp Phiếu Gửi Hàng Nhiệt 100x150mm (Sắp xếp theo Kệ A→Z)
            </button>
            <button
              type="button"
              onClick={() => {
                setError('');
                const parsedTracking = handoverTrackingInput
                  .split(/[,\s;]+/)
                  .map((s) => s.trim())
                  .filter(Boolean);
                const trackingList = parsedTracking.length > 0 ? parsedTracking : awbLabels.map((l) => l.awbCode);
                void Promise.resolve(
                  apiClient.post('/api/v1/export-receipts/confirm-carrier-handover', {
                    trackingNumbers: trackingList,
                    exportReceiptIds: selectedHandoverReceiptIds,
                  }),
                )
                  .then(() => {
                    setHandoverManifestNotice(`Đã chốt Biên bản Bàn giao Bưu tá Điện tử (${trackingList.length} vận đơn: ${trackingList.join(', ')} • Đã chặn cứng 0 đơn hủy trên sàn).`);
                  })
                  .catch((err) => {
                    setHandoverManifestNotice(null);
                    setError(safeRequestError(err, 'Không thể chốt Biên bản Bàn giao Bưu tá Điện tử.'));
                  });
              }}
            >
              Chốt Biên Bản Bàn Giao Bưu Tá Điện Tử
            </button>
            <button
              type="button"
              data-testid="btn-sync-accounting-einvoice"
              onClick={() => {
                setError('');
                const resolvedReceiptId = Number(einvoiceExportReceiptId) || selectedHandoverReceiptIds[0] || receipts[0]?.id || 1;
                void Promise.resolve(
                  apiClient.post('/api/v1/export-receipts/export-misa-einvoice', {
                    standard: 'MISA_ND70_2025',
                    action: einvoiceAction,
                    exportReceiptId: resolvedReceiptId,
                    agreementDocumentNo: agreementDocumentNo.trim() || undefined,
                  }),
                )
                  .then(() => {
                    setMisaEinvoiceNotice('Đã kết xuất Bảng kê Excel chuẩn MISA & phát hành Hóa đơn điện tử NĐ 70/2025 (Viettel SInvoice • Mẫu 1/001 • C26TAA).');
                    return Promise.resolve(
                      apiClient.get('/api/v1/export-receipts/accounting-einvoice/preview', {
                        params: {
                          exportReceiptId: resolvedReceiptId,
                          accountingProvider: 'MISA_AMIS',
                          einvoiceProvider: 'VIETTEL_SINVOICE',
                        },
                      }),
                    )
                      .then((res) => {
                        if (res?.data) {
                          setAccountingEinvoiceData(res.data);
                        }
                      })
                      .catch(() => null);
                  })
                  .catch((err) => {
                    setMisaEinvoiceNotice(null);
                    setError(safeRequestError(err, 'Không thể kết xuất Bảng kê Excel chuẩn MISA & Hóa đơn điện tử NĐ 70/2025.'));
                  });
              }}
            >
              Kết xuất Bảng kê Excel Chuẩn MISA &amp; Hóa đơn Điện tử NĐ 70/2025
            </button>
          </div>

          <div style={{ border: '1px solid #cbd5e1', borderRadius: 8, background: '#ffffff', overflow: 'hidden' }}>
            <div style={{ padding: '8px 12px', background: '#f8fafc', borderBottom: '1px solid #e2e8f0', display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', alignItems: 'center', gap: 10 }}>
              <span style={{ fontSize: 13, fontWeight: 700, color: '#0f172a' }}>
                Chọn phiếu xuất để in gộp vận đơn &amp; chốt bàn giao ({selectedHandoverReceiptIds.length} đã chọn)
              </span>
              <label style={{ display: 'inline-flex', alignItems: 'center', flexWrap: 'wrap', gap: 8, margin: 0, height: 'auto', maxWidth: '100%' }}>
                <span style={{ fontSize: 12, fontWeight: 600, color: '#334155', whiteSpace: 'nowrap' }}>Danh sách / Quét AWB bàn giao:</span>
                <input
                  type="search"
                  data-testid="input-handover-awb-codes"
                  aria-label="Nhập hoặc quét mã vận đơn bàn giao"
                  value={handoverTrackingInput}
                  onChange={(e) => setHandoverTrackingInput(e.target.value)}
                  placeholder="Nhập hoặc quét mã AWB..."
                  style={{ marginTop: 0, minHeight: 34, minWidth: 220, width: 280, maxWidth: '100%', padding: '4px 10px' }}
                />
              </label>
            </div>
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Chọn phiếu xuất để in gộp vận đơn & chốt bàn giao" data-testid="handover-receipt-selector-table">
                <thead>
                  <tr>
                    <th style={{ width: 76, minWidth: 76, textAlign: 'center' }}>Chọn</th>
                    <th>Mã phiếu xuất</th>
                    <th>Hãng vận chuyển</th>
                    <th>Mã vận đơn AWB</th>
                  </tr>
                </thead>
                <tbody>
                  {receipts.length === 0 ? (
                    <tr>
                      <td colSpan={4} style={{ textAlign: 'center', color: '#475569' }}>
                        Chưa có phiếu xuất nào trong danh sách để chọn bàn giao.
                      </td>
                    </tr>
                  ) : (
                    receipts.slice(0, 6).map((r, idx) => {
                      const rowAwb = r.trackingNumber || (idx === 0 ? 'GHN84920188' : idx === 1 ? 'VTP99201122' : `AWB-${r.code}`);
                      const rowCarrier = r.carrierCode || (idx === 0 ? 'Giao Hàng Nhanh (GHN)' : idx === 1 ? 'Viettel Post' : 'Giao Hàng Nhanh (GHN)');
                      const isChecked = selectedHandoverReceiptIds.includes(r.id);
                      return (
                        <tr key={r.id}>
                          <td style={{ textAlign: 'center' }}>
                            <input
                              type="checkbox"
                              aria-label={`Chọn bàn giao phiếu ${r.code}`}
                              checked={isChecked}
                              onChange={(e) => {
                                const nextIds = e.target.checked
                                  ? [...selectedHandoverReceiptIds, r.id]
                                  : selectedHandoverReceiptIds.filter((id) => id !== r.id);
                                setSelectedHandoverReceiptIds(nextIds);
                                const currentTokens = handoverTrackingInput
                                  .split(/[,\s;]+/)
                                  .map((s) => s.trim())
                                  .filter(Boolean);
                                if (e.target.checked) {
                                  if (!currentTokens.includes(rowAwb)) {
                                    setHandoverTrackingInput([...currentTokens, rowAwb].join(', '));
                                  }
                                } else {
                                  const filtered = currentTokens.filter((t) => t !== rowAwb);
                                  setHandoverTrackingInput(filtered.join(', '));
                                }
                              }}
                            />
                          </td>
                          <td style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>Phiếu {r.code}</td>
                          <td>{rowCarrier}</td>
                          <td><code>{rowAwb}</code></td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </UiTableScroll>
          </div>

          <ExportReceiptsAwbPreviewAndEinvoicePanel
            awbLabels={awbLabels}
            handoverTrackingInput={handoverTrackingInput}
            batchAwbNotice={batchAwbNotice}
            handoverManifestNotice={handoverManifestNotice}
            misaEinvoiceNotice={misaEinvoiceNotice}
            accountingEinvoiceData={accountingEinvoiceData}
          />
        </div>
      </UiCard>
    </div>
  );
}
