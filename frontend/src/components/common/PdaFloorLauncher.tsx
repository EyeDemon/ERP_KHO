import { Link } from 'react-router-dom';
import {
  Box,
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
  badgeText: string;
  to: string;
  accentColor: string;
  bgColor: string;
  borderColor: string;
}

export const PDA_FLOOR_TILES: PdaFloorTileItem[] = [
  {
    id: 'inbound',
    testId: 'pda-tile-inbound',
    title: 'Nhận hàng nhập',
    subtitle: '3 phiếu chờ',
    badgeText: '3 phiếu chờ',
    to: '/import-receipts',
    accentColor: '#1d4ed8',
    bgColor: '#f8fafc',
    borderColor: '#cbd5e1',
  },
  {
    id: 'putaway',
    testId: 'pda-tile-putaway',
    title: 'Cất hàng vào kệ',
    subtitle: '4 tác vụ mở',
    badgeText: '4 tác vụ mở',
    to: '/putaway-tasks',
    accentColor: '#1d4ed8',
    bgColor: '#f8fafc',
    borderColor: '#cbd5e1',
  },
  {
    id: 'picking',
    testId: 'pda-tile-picking',
    title: 'Lấy hàng tại kệ',
    subtitle: '5 tác vụ mở',
    badgeText: '5 tác vụ mở',
    to: '/picking-tasks',
    accentColor: '#1d4ed8',
    bgColor: '#f8fafc',
    borderColor: '#cbd5e1',
  },
  {
    id: 'packing',
    testId: 'pda-tile-packing',
    title: 'Đóng gói đơn hàng',
    subtitle: '2 bàn đang mở',
    badgeText: '2 bàn đang mở',
    to: '/packing-sessions',
    accentColor: '#1d4ed8',
    bgColor: '#f8fafc',
    borderColor: '#cbd5e1',
  },
  {
    id: 'handover',
    testId: 'pda-tile-handover',
    title: 'Bàn giao vận đơn',
    subtitle: '2 biên bản chờ',
    badgeText: '2 biên bản chờ',
    to: '/handover-manifests',
    accentColor: '#1d4ed8',
    bgColor: '#f8fafc',
    borderColor: '#cbd5e1',
  },
  {
    id: 'stocktake',
    testId: 'pda-tile-stocktake',
    title: 'Kiểm kê tại kệ',
    subtitle: '1 đợt kiểm kê',
    badgeText: '1 đợt kiểm kê',
    to: '/stocktakes',
    accentColor: '#1d4ed8',
    bgColor: '#f8fafc',
    borderColor: '#cbd5e1',
  },
];

const renderTileIcon = (id: string) => {
  if (id === 'inbound') return <PackageOpen size={20} aria-hidden="true" />;
  if (id === 'putaway') return <PackageCheck size={20} aria-hidden="true" />;
  if (id === 'picking') return <ScanBarcode size={20} aria-hidden="true" />;
  if (id === 'packing') return <Box size={20} aria-hidden="true" />;
  if (id === 'handover') return <Truck size={20} aria-hidden="true" />;
  return <ClipboardCheck size={20} aria-hidden="true" />;
};

export function PdaFloorLauncher({ compactOnly = false }: { compactOnly?: boolean }) {
  return (
    <section
      data-testid="pda-floor-launcher"
      aria-label="Tác vụ sàn kho nhanh"
      className={compactOnly ? 'pda-floor-launcher pda-floor-launcher-compact' : 'pda-floor-launcher'}
      style={{ padding: '12px 14px', borderRadius: 12, border: '1px solid #cbd5e1', background: '#ffffff', boxSizing: 'border-box', maxWidth: '100%', overflowX: 'hidden' }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 10 }}>
        <strong style={{ fontSize: 13.5, color: '#0f172a' }}>Tác vụ sàn kho nhanh</strong>
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 10, width: '100%', boxSizing: 'border-box' }}>
        {PDA_FLOOR_TILES.map(tile => (
          <Link
            key={tile.id}
            to={tile.to}
            data-testid={tile.testId}
            style={{
              display: 'flex',
              flexDirection: 'column',
              justifyContent: 'space-between',
              gap: 6,
              minHeight: 78,
              padding: '10px 12px',
              borderRadius: 10,
              border: `1.5px solid ${tile.borderColor}`,
              background: tile.bgColor,
              boxSizing: 'border-box',
              minWidth: 0,
              textDecoration: 'none',
            }}
          >
            <span style={{ display: 'inline-flex', alignItems: 'center', color: tile.accentColor }}>
              {renderTileIcon(tile.id)}
            </span>
            <span style={{ display: 'block', color: '#0f172a', fontWeight: 700, fontSize: 13.5, lineHeight: 1.25 }}>
              {tile.title}
            </span>
            <span style={{ display: 'inline-block', alignSelf: 'flex-start', padding: '2px 8px', borderRadius: 999, background: '#dbeafe', color: '#1e40af', fontWeight: 600, fontSize: 11.5, lineHeight: 1.2 }}>
              {tile.badgeText}
            </span>
          </Link>
        ))}
      </div>
    </section>
  );
}

export default PdaFloorLauncher;
