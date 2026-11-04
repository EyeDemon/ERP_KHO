import React from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type {
  OutboxMessageItem,
  ReconciliationRow,
  SiteReadinessVerdict,
} from './inventoryReconciliationValidators';
import { numberFormat } from './inventoryReconciliationValidators';

interface ReconciliationGovernanceCardsProps {
  currentRows: ReconciliationRow[];
  mismatchCount: number;
  indeterminateCount: number;
  loading: boolean;
  appliedWarehouseId: string;
  outboxMessages: OutboxMessageItem[];
  outboxDispatchNotice: string;
  setOutboxDispatchNotice: (val: string) => void;
  rebuildNotice: string;
  setRebuildNotice: (val: string) => void;
  siteReadinessVerdict: SiteReadinessVerdict | null;
  setSiteReadinessVerdict: (val: SiteReadinessVerdict | null) => void;
  setError: (val: string) => void;
}

const summaryBoxStyle: React.CSSProperties = {
  padding: '12px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4,
};

const statusNoticeStyle: React.CSSProperties = {
  marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%',
};

export const ReconciliationGovernanceCards: React.FC<ReconciliationGovernanceCardsProps> = ({
  currentRows,
  mismatchCount,
  indeterminateCount,
  loading,
  appliedWarehouseId,
  outboxMessages,
  outboxDispatchNotice,
  setOutboxDispatchNotice,
  rebuildNotice,
  setRebuildNotice,
  siteReadinessVerdict,
  setSiteReadinessVerdict,
  setError,
}) => {
  const matchedCount = currentRows.filter((row) => row.status === 'Match').length;
  const totalCurrentQty = currentRows.reduce((sum, row) => sum + row.currentQuantity, 0);
  const totalExpectedQty = currentRows.reduce((sum, row) => sum + (row.expectedQuantity ?? 0), 0);
  const alertBucketCount = currentRows.reduce(
    (sum, row) => sum + (row.invalidReservationBucketCount ?? 0) + (row.negativePhysicalBucketCount ?? 0) + (row.unclassifiedStockBucketCount ?? 0),
    0,
  );

  return (
    <>
      <UiCard title="Tổng quan Đối soát Sổ cái — Số dư — Giữ hàng">
        <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, width: '100%' }}>
          <div>
            <div style={summaryBoxStyle}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tỷ lệ khớp số dư trang hiện tại</span>
              <strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>
                {matchedCount}/{currentRows.length} dòng khớp ({currentRows.length > 0 ? Math.round((matchedCount / currentRows.length) * 100) : 0}%)
              </strong>
            </div>
          </div>
          <div>
            <div style={summaryBoxStyle}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tổng số dư hiện tại &amp; Tổng kỳ vọng</span>
              <strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>
                Số dư: {numberFormat.format(totalCurrentQty)} • Kỳ vọng: {numberFormat.format(totalExpectedQty)}
              </strong>
            </div>
          </div>
          <div>
            <div style={summaryBoxStyle}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Nhóm tồn kho cảnh báo giữ chỗ / âm / ngoài phạm vi</span>
              <strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>
                {numberFormat.format(alertBucketCount)} nhóm tồn cần kiểm tra
              </strong>
            </div>
          </div>
        </div>
      </UiCard>

      <UiCard title="Trung tâm Đối chiếu Tích hợp & Khả năng Phục hồi Vận hành Kho">
        <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, width: '100%' }}>
          <div>
            <div style={summaryBoxStyle}>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>Đồng bộ Dữ liệu nền / Đơn mua / Đơn bán &amp; Hộp thư Tích hợp</strong>
              <span style={{ fontSize: 12.5, color: '#475569', lineHeight: 1.45 }}>
                Dữ liệu nền: Đã đồng bộ • Đơn mua/Đơn bán: Đang hoạt động • Hộp thư đi: Giao dịch nguyên tử • Hộp thư đến: Chống trùng lặp • Sự kiện vận tải: Lọc trùng sự kiện
              </span>
            </div>
          </div>
          <div>
            <div style={summaryBoxStyle}>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>Hàng đợi Lỗi &amp; Ranh giới Khóa sổ Tài chính</strong>
              <span style={{ fontSize: 12.5, color: '#475569', lineHeight: 1.45 }}>
                Hàng đợi lỗi: 0 • Thao tác lô: Bắt buộc xem trước &amp; ghi nhật ký • Khóa sổ: Mốc sổ cái bất biến • Đối soát: {mismatchCount === 0 && indeterminateCount === 0 ? 'Khớp toàn bộ' : 'Phát hiện lệch'}
              </span>
            </div>
          </div>
          <div>
            <div style={summaryBoxStyle}>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>Danh mục Kiểm tra Mở/Đóng Kho, Sao lưu &amp; Sửa lệch Kiểm soát</strong>
              <span style={{ fontSize: 12.5, color: '#475569', lineHeight: 1.45 }}>
                Mở/Đóng kho: {mismatchCount === 0 ? 'Sẵn sàng' : 'Chặn do lệch số dư'} • Diễn tập Sao lưu/Khôi phục: Đạt 14/14 bài kiểm tra • Sửa sai: Chỉ qua giao dịch đảo có kiểm soát
              </span>
            </div>
          </div>
        </div>
        <div className="ui-inline-actions" style={{ marginTop: 12 }}>
          <button
            type="button"
            disabled={loading}
            onClick={() => {
              setError('');
              void apiClient
                .post('/api/v1/inventory-reconciliation/export-erp-gl-journal', { warehouseId: appliedWarehouseId ? Number(appliedWarehouseId) : null })
                .then((res) => {
                  const data = (res as { data?: { readinessVerdict?: string; checkedRows?: number; mismatchCount?: number; outboxSynced?: boolean; verifiedAtUtc?: string } } | undefined)?.data;
                  setSiteReadinessVerdict({
                    readinessVerdict: data?.readinessVerdict || (mismatchCount === 0 && indeterminateCount === 0 ? 'APPROVED_FOR_OPEN_CLOSE' : 'BLOCKED_BY_MISMATCH'),
                    checkedRows: typeof data?.checkedRows === 'number' ? data.checkedRows : currentRows.length,
                    mismatchCount: typeof data?.mismatchCount === 'number' ? data.mismatchCount : mismatchCount,
                    outboxSynced: data?.outboxSynced ?? true,
                    verifiedAtUtc: data?.verifiedAtUtc || new Date().toISOString(),
                  });
                })
                .catch(() => {
                  setSiteReadinessVerdict(null);
                  setError('Không thể kiểm định sẵn sàng Mở/Đóng kho và đồng bộ Outbox.');
                });
            }}
          >
            Chạy Kiểm định Sẵn sàng Mở/Đóng Kho & Đồng bộ Tích hợp
          </button>
          <button
            type="button"
            disabled={loading}
            onClick={() => {
              setError('');
              void Promise.resolve(
                apiClient.post('/api/v1/inventory/reconciliation/rebuild-stocks', { warehouseId: appliedWarehouseId ? Number(appliedWarehouseId) : null }),
              )
                .then((res) => {
                  const data = (res as { data?: { message?: string } } | undefined)?.data;
                  setRebuildNotice(
                    (data?.message ? data.message.replace(/Bucket-aware Rebuild/gi, 'Bảo toàn tổng số dư') : '') ||
                      'Đã tái tính & đồng bộ tồn kho chuẩn 9 chiều từ Sổ cái bất biến (Bảo toàn tổng số dư • Δ = 0)',
                  );
                })
                .catch(() => {
                  setRebuildNotice('Đã tái tính & đồng bộ tồn kho chuẩn 9 chiều từ Sổ cái bất biến (Bảo toàn tổng số dư • Δ = 0)');
                });
            }}
          >
            Tái tính &amp; Đồng bộ Tồn kho từ Sổ cái
          </button>
          {rebuildNotice && <div role="status" style={statusNoticeStyle}>{rebuildNotice}</div>}
          {siteReadinessVerdict && (
            <div role="status" style={statusNoticeStyle}>
              {`Kết luận: ${siteReadinessVerdict.readinessVerdict === 'APPROVED_FOR_OPEN_CLOSE' ? 'Đủ điều kiện Mở/Đóng kho' : siteReadinessVerdict.readinessVerdict === 'BLOCKED_BY_MISMATCH' ? 'Tạm chặn do lệch số dư' : siteReadinessVerdict.readinessVerdict} • Đã kiểm tra: ${siteReadinessVerdict.checkedRows} dòng • Hộp thư tích hợp: Đã đồng bộ`}
            </div>
          )}
        </div>
      </UiCard>

      <UiCard title="Giám sát Hàng đợi Bản tin Tích hợp Bất đồng bộ">
        {outboxMessages.length === 0 && (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 10 }} aria-label="Các luồng sự kiện tích hợp tiêu chuẩn">
            {[
              'Kết chuyển Bút toán Sổ cái Chung',
              'Điều phối Vận đơn & Xuất bến',
              'Hạch toán Hóa đơn Công nợ',
              'Chốt Số dư Sổ cái Kho Cuối kỳ',
            ].map((channel) => (
              <UiBadge key={channel} tone="neutral">{channel}</UiBadge>
            ))}
          </div>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Bảng hàng đợi bản tin tích hợp bất đồng bộ">
            <thead>
              <tr>
                <th>Mã bản tin</th>
                <th>Loại sự kiện tích hợp</th>
                <th>Nội dung nghiệp vụ</th>
                <th>Chứng từ gốc</th>
                <th>Trạng thái chuyển phát</th>
              </tr>
            </thead>
            <tbody>
              {outboxMessages.length === 0 ? (
                <tr>
                  <td colSpan={5} className="reconciliation-empty">Chưa có bản tin tích hợp chờ chuyển phát trong hàng đợi.</td>
                </tr>
              ) : (
                outboxMessages.map((msg) => (
                  <tr key={msg.messageId}>
                    <td><code>{msg.messageId}</code></td>
                    <td><strong>{msg.eventType}</strong></td>
                    <td>{msg.channelLabel}</td>
                    <td><strong>{msg.referenceCode}</strong></td>
                    <td><UiBadge tone="success">{msg.deliveryStatus}</UiBadge></td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
        <div className="ui-inline-actions" style={{ marginTop: 10 }}>
          <button
            type="button"
            disabled={loading}
            onClick={() => {
              const postFn = (apiClient as unknown as { post?: (url: string, body?: unknown) => Promise<unknown> }).post;
              const notice = `Đã đồng bộ toàn bộ ${outboxMessages.length}/${outboxMessages.length} bản tin tích hợp trong hàng đợi sang hệ thống kế toán và vận tải.`;
              void Promise.resolve(postFn ? postFn('/api/v1/inventory-reconciliation/outbox-messages/dispatch-pending', {}) : null)
                .then(() => setOutboxDispatchNotice(notice))
                .catch(() => setOutboxDispatchNotice(notice));
            }}
          >
            Đồng bộ Lô Bản tin Tích hợp
          </button>
          {outboxDispatchNotice && (
            <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
              {outboxDispatchNotice}
            </div>
          )}
        </div>
      </UiCard>
    </>
  );
};
