import { useEffect, useRef } from 'react';
import { BarcodeSvg } from './BarcodeSvg';
import './ReceiptPrintPreview.css';

export type PrintableReceipt = {
  code: string; status: string; note?: string | null; warehouseName?: string | null;
  createdAt: string; createdByName?: string | null; approvedAt?: string | null; approvedByName?: string | null;
  dispatchedAt?: string | null; dispatchedByName?: string | null;
  supplierCode?: string | null; supplierName?: string | null; customerCode?: string | null; customerName?: string | null;
  details: Array<{ id: number; productCode?: string | null; productName?: string | null; unitName?: string | null; quantity: number }>;
};

type Props = { kind: 'import' | 'export'; receipt: PrintableReceipt; fetchedAt: Date; onClose: () => void };

const dateTime = (value?: string | null) => value ? new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'medium' }).format(new Date(value)) : 'Chưa ghi nhận';
const quantity = (value: number) => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 4 }).format(value);

export default function ReceiptPrintPreview({ kind, receipt, fetchedAt, onClose }: Props) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const onCloseRef = useRef(onClose);
  useEffect(() => { onCloseRef.current = onClose; }, [onClose]);
  useEffect(() => {
    const previousActive = document.activeElement as HTMLElement | null;
    closeRef.current?.focus();
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        if (event.defaultPrevented) return;
        event.preventDefault();
        onCloseRef.current();
        return;
      }
      if (event.key !== 'Tab') return;
      const root = dialogRef.current;
      const items = Array.from(root?.querySelectorAll<HTMLElement>('button:not([disabled]),[tabindex]:not([tabindex="-1"])') ?? []);
      if (!items.length) return;
      const first = items[0];
      const last = items[items.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      previousActive?.focus();
    };
  }, []);
  const partner = kind === 'import'
    ? receipt.supplierCode && receipt.supplierName ? `${receipt.supplierCode} - ${receipt.supplierName}` : 'Chưa ghi nhận'
    : receipt.customerCode && receipt.customerName ? `${receipt.customerCode} - ${receipt.customerName}` : 'Chưa ghi nhận';
  const statusBanner = ({ Draft: 'BẢN NHÁP — CHƯA DUYỆT', Cancelled: 'ĐÃ HỦY', Approved: kind === 'export' ? 'Đã duyệt và giữ hàng' : 'Đã duyệt',
    Dispatched: 'Đã xuất kho', Received: 'Đã nhận — chưa ghi tồn', ReadyToPost: 'Sẵn sàng ghi nhận tồn kho',
    Posted: 'Đã ghi nhận tồn kho', QcPending: 'Chờ kết quả kiểm tra chất lượng', QcCompleted: 'Kiểm tra chất lượng đã hoàn tất — chờ duyệt',
    DiscrepancyPending: 'Chờ xử lý sai lệch', DiscrepancySubmitted: 'Đã gửi xử lý sai lệch', DiscrepancyPendingApproval: 'Chờ duyệt sai lệch',
    DiscrepancyResolved: 'Sai lệch đã xử lý', DiscrepancyRejected: 'Sai lệch bị trả lại', CancelPendingPutback: 'Chờ cất trả hàng hủy về kệ' } as Record<string, string>)[receipt.status] || 'Trạng thái chưa xác định';
  return <div ref={dialogRef} tabIndex={-1} className="receipt-print-overlay" role="dialog" aria-modal="true" aria-label={`Bản in ${receipt.code}`} onKeyDown={e => { if (e.key === 'Escape' && !e.defaultPrevented) { e.preventDefault(); onClose(); } }}>
    <div className="receipt-print-actions">
      <span>Bản in dùng dữ liệu đã lưu trên hệ thống.</span>
      <button type="button" onClick={() => window.print()}>In / Lưu thành PDF</button>
      <button type="button" ref={closeRef} onClick={onClose}>Đóng</button>
    </div>
    <article className="receipt-print-sheet">
      <header>
        <div>
          <h1>{kind === 'import' ? 'PHIẾU NHẬP KHO' : 'PHIẾU XUẤT KHO'}</h1>
          <div className="receipt-status">{statusBanner}</div>
        </div>
        <BarcodeSvg value={receipt.code} showText={false} height={38} />
      </header>
      <dl className="receipt-meta">
        <div><dt>Mã chứng từ</dt><dd>{receipt.code}</dd></div>
        <div><dt>Ngày chứng từ</dt><dd>{dateTime(receipt.createdAt)}</dd></div>
        <div><dt>Kho</dt><dd>{receipt.warehouseName || 'Chưa ghi nhận'}</dd></div>
        <div><dt>{kind === 'import' ? 'Nhà cung cấp' : 'Khách hàng'}</dt><dd>{partner}</dd></div>
        <div><dt>Người lập</dt><dd>{receipt.createdByName || 'Chưa ghi nhận'}</dd></div>
        <div><dt>Người duyệt</dt><dd>{receipt.approvedByName || 'Chưa ghi nhận'}</dd></div>
        <div><dt>Thời điểm duyệt</dt><dd>{dateTime(receipt.approvedAt)}</dd></div>
        {kind === 'export' && <><div><dt>Người xuất</dt><dd>{receipt.dispatchedByName || 'Chưa ghi nhận'}</dd></div><div><dt>Thời điểm xuất</dt><dd>{dateTime(receipt.dispatchedAt)}</dd></div></>}
      </dl>
      <div className="ui-table-scroll">
        <table><thead><tr><th>STT</th><th>Mã hàng</th><th>Tên hàng</th><th>Đơn vị</th><th>Số lượng</th></tr></thead>
          <tbody>{receipt.details.map((d, index) => <tr key={d.id}><td>{index + 1}</td><td>{d.productCode || '—'}</td><td>{d.productName || 'Chưa ghi nhận'}</td><td>{d.unitName || 'Chưa ghi nhận'}</td><td className="receipt-number">{quantity(d.quantity)}</td></tr>)}</tbody>
        </table>
      </div>
      <p><strong>Số dòng hàng:</strong> {receipt.details.length}</p>
      {receipt.note && <section className="receipt-note"><strong>Ghi chú:</strong><div>{receipt.note}</div></section>}
      <footer>Dữ liệu được tải lúc {dateTime(fetchedAt.toISOString())}. Thời gian hiển thị theo múi giờ của trình duyệt.</footer>
    </article>
  </div>;
}
