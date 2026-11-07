import React from 'react';
import { Link } from 'react-router-dom';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  activityLabel,
  capacityTone,
  mapped,
  stateLabel,
  type LayoutForm,
  type MapItem,
  type ProductivityAndBiSnapshot,
} from './warehouseMapShared';

interface WarehouseMapTopCardsProps {
  inRouter: boolean;
  items: MapItem[];
  mappedCount: number;
  avgUtilization: number;
  biSnapshot: ProductivityAndBiSnapshot | null;
  loading: boolean;
  saving: boolean;
  biSyncBusy: boolean;
  biSyncResult: { snapshotId: string; status: string } | null;
  onSyncBiSnapshot: () => void;
}

export const WarehouseMapTopCards: React.FC<WarehouseMapTopCardsProps> = ({
  inRouter,
  items,
  mappedCount,
  avgUtilization,
  biSnapshot,
  loading,
  saving,
  biSyncBusy,
  biSyncResult,
  onSyncBiSnapshot,
}) => (
  <>
    <div
      data-testid="warehouse-topology-tabs"
      role="navigation"
      aria-label="Điều hướng cấu trúc kho"
      style={{
        display: 'inline-flex',
        flexWrap: 'wrap',
        gap: 6,
        marginBottom: 12,
        padding: 4,
        borderRadius: 10,
        background: '#f1f5f9',
        border: '1px solid #e2e8f0',
      }}
    >
      {[
        { to: '/warehouses', label: 'Danh mục Kho hàng', testId: 'tab-warehouses', active: false },
        { to: '/warehouse-locations', label: 'Cấu trúc & Mã vạch Vị trí Kệ', testId: 'tab-warehouse-locations', active: false },
        { to: '/warehouse-map', label: 'Bản đồ Nhiệt Kho 2D', testId: 'tab-warehouse-map', active: true },
      ].map((tab) => {
        const style: React.CSSProperties = {
          padding: '8px 16px',
          borderRadius: 8,
          fontWeight: 600,
          fontSize: 13,
          textDecoration: 'none',
          background: tab.active ? '#ffffff' : 'transparent',
          color: tab.active ? '#0f172a' : '#475569',
          border: tab.active ? '1px solid #cbd5e1' : '1px solid transparent',
          boxShadow: tab.active ? '0 1px 2px rgba(15, 23, 42, 0.06)' : 'none',
        };
        return inRouter ? (
          <Link key={tab.to} to={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
            {tab.label}
          </Link>
        ) : (
          <a key={tab.to} href={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
            {tab.label}
          </a>
        );
      })}
    </div>

    <UiCard title="Tổng quan Mật độ Sơ đồ Không gian Kho">
      <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, maxWidth: '100%' }}>
        <div>
          <strong>Độ phủ tọa độ sơ đồ không gian</strong><br />
          {items.length > 0 ? Math.round((mappedCount / items.length) * 100) : 0}% ({mappedCount}/{items.length} vị trí)
        </div>
        <div>
          <strong>Mật độ lấp đầy & Ô lưu trữ tải cao (≥75%)</strong><br />
          TB {avgUtilization}% • {items.filter((item) => (item.utilizationPercent ?? 0) >= 75).length} vị trí tải cao
        </div>
        <div>
          <strong>Cường độ vận hành & Cất hàng chờ xử lý</strong><br />
          {items.reduce((sum, item) => sum + (item.recentMovementCount || 0), 0)} dịch chuyển 60p • {items.reduce((sum, item) => sum + (item.activePutawayCount || 0), 0)} tác vụ cất hàng
        </div>
      </div>
    </UiCard>

    <UiCard title="Năng suất Tác vụ Kho, Bản đồ Nhiệt & Điểm chuẩn Xuất BI">
      <div className="ui-inline-wrap">
        <span style={{ fontSize: 13, color: '#334155' }}>{`• Cất hàng hoàn tất: ${biSnapshot?.completedPutawaysCount ?? 0}`}</span>
        <span style={{ fontSize: 13, color: '#334155' }}>{`• Lấy hàng hoàn tất: ${biSnapshot?.completedPicksCount ?? 0}`}</span>
        <span style={{ fontSize: 13, color: '#334155' }}>{`• Đóng gói hoàn tất: ${biSnapshot?.completedPacksCount ?? 0}`}</span>
        <span style={{ fontSize: 13, color: '#334155' }}>{`• Tổng dịch chuyển nội bộ: ${biSnapshot?.totalMovesCount ?? items.reduce((sum, item) => sum + (item.recentMovementCount || 0), 0)}`}</span>
        <span style={{ fontSize: 13, color: '#334155' }}>{`• Mốc đồng bộ sổ cái: #${biSnapshot?.ledgerWatermarkId ?? 0} • Mốc kiểm toán: #${biSnapshot?.auditWatermarkId ?? 0}`}</span>
        <UiBadge tone={biSnapshot?.readyForBiExport !== false ? 'success' : 'neutral'}>
          {biSnapshot?.readyForBiExport !== false ? `Sẵn sàng đồng bộ BI (${biSnapshot?.stockSnapshotRowCount ?? items.length} dòng)` : 'Đang đồng bộ BI'}
        </UiBadge>
      </div>
      <div className="ui-inline-actions" style={{ marginTop: 8 }}>
        <button type="button" disabled={loading || saving || biSyncBusy} onClick={onSyncBiSnapshot}>
          Đồng bộ Ảnh chụp Bản đồ Nhiệt &amp; Chuẩn dữ liệu BI
        </button>
        {biSyncResult && (
          <UiBadge tone="success">
            {biSyncResult.snapshotId} • {({ BI_SNAPSHOT_SYNCED: 'Đã đồng bộ chuẩn dữ liệu BI', READY_FOR_BI_EXPORT: 'Sẵn sàng xuất dữ liệu BI', SYNCED: 'Đã đồng bộ' } as Record<string, string>)[biSyncResult.status] || biSyncResult.status}
          </UiBadge>
        )}
      </div>
    </UiCard>
  </>
);

interface WarehouseMapBottomSectionsProps {
  items: MapItem[];
  unmappedItems: MapItem[];
  canManage: boolean;
  layout: LayoutForm | null;
  setLayout: React.Dispatch<React.SetStateAction<LayoutForm | null>>;
  saving: boolean;
  onOpenLayout: (item: MapItem) => void;
  onSaveLayout: (clear: boolean) => void;
}

const rightNumStyle: React.CSSProperties = {
  textAlign: 'right',
  fontVariantNumeric: 'tabular-nums',
  whiteSpace: 'nowrap',
};

export const WarehouseMapBottomSections: React.FC<WarehouseMapBottomSectionsProps> = ({
  items,
  unmappedItems,
  canManage,
  layout,
  setLayout,
  saving,
  onOpenLayout,
  onSaveLayout,
}) => (
  <>
    <UiCard title="Chi tiết Bản đồ Nhiệt Vận hành">
      <UiTableScroll>
        <table aria-label="Chi tiết heatmap kho thật" className="ui-table-compact">
          <thead>
            <tr>
              <th style={{ minWidth: 110 }}>Vị trí</th>
              <th style={{ minWidth: 120 }}>Phân lớp lưu trữ</th>
              <th style={{ ...rightNumStyle, minWidth: 96 }}>Mức sử dụng</th>
              <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Sức chứa</th>
              <th style={{ ...rightNumStyle, minWidth: 106 }}>Dịch chuyển 60p</th>
              <th style={{ ...rightNumStyle, minWidth: 96 }}>Cất hàng mở</th>
              <th style={{ minWidth: 92, whiteSpace: 'nowrap' }}>Hoạt động</th>
              {canManage && <th style={{ minWidth: 84, whiteSpace: 'nowrap' }}>Bố trí</th>}
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.locationId}>
                <td><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.code}</strong><br /><small>{item.structurePath?.split('/').join(' / ') || item.name}</small></td>
                <td>{item.storageClass === 'AMBIENT' ? 'Nhiệt độ thường' : item.storageClass === 'COLD' ? 'Kho lạnh' : (item.storageClass || '—')}</td>
                <td style={rightNumStyle}>{item.utilizationPercent != null ? item.utilizationPercent + '%' : 'Chưa có'}</td>
                <td><UiBadge tone={capacityTone(item)}>{stateLabel[item.capacityState] || item.capacityState}</UiBadge></td>
                <td style={rightNumStyle}>{item.recentMovementCount}</td>
                <td style={rightNumStyle}>{item.activePutawayCount}</td>
                <td>{activityLabel[item.activityLevel] || item.activityLevel}</td>
                {canManage && <td style={{ whiteSpace: 'nowrap' }}><button type="button" onClick={() => onOpenLayout(item)} disabled={!item.rowVersion} style={{ whiteSpace: 'nowrap' }}>{mapped(item) ? 'Sửa bố trí' : 'Bố trí'}</button></td>}
              </tr>
            ))}
          </tbody>
        </table>
      </UiTableScroll>
    </UiCard>

    {unmappedItems.length > 0 && (
      <UiCard title="Vị trí chưa bố trí">
        <p className="ui-muted-text">Hệ thống không tự suy đoán tọa độ từ đường dẫn cấu trúc. Quản lý kho phải bố trí bằng tọa độ phần trăm.</p>
        <UiTableScroll>
          <table aria-label="Location chưa bố trí bản đồ" className="ui-table-compact">
            <thead>
              <tr>
                <th style={{ minWidth: 92, whiteSpace: 'nowrap' }}>Mã vị trí</th>
                <th style={{ minWidth: 130 }}>Tên vị trí</th>
                <th style={{ minWidth: 150 }}>Đường dẫn cấu trúc</th>
                <th style={{ minWidth: 120 }}>Phân lớp lưu trữ</th>
                {canManage && <th style={{ minWidth: 88, whiteSpace: 'nowrap' }}>Thao tác</th>}
              </tr>
            </thead>
            <tbody>
              {unmappedItems.map((item) => (
                <tr key={item.locationId}>
                  <td style={{ whiteSpace: 'nowrap' }}><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.code}</strong></td>
                  <td>{item.name.replace(/\s*\(RMA\)/gi, '')}</td>
                  <td>{item.structurePath?.split('/').join(' / ') || '—'}</td>
                  <td>{item.storageClass === 'AMBIENT' ? 'Nhiệt độ thường' : item.storageClass === 'COLD' ? 'Kho lạnh' : (item.storageClass || '—')}</td>
                  {canManage && <td style={{ whiteSpace: 'nowrap' }}><button type="button" onClick={() => onOpenLayout(item)} disabled={!item.rowVersion} style={{ whiteSpace: 'nowrap' }}>Bố trí</button></td>}
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>
    )}

    {layout && canManage && (
      <UiCard title={'Bố trí bản đồ • ' + layout.code}>
        <p className="ui-muted-text">Tọa độ dùng phần trăm khung bản đồ 0–100. X + Rộng và Y + Cao không được vượt 100.</p>
        <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 12, maxWidth: '100%' }}>
          <label className="ui-stack"><span>X (%)</span><input aria-label="Map X" type="number" min="0" max="100" step="0.01" value={layout.x} onChange={(event) => setLayout((current) => current && ({ ...current, x: event.target.value }))} /></label>
          <label className="ui-stack"><span>Y (%)</span><input aria-label="Map Y" type="number" min="0" max="100" step="0.01" value={layout.y} onChange={(event) => setLayout((current) => current && ({ ...current, y: event.target.value }))} /></label>
          <label className="ui-stack"><span>Rộng (%)</span><input aria-label="Map Width" type="number" min="0.01" max="100" step="0.01" value={layout.width} onChange={(event) => setLayout((current) => current && ({ ...current, width: event.target.value }))} /></label>
          <label className="ui-stack"><span>Cao (%)</span><input aria-label="Map Height" type="number" min="0.01" max="100" step="0.01" value={layout.height} onChange={(event) => setLayout((current) => current && ({ ...current, height: event.target.value }))} /></label>
        </div>
        <div className="ui-inline-actions">
          <button type="button" onClick={() => onSaveLayout(false)} disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu bố trí'}</button>
          <button type="button" onClick={() => setLayout(null)} disabled={saving}>Hủy</button>
          {mapped(items.find((item) => item.locationId === layout.locationId) ?? ({} as MapItem)) && (
            <button type="button" onClick={() => onSaveLayout(true)} disabled={saving}>Xóa bố trí</button>
          )}
        </div>
      </UiCard>
    )}
  </>
);
