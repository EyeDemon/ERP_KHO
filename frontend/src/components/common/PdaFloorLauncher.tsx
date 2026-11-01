import { Link } from 'react-router-dom';
import {
  ArrowLeftRight,
  ClipboardCheck,
  PackageCheck,
  PackageOpen,
  ScanBarcode,
  Truck,
} from 'lucide-react';

export interface PdaFloorTileItem {
  id: string;
  testId: string;
  title: string;
  subtitle: string;
  to: string;
  secondaryTo?: string;
  secondaryLabel?: string;
  accentColor: string;
  bgColor: string;
  borderColor: string;
}

export const PDA_FLOOR_TILES: PdaFloorTileItem[] = [
  {
    id: 'inbound',
    testId: 'pda-tile-inbound',
    title: '1. Nhận hàng',
    subtitle: 'Quét phiếu nhập & ASN tại cửa nhận',
    to: '/import-receipts',
    accentColor: '#0369a1',
    bgColor: '#f0f9ff',
    borderColor: '#bae6fd',
  },
  {
    id: 'putaway',
    testId: 'pda-tile-putaway',
    title: '2. Cất hàng',
    subtitle: 'Đưa hàng từ bãi nhận lên kệ lưu trữ',
    to: '/putaway-tasks',
    accentColor: '#047857',
    bgColor: '#ecfdf5',
    borderColor: '#a7f3d0',
  },
  {
    id: 'picking',
    testId: 'pda-tile-picking',
    title: '3. Lấy hàng',
    subtitle: 'Soạn hàng theo cụm đơn & sóng lấy hàng',
    to: '/picking-tasks',
    accentColor: '#b45309',
    bgColor: '#fffbeb',
    borderColor: '#fde68a',
  },
  {
    id: 'handover',
    testId: 'pda-tile-handover',
    title: '4. Đóng gói & Bàn giao AWB',
    subtitle: 'Quét vận đơn AWB bàn giao hãng vận chuyển',
    to: '/handover-manifests',
    secondaryTo: '/packing-sessions',
    secondaryLabel: 'Mở Bàn đóng gói',
    accentColor: '#4338ca',
    bgColor: '#eef2ff',
    borderColor: '#c7d2fe',
  },
  {
    id: 'returns-movements',
    testId: 'pda-tile-returns-movements',
    title: '5. Hàng hoàn & Chuyển vị trí',
    subtitle: 'Tiếp nhận kiện hoàn RMA & chuyển kệ nội bộ',
    to: '/customer-returns',
    secondaryTo: '/inventory-movements',
    secondaryLabel: 'Chuyển vị trí nội bộ',
    accentColor: '#be123c',
    bgColor: '#fff1f2',
    borderColor: '#fecdd3',
  },
  {
    id: 'stocktake',
    testId: 'pda-tile-stocktake',
    title: '6. Kiểm kê kho',
    subtitle: 'Đếm mù tuần hoàn tại vị trí kệ/Bin',
    to: '/stocktakes',
    accentColor: '#0f172a',
    bgColor: '#f8fafc',
    borderColor: '#cbd5e1',
  },
];

const renderTileIcon = (id: string) => {
  if (id === 'inbound') return <PackageOpen size={20} aria-hidden="true" />;
  if (id === 'putaway') return <PackageCheck size={20} aria-hidden="true" />;
  if (id === 'picking') return <ScanBarcode size={20} aria-hidden="true" />;
  if (id === 'handover') return <Truck size={20} aria-hidden="true" />;
  if (id === 'returns-movements') return <ArrowLeftRight size={20} aria-hidden="true" />;
  return <ClipboardCheck size={20} aria-hidden="true" />;
};

export function PdaFloorLauncher({ compactOnly = false }: { compactOnly?: boolean }) {
  return (
    <section
      data-testid="pda-floor-launcher"
      aria-label="Bàn điều hướng nhanh thiết bị cầm tay PDA (6 tác vụ sàn kho)"
      className={compactOnly ? 'pda-floor-launcher pda-floor-launcher-compact' : 'pda-floor-launcher'}
      style={{ padding: '12px 14px', borderRadius: 12, border: '1px solid #cbd5e1', background: '#ffffff', boxSizing: 'border-box', maxWidth: '100%', overflowX: 'hidden' }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 10, flexWrap: 'wrap' }}>
        <strong style={{ fontSize: 13.5, color: '#0f172a' }}>Bàn Phím Nhanh Sàn Kho PDA (6 Tác vụ Cốt lõi)</strong>
        <span style={{ fontSize: 11.5, color: '#475569', fontWeight: 600 }}>Chuẩn cảm ứng ≥ 48×48px • Sẵn sàng nhận súng quét HID</span>
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 10, width: '100%', boxSizing: 'border-box' }}>
        {PDA_FLOOR_TILES.map(tile => (
          <div key={tile.id} style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between', minHeight: 74, padding: '10px 12px', borderRadius: 10, border: `1.5px solid ${tile.borderColor}`, background: tile.bgColor, boxSizing: 'border-box', minWidth: 0 }}>
            <Link to={tile.to} data-testid={tile.testId} style={{ display: 'flex', alignItems: 'flex-start', gap: 8, minHeight: 48, textDecoration: 'none', color: tile.accentColor, fontWeight: 700, fontSize: 13.5, lineHeight: 1.3 }}>
              <span style={{ flexShrink: 0, marginTop: 1 }}>{renderTileIcon(tile.id)}</span>
              <span style={{ minWidth: 0, wordBreak: 'break-word' }}>
                <span style={{ display: 'block', color: '#0f172a', fontWeight: 700 }}>{tile.title}</span>
                <small style={{ display: 'block', color: '#475569', fontWeight: 500, fontSize: 11.5, marginTop: 2 }}>{tile.subtitle}</small>
              </span>
            </Link>
            {tile.secondaryTo && tile.secondaryLabel && (
              <div style={{ marginTop: 6, paddingTop: 6, borderTop: `1px dashed ${tile.borderColor}` }}>
                <Link to={tile.secondaryTo} style={{ fontSize: 11.5, fontWeight: 600, color: tile.accentColor, textDecoration: 'none' }}>→ {tile.secondaryLabel}</Link>
              </div>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}

export default PdaFloorLauncher;
