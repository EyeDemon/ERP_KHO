import { useEffect, useRef } from 'react';
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
  const closeRef = useRef<HTMLButtonElement>(null);
  useEffect(() => { closeRef.current?.focus(); }, []);
  const partner = kind === 'import'
    ? receipt.supplierCode && receipt.supplierName ? `${receipt.supplierCode} - ${receipt.supplierName}` : 'Chưa ghi nhận'
    : receipt.customerCode && receipt.customerName ? `${receipt.customerCode} - ${receipt.customerName}` : 'Chưa ghi nhận';
  const statusBanner = receipt.status === 'Draft' ? 'BẢN NHÁP — CHƯA DUYỆT' : receipt.status === 'Cancelled' ? 'ĐÃ HỦY' : receipt.status;
  return <div className="receipt-print-overlay" role="dialog" aria-modal="true" aria-label={`Bản in ${receipt.code}`} onKeyDown={e => { if (e.key === 'Escape') onClose(); }}>
    <div className="receipt-print-actions">
      <span>Preview dùng dữ liệu đã lưu từ backend.</span>
      <button onClick={() => window.print()}>In / Save as PDF</button>
      <button ref={closeRef} onClick={onClose}>Đóng</button>
    </div>
    <article className="receipt-print-sheet">
      <header><h1>{kind === 'import' ? 'PHIẾU NHẬP KHO' : 'PHIẾU XUẤT KHO'}</h1><div className="receipt-status">{statusBanner}</div></header>
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
      <table><thead><tr><th>STT</th><th>Mã hàng</th><th>Tên hàng</th><th>Đơn vị</th><th>Số lượng</th></tr></thead>
        <tbody>{receipt.details.map((d, index) => <tr key={d.id}><td>{index + 1}</td><td>{d.productCode || '—'}</td><td>{d.productName || 'Chưa ghi nhận'}</td><td>{d.unitName || 'Chưa ghi nhận'}</td><td className="receipt-number">{quantity(d.quantity)}</td></tr>)}</tbody>
      </table>
      <p><strong>Số dòng hàng:</strong> {receipt.details.length}</p>
      {receipt.note && <section className="receipt-note"><strong>Ghi chú:</strong><div>{receipt.note}</div></section>}
      <footer>Dữ liệu được tải lúc {dateTime(fetchedAt.toISOString())}. Thời gian hiển thị theo múi giờ của trình duyệt.</footer>
    </article>
  </div>;
}
