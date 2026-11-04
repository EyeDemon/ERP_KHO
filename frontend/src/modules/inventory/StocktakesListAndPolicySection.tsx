import React from 'react';
import { UiBadge, UiCard, UiTableScroll, UiTabs, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import type { StocktakeAutoApprovalItem, StocktakeResponse, StocktakeSummary } from './stocktakesShared';
import { getStatusText, inactiveTabPanelStyle, normalizeStatusCode, statusTone } from './stocktakesShared';

interface StocktakesListAndPolicySectionProps {
  stocktakes: StocktakeSummary[]; filteredStocktakes: StocktakeSummary[]; paginatedStocktakes: StocktakeSummary[];
  loading: boolean; searchTerm: string; onSearchChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  statusFilter: string; onStatusFilterChange: (e: React.ChangeEvent<HTMLSelectElement>) => void;
  currentPage: number; totalPages: number; setCurrentPage: React.Dispatch<React.SetStateAction<number>>;
  onLoadDetail: (id: number) => void; stocktakeSectionTab: 'list' | 'policy';
  setStocktakeSectionTab: React.Dispatch<React.SetStateAction<'list' | 'policy'>>;
  policyExpanded: boolean; setPolicyExpanded: React.Dispatch<React.SetStateAction<boolean>>;
  autoApprovalRows: StocktakeAutoApprovalItem[]; autoApprovalBusy: boolean; autoApprovalBanner: string;
  onEvaluateAutoApproval: () => void; onReloadAutoApproval: () => void; currentDetail: StocktakeResponse | null;
  proposalProductCode: string; setProposalProductCode: (val: string) => void;
  proposalLocationCode: string; setProposalLocationCode: (val: string) => void;
  proposalDeltaQty: number; setProposalDeltaQty: (val: number) => void;
  proposalReasonCode: string; setProposalReasonCode: (val: string) => void;
  proposalBusy: boolean; proposalStatus: string; onQuickAdjustmentProposal: () => void;
}

const kpiBoxStyle: React.CSSProperties = {
  padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#ffffff',
  display: 'flex', justifyContent: 'space-between', alignItems: 'center',
};
const rightNumStyle: React.CSSProperties = { textAlign: 'right', fontVariantNumeric: 'tabular-nums' };
const policyBoxStyle: React.CSSProperties = {
  padding: '12px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', height: '100%', boxSizing: 'border-box',
};

export const StocktakesListAndPolicySection: React.FC<StocktakesListAndPolicySectionProps> = ({
  stocktakes, filteredStocktakes, paginatedStocktakes, loading, searchTerm, onSearchChange,
  statusFilter, onStatusFilterChange, currentPage, totalPages, setCurrentPage, onLoadDetail,
  stocktakeSectionTab, setStocktakeSectionTab, policyExpanded, setPolicyExpanded,
  autoApprovalRows, autoApprovalBusy, autoApprovalBanner, onEvaluateAutoApproval, onReloadAutoApproval,
  currentDetail, proposalProductCode, setProposalProductCode, proposalLocationCode, setProposalLocationCode,
  proposalDeltaQty, setProposalDeltaQty, proposalReasonCode, setProposalReasonCode,
  proposalBusy, proposalStatus, onQuickAdjustmentProposal,
}) => (
  <>
    <div className="stocktake-kpi-row" style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 10, marginBottom: 10 }}>
      <div style={kpiBoxStyle}>
        <span style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>Tổng phiếu kiểm kê</span>
        <strong style={{ fontSize: 16, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{stocktakes.length}</strong>
      </div>
      <div style={kpiBoxStyle}>
        <span style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>Đang đếm / Chờ duyệt</span>
        <strong style={{ fontSize: 16, color: '#b45309', fontVariantNumeric: 'tabular-nums' }}>
          {stocktakes.filter((s) => [0, 3, 5].includes(normalizeStatusCode(s.status))).length}
        </strong>
      </div>
      <div style={kpiBoxStyle}>
        <span style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>Hoàn tất / Đã duyệt</span>
        <strong style={{ fontSize: 16, color: '#065f46', fontVariantNumeric: 'tabular-nums' }}>
          {stocktakes.filter((s) => [1, 4, 6].includes(normalizeStatusCode(s.status))).length}
        </strong>
      </div>
      <div style={kpiBoxStyle}>
        <span style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>Ngưỡng tự duyệt</span>
        <UiBadge tone="success">&lt;1% &amp; ≤500k</UiBadge>
      </div>
    </div>

    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
      <UiTabs
        ariaLabel="Phân vùng kiểm kê và chính sách điều chỉnh"
        activeTab={stocktakeSectionTab}
        onChange={(tab) => { setStocktakeSectionTab(tab); if (tab === 'policy') setPolicyExpanded(true); }}
        tabs={[
          { id: 'list', label: 'Danh sách Phiếu kiểm kê & Đếm mù', testId: 'tab-stocktake-list' },
          { id: 'policy', label: 'Đề xuất Điều chỉnh & Chính sách Mã lý do', testId: 'tab-stocktake-policy' },
        ]}
      />
      {stocktakeSectionTab === 'policy' && (
        <button type="button" className="ui-btn-secondary" onClick={() => setPolicyExpanded((prev) => !prev)} style={{ padding: '6px 10px', fontSize: 12.5 }}>
          {policyExpanded ? 'Thu gọn công cụ điều chỉnh & đếm lại' : 'Mở công cụ điều chỉnh & đếm lại'}
        </button>
      )}
    </div>

    <div className="ui-stack" style={stocktakeSectionTab === 'list' ? undefined : inactiveTabPanelStyle}>
      <UiCard title="Danh sách phiếu kiểm kê">
        <UiToolbar>
          <UiToolbarField label="Tìm kiếm">
            <input aria-label="Tìm phiếu kiểm kê" type="text" placeholder="Tìm mã phiếu hoặc tên kho..." style={{ minWidth: 280 }} value={searchTerm} onChange={onSearchChange} />
          </UiToolbarField>
          <UiToolbarField label="Trạng thái">
            <select aria-label="Lọc trạng thái kiểm kê" value={statusFilter} onChange={onStatusFilterChange}>
              <option value="">Tất cả trạng thái</option>
              <option value="0">Nháp</option><option value="1">Đã duyệt</option><option value="2">Đã hủy</option>
              <option value="3">Đang kiểm đếm</option><option value="4">Hoàn tất đếm</option>
              <option value="5">Chờ phê duyệt</option><option value="6">Hoàn tất điều chỉnh</option><option value="7">Từ chối</option>
            </select>
          </UiToolbarField>
          <div className="ui-muted-text ui-auto-actions">Trang {currentPage}/{totalPages} • {filteredStocktakes.length} phiếu</div>
        </UiToolbar>

        {loading && stocktakes.length === 0 ? (
          <p role="status">Đang tải...</p>
        ) : (
          <UiTableScroll>
            <table className="ui-table-compact stocktake-list-table" aria-label="Danh sách phiếu kiểm kê">
              <thead>
                <tr>
                  <th>Mã phiếu</th><th>Kho</th><th>Ngày tạo</th><th>Trạng thái</th>
                  <th className="ui-cell-numeric" style={rightNumStyle}>SL SP</th><th>Ghi chú</th><th>Hành động</th>
                </tr>
              </thead>
              <tbody>
                {paginatedStocktakes.map((s) => (
                  <tr key={s.id}>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap' }}><strong style={{ whiteSpace: 'nowrap' }}>{s.code}</strong></td>
                    <td>{s.warehouseName}</td>
                    <td>{new Date(s.createdAt).toLocaleString('vi-VN')}</td>
                    <td><UiBadge tone={statusTone(s.status)}>{getStatusText(s.status)}</UiBadge></td>
                    <td className="ui-cell-numeric" style={rightNumStyle}>{s.detailCount}</td>
                    <td className="stocktake-note" title={s.note || ''}>{s.note || '—'}</td>
                    <td><button type="button" onClick={() => onLoadDetail(s.id)}>Xem chi tiết</button></td>
                  </tr>
                ))}
                {filteredStocktakes.length === 0 && (
                  <tr><td colSpan={7} className="ui-empty-cell">Không tìm thấy phiếu kiểm kê nào</td></tr>
                )}
              </tbody>
            </table>
          </UiTableScroll>
        )}

        {totalPages > 1 && (
          <div className="ui-pagination" aria-label="Phân trang kiểm kê">
            <button type="button" disabled={currentPage === 1} onClick={() => setCurrentPage(1)}>Đầu</button>
            <button type="button" disabled={currentPage === 1} onClick={() => setCurrentPage((p) => p - 1)}>Trước</button>
            <span>Trang {currentPage} / {totalPages}</span>
            <button type="button" disabled={currentPage === totalPages} onClick={() => setCurrentPage((p) => p + 1)}>Sau</button>
            <button type="button" disabled={currentPage === totalPages} onClick={() => setCurrentPage(totalPages)}>Cuối</button>
          </div>
        )}
      </UiCard>
    </div>

    {policyExpanded && (
      <div className="ui-stack" style={stocktakeSectionTab === 'policy' ? undefined : inactiveTabPanelStyle}>
        <UiCard
          title="Đánh giá Tự động Duyệt Chênh lệch Nhỏ (< 1% & ≤ 500.000đ) & Lịch sử Đếm lại (Lần 1 / Lần 2)"
          subtitle="Quy tắc ngưỡng tự động duyệt: |Chênh lệch %| < 1% VÀ |Giá trị lệch| ≤ 500.000 ₫ — Vượt ngưỡng bắt buộc chuyển Giám đốc kho phê duyệt"
        >
          <div className="ui-inline-wrap" style={{ justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
            <div className="ui-inline-wrap" style={{ gap: 8 }}>
              <UiBadge tone="success">Tự động duyệt (&lt;1% &amp; ≤500k)</UiBadge>
              <UiBadge tone="warning">Cần Giám đốc kho duyệt</UiBadge>
            </div>
            <div className="ui-inline-wrap" style={{ gap: 8 }}>
              <button type="button" data-testid="btn-evaluate-stocktake-auto-approval" disabled={autoApprovalBusy} onClick={onEvaluateAutoApproval}>
                {autoApprovalBusy ? 'Đang đánh giá...' : 'Chạy Đánh Giá Tự Động Duyệt (< 1% & ≤ 500.000đ)'}
              </button>
              <button type="button" className="ui-btn-secondary" disabled={autoApprovalBusy} onClick={onReloadAutoApproval}>
                Tải lại Lịch sử Đếm Lần 1 / Lần 2
              </button>
            </div>
          </div>

          {autoApprovalBanner && (
            <div role="status" style={{ marginBottom: 10, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, fontWeight: 600 }}>
              {autoApprovalBanner}
            </div>
          )}

          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Bảng đối chiếu lịch sử đếm lại và đánh giá tự động duyệt" style={{ width: '100%' }}>
              <thead>
                <tr>
                  <th>Mã &amp; Tên sản phẩm</th>
                  <th style={rightNumStyle}>Tồn hệ thống (Ẩn khi đếm mù)</th>
                  <th style={rightNumStyle}>Đếm Lần 1</th>
                  <th style={rightNumStyle}>Đếm Lại Lần 2</th>
                  <th style={rightNumStyle}>Chênh lệch (%)</th>
                  <th style={rightNumStyle}>Giá trị lệch (VNĐ)</th>
                  <th>Phân loại phê duyệt</th>
                </tr>
              </thead>
              <tbody>
                {autoApprovalRows.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="ui-empty-cell">
                      Nhấn &ldquo;Chạy Đánh Giá Tự Động Duyệt (&lt; 1% &amp; ≤ 500.000đ)&rdquo; hoặc chọn phiếu kiểm kê để xem chi tiết Đếm Lần 1 / Đếm Lại Lần 2.
                    </td>
                  </tr>
                ) : (
                  autoApprovalRows.map((item) => (
                    <tr key={item.id}>
                      <td><strong>{item.productCode}</strong> — {item.productName}</td>
                      <td style={rightNumStyle}>{item.isBlindCount ? <span className="ui-muted-text">Ẩn khi đếm mù</span> : item.systemQuantity.toLocaleString('vi-VN')}</td>
                      <td style={rightNumStyle}>{item.attempt1Quantity !== null ? item.attempt1Quantity.toLocaleString('vi-VN') : '—'}</td>
                      <td style={rightNumStyle}>{item.attempt2Quantity !== null ? item.attempt2Quantity.toLocaleString('vi-VN') : '—'}</td>
                      <td style={rightNumStyle}>{`${item.variancePct.toLocaleString('vi-VN', { maximumFractionDigits: 2 })}%`}</td>
                      <td style={rightNumStyle}>{`${Math.round(item.varianceValueVnd).toLocaleString('vi-VN')} ₫`}</td>
                      <td>
                        <UiBadge tone={item.autoApproved ? 'success' : 'warning'}>
                          {item.autoApproved ? 'Tự động duyệt (<1% & ≤500k)' : 'Cần Giám đốc kho duyệt'}
                        </UiBadge>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </UiTableScroll>
        </UiCard>

        <UiCard title="Danh mục Mã lý do Chuẩn & Chính sách Điều chỉnh Tồn kho có Phê duyệt Độc lập">
          <div className="stocktake-detail-meta" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, width: '100%' }}>
            <div><div style={policyBoxStyle}><strong>Mã lý do chuẩn hóa toàn hệ thống:</strong><br />Chênh lệch kiểm đếm • Hư hỏng vật lý • Thất lạc / Sai vị trí • Thiếu từ nhà cung cấp • Hủy hết hạn • Cô lập thu hồi</div></div>
            <div><div style={policyBoxStyle}><strong>Loại bút toán điều chỉnh sổ cái:</strong><br />Điều chỉnh tăng &amp; Điều chỉnh giảm</div></div>
            <div><div style={policyBoxStyle}><strong>Kiểm soát phân tách nhiệm vụ:</strong><br />Người tạo phiếu kiểm kê không được tự phê duyệt điều chỉnh tồn kho (Ngưỡng cảnh báo lệch chuẩn: ±2.0%)</div></div>
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 10, marginTop: 10 }}>
            <label className="ui-stack">
              <span>Sản phẩm điều chỉnh</span>
              <input aria-label="Sản phẩm điều chỉnh" type="text" list="stocktake-proposal-products" placeholder="VD: QA-E2E-001" value={proposalProductCode} onChange={(e) => setProposalProductCode(e.target.value)} />
              <datalist id="stocktake-proposal-products">
                <option value="QA-E2E-001">QA-E2E-001 — Thùng linh kiện điện tử QA-01</option>
                <option value="QA-LOT-002">QA-LOT-002 — Hộp cảm biến nhiệt độ QA-02</option>
                {(currentDetail?.details ?? [])
                  .filter((d) => d.productCode !== 'QA-E2E-001' && d.productCode !== 'QA-LOT-002')
                  .map((d) => <option key={d.productCode} value={d.productCode}>{d.productCode} — {d.productName}</option>)}
              </datalist>
            </label>
            <label className="ui-stack">
              <span>Vị trí kệ điều chỉnh</span>
              <input aria-label="Vị trí kệ đề xuất điều chỉnh" type="text" list="stocktake-proposal-locations" placeholder="VD: BIN-A01-01" value={proposalLocationCode} onChange={(e) => setProposalLocationCode(e.target.value)} />
              <datalist id="stocktake-proposal-locations">
                <option value="BIN-A01-01">BIN-A01-01 — Kệ A01 Tầng 1</option>
                <option value="BIN-A01-02">BIN-A01-02 — Kệ A01 Tầng 2</option>
                <option value="BIN-B02-01">BIN-B02-01 — Kệ B02 Tầng 1</option>
              </datalist>
            </label>
            <label className="ui-stack">
              <span>Số lượng điều chỉnh (+/-)</span>
              <input aria-label="Số lượng đề xuất điều chỉnh" type="number" step="1" value={proposalDeltaQty} onChange={(e) => setProposalDeltaQty(Number(e.target.value))} />
            </label>
            <label className="ui-stack">
              <span>Mã lý do điều chỉnh</span>
              <select aria-label="Mã lý do đề xuất điều chỉnh" value={proposalReasonCode} onChange={(e) => setProposalReasonCode(e.target.value)}>
                <option value="COUNT_VARIANCE">Chênh lệch kiểm đếm</option>
                <option value="PHYSICAL_DAMAGE">Hư hỏng vật lý</option>
                <option value="MISPLACED_STOCK">Thất lạc / Sai vị trí</option>
                <option value="SUPPLIER_SHORTAGE">Thiếu từ nhà cung cấp</option>
                <option value="EXPIRED_WRITE_OFF">Hủy hết hạn</option>
                <option value="RECALL_QUARANTINE">Cô lập thu hồi</option>
              </select>
            </label>
          </div>
          <div className="ui-inline-actions" style={{ marginTop: 8 }}>
            <button type="button" disabled={proposalBusy} onClick={onQuickAdjustmentProposal}>
              Tạo nhanh Đề xuất Điều chỉnh qua Cổng Phê duyệt Độc lập
            </button>
            {proposalStatus && (
              <div
                role="status"
                style={{
                  marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46',
                  border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%', fontWeight: 600,
                }}
              >
                {proposalStatus === 'PENDING_SOD_APPROVAL' || proposalStatus === 'PENDING_APPROVAL'
                  ? 'Trạng thái: Đã tạo đề xuất điều chỉnh tồn kho • Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm'
                  : proposalStatus === 'APPROVED' || proposalStatus === 'AUTO_APPROVED'
                    ? 'Trạng thái: Đã tạo và phê duyệt đề xuất điều chỉnh tồn kho qua Cổng Phê duyệt Độc lập'
                    : proposalStatus === 'REJECTED'
                      ? 'Trạng thái: Đề xuất điều chỉnh tồn kho đã bị từ chối'
                      : proposalStatus
                          .replace(/Chờ phê duyệt tách biệt nhiệm vụ\s*\(SoD\)/gi, 'Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm')
                          .replace(/\s*\(SoD\)/gi, '')}
              </div>
            )}
          </div>
        </UiCard>
      </div>
    )}
  </>
);
