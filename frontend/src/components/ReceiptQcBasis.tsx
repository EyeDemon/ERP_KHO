import { useId, useState } from 'react';
import './ReceiptQcBasis.css';

interface Props {
  requiresQc: boolean;
  qcPolicySource?: string | null;
  qcPolicyVersion?: number | null;
  qcPolicyEffectiveAtUtc?: string | null;
}

const sources: Record<string, string> = {
  None: 'Không có chính sách được chọn khi tạo phiếu',
  Product: 'Chính sách theo sản phẩm',
  ProductSupplier: 'Chính sách theo sản phẩm và nhà cung cấp',
  DiscrepancyRouteToQc: 'Chuyển sang kiểm tra chất lượng do sai lệch nhận hàng',
};

function effectiveTime(value: string) {
  if (/(?:Z|[+-]\d{2}:\d{2})$/i.test(value)) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? 'Chưa có thông tin thời điểm hiệu lực hợp lệ'
      : `${date.toLocaleString('vi-VN', { timeZone: 'UTC' })} (UTC)`;
  }
  // Preserve the saved wall-clock fields; never assume a zone for a zone-less response.
  const parts = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}:\d{2}:\d{2})(?:\.\d+)?$/.exec(value);
  return parts ? `${parts[3]}/${parts[2]}/${parts[1]} ${parts[4]} (chưa có thông tin múi giờ trong dữ liệu trả về)`
    : 'Chưa có thông tin thời điểm hiệu lực hợp lệ';
}

export default function ReceiptQcBasis({ requiresQc, qcPolicySource, qcPolicyVersion, qcPolicyEffectiveAtUtc }: Props) {
  const [expanded, setExpanded] = useState(false);
  const id = useId();
  const hasBasis = Boolean(qcPolicySource || qcPolicyVersion || qcPolicyEffectiveAtUtc);
  return <section className="receipt-qc-basis" aria-label="Căn cứ kiểm tra chất lượng đã lưu">
    <button type="button" aria-expanded={expanded} aria-controls={id} onClick={() => setExpanded(value => !value)}>
      {expanded ? 'Thu gọn căn cứ kiểm tra chất lượng' : 'Xem căn cứ kiểm tra chất lượng'}
    </button>
    <div id={id} hidden={!expanded}>
      {!hasBasis ? <p>Chưa có thông tin căn cứ đã lưu</p> : <>
        <dl>
          <dt>Yêu cầu kiểm tra chất lượng</dt><dd>{requiresQc ? 'Yêu cầu kiểm tra chất lượng' : 'Không yêu cầu kiểm tra chất lượng'}</dd>
          <dt>Nguồn căn cứ</dt><dd>{Object.hasOwn(sources, qcPolicySource || '') ? sources[qcPolicySource!] : 'Chưa có thông tin nguồn căn cứ đã lưu'}</dd>
          <dt>Phiên bản chính sách</dt><dd>{qcPolicyVersion && qcPolicyVersion > 0 ? `Phiên bản ${qcPolicyVersion}` : 'Chưa có thông tin phiên bản đã lưu'}</dd>
          <dt>Thời điểm bắt đầu hiệu lực đã lưu</dt><dd>{qcPolicyEffectiveAtUtc ? effectiveTime(qcPolicyEffectiveAtUtc) : 'Chưa có thông tin thời điểm hiệu lực đã lưu'}</dd>
        </dl>
        <p>Căn cứ đã lưu trên dòng phiếu, không lấy lại từ chính sách hiện tại.</p>
      </>}
    </div>
  </section>;
}
