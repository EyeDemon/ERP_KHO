// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { PDA_FLOOR_TILES, PdaFloorLauncher } from '../common/PdaFloorLauncher';

describe('TC-P1-03: PdaFloorLauncher Mobile Viewport (390x844) & 6 Core Warehouse Floor Tiles', () => {
  const originalInnerWidth = window.innerWidth;
  const originalInnerHeight = window.innerHeight;

  beforeEach(() => {
    Object.defineProperty(window, 'innerWidth', {
      writable: true,
      configurable: true,
      value: 390,
    });
    Object.defineProperty(window, 'innerHeight', {
      writable: true,
      configurable: true,
      value: 844,
    });
    window.dispatchEvent(new Event('resize'));
  });

  afterEach(() => {
    Object.defineProperty(window, 'innerWidth', {
      writable: true,
      configurable: true,
      value: originalInnerWidth,
    });
    Object.defineProperty(window, 'innerHeight', {
      writable: true,
      configurable: true,
      value: originalInnerHeight,
    });
    cleanup();
  });

  it('hiển thị đầy đủ 6 ô tác vụ sàn kho PDA ở viewport 390x844 với đúng testId và đường dẫn điều hướng', () => {
    expect(window.innerWidth).toBe(390);
    expect(window.innerHeight).toBe(844);

    render(
      <MemoryRouter>
        <PdaFloorLauncher />
      </MemoryRouter>,
    );

    const launcher = screen.getByTestId('pda-floor-launcher');
    expect(launcher).toBeTruthy();
    expect(launcher.textContent).toContain('Tác vụ sàn kho nhanh');

    const expectedTiles: Array<{ testId: string; href: string; title: string }> = [
      { testId: 'pda-tile-inbound', href: '/import-receipts', title: 'Nhận hàng nhập' },
      { testId: 'pda-tile-putaway', href: '/putaway-tasks', title: 'Cất hàng vào kệ' },
      { testId: 'pda-tile-picking', href: '/picking-tasks', title: 'Lấy hàng tại kệ' },
      { testId: 'pda-tile-packing', href: '/packing-sessions', title: 'Đóng gói đơn hàng' },
      { testId: 'pda-tile-handover', href: '/handover-manifests', title: 'Bàn giao vận đơn' },
      { testId: 'pda-tile-stocktake', href: '/stocktakes', title: 'Kiểm kê tại kệ' },
    ];

    expect(PDA_FLOOR_TILES).toHaveLength(6);

    for (const item of expectedTiles) {
      const tileLink = screen.getByTestId(item.testId) as HTMLAnchorElement;
      expect(tileLink).toBeTruthy();
      expect(tileLink.getAttribute('href')).toBe(item.href);
      expect(tileLink.textContent).toContain(item.title);
    }
  });
});
