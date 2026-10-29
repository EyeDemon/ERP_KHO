import { test, expect } from '@playwright/test';

const base = (process.env.PLAYWRIGHT_BASE_URL
  ?? 'http://127.0.0.1:5173').replace(/\/$/, '');

test('ERP/WMS: Real dashboard, Blueprint routing, and inventory fixture disclosures', async ({ page }) => {
  const mutations = [];
  const runtimeErrors = [];
  page.on('request', request => {
    if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method())) mutations.push(request.method() + ' ' + request.url());
  });
  page.on('pageerror', error => runtimeErrors.push(error.message));

  await page.goto(base + '/system-blueprint', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: 'Bản đồ chức năng ERP/WMS hoàn chỉnh', level: 1 })).toBeVisible();
  await expect(page).toHaveURL(base + '/system-blueprint');

  await page.goto(base + '/system-blueprint/coverage', { waitUntil: 'domcontentloaded' });
  await expect(page).toHaveURL(base + '/system-blueprint/coverage');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  await page.goto(base + '/system-blueprint/inventory-control', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('note').filter({ hasText: 'RECON-HCM-0930' })).toBeVisible();
  await page.goto(base + '/system-blueprint/inventory-control/INV-11', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('note').filter({ hasText: 'kịch bản minh họa độc lập' })).toBeVisible();

  expect(mutations, 'Read-only preview must not perform mutation requests').toEqual([]);
  expect(runtimeErrors, 'Preview must not throw unhandled runtime errors').toEqual([]);
});

test('ERP/WMS: reconciliation layout, table scroll and usable mobile nav', async ({ page }) => {
  const mutations = [];
  const runtimeErrors = [];
  await page.addInitScript(() => {
    if (!localStorage.getItem('token')) {
      localStorage.setItem('token', 'e2e-preview-token');
      localStorage.setItem('role', 'Admin');
      localStorage.setItem('userId', '1');
      localStorage.setItem('permissions', JSON.stringify(['warehouse.read', 'inventory.read', 'inventory_ledger.read']));
    }
  });
  await page.route('**/api/Warehouses**', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 1, name: 'Distribution Center - South (WH-HCM-01)', isActive: true },
        { id: 2, name: 'Northern Hub (WH-HN-01)', isActive: true },
      ]),
    });
  });
  await page.route('**/api/InventoryReconciliation**', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [
          {
            warehouseId: 1,
            warehouseName: 'Distribution Center - South (WH-HCM-01)',
            productId: 1008,
            productCode: 'SKU-1008',
            productName: 'Industrial Barcode Scanner Gun',
            unitName: 'EA',
            currentQuantity: 28,
            expectedQuantity: 28,
            difference: 0,
            lastUpdated: '2026-04-12T08:30:00Z',
            lastTransactionAt: '2026-04-12T08:15:00Z',
            status: 'Match',
            allStatusStatus: 'Match',
          },
          {
            warehouseId: 1,
            warehouseName: 'Distribution Center - South (WH-HCM-01)',
            productId: 2012,
            productCode: 'SKU-2012',
            productName: 'Cold-Chain Temperature Logger',
            unitName: 'EA',
            currentQuantity: 18,
            expectedQuantity: 20,
            difference: -2,
            lastUpdated: '2026-04-12T07:55:00Z',
            lastTransactionAt: '2026-04-12T07:50:00Z',
            status: 'Mismatch',
            allStatusStatus: 'Mismatch',
          },
        ],
        totalRecords: 2,
        pageIndex: 1,
        pageSize: 20,
        totalPages: 1,
      }),
    });
  });
  page.on('request', request => {
    if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method())) mutations.push(request.method() + ' ' + request.url());
  });
  page.on('pageerror', error => runtimeErrors.push(error.message));

  for (const width of [320, 360, 390, 768, 1366]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(base + '/inventory-reconciliation', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /Đối chiếu tồn kho & ledger/, level: 1 })).toBeVisible();
    await expect(page.getByRole('note').filter({ hasText: /không phải kết quả từ SQL Server thật|Kết quả này chỉ để phát hiện sai lệch/ }).first()).toBeVisible();
    await expect(page.getByText(/Độ lệch (tuyệt đối|đã xác định)/)).toBeVisible();
    await expect(page.locator('.reconciliation-table')).toContainText('SKU-1008');
    await expect(page.locator('.reconciliation-table')).toContainText('SKU-2012');

    const m = await page.evaluate(() => {
      const doc = document.documentElement;
      const table = document.querySelector('.ui-table-scroll');
      const nav = document.querySelector('.mobile-nav-toggle');
      const badge = document.querySelector('.runtime-badge');
      const leading = document.querySelector('.topbar-leading');
      const title = document.querySelector('.topbar-title');
      const center = nav?.getBoundingClientRect();
      const hit = center ? document.elementFromPoint(center.left + center.width / 2, center.top + center.height / 2) : null;
      return {
        viewport: innerWidth, scrollWidth: doc.scrollWidth, clientWidth: doc.clientWidth,
        tableClient: table?.clientWidth, tableScroll: table?.scrollWidth,
        tableOverflow: table ? getComputedStyle(table).overflowX : null,
        navHit: !!hit?.closest('.mobile-nav-toggle'),
        navWidth: nav?.getBoundingClientRect().width ?? 0,
        leadingWidth: leading?.getBoundingClientRect().width ?? 0,
        titleHeight: title?.getBoundingClientRect().height ?? 0,
        badgeWidth: badge?.getBoundingClientRect().width ?? 0,
      };
    });
    expect(m.scrollWidth, 'No document horizontal overflow at ' + width + 'px: ' + JSON.stringify(m)).toBeLessThanOrEqual(m.clientWidth);
    expect(m.tableOverflow).toBe('auto');
    if (width <= 390) {
      expect(m.tableScroll, 'Wide table must remain scrollable inside card').toBeGreaterThan(m.tableClient);
      expect(m.navHit, 'Menu must be hit-testable at ' + width + 'px: ' + JSON.stringify(m)).toBe(true);
      expect(m.leadingWidth, 'Title row must not collapse to zero width').toBeGreaterThan(100);
      await page.locator('.mobile-nav-toggle').click();
      await expect(page.locator('.mobile-nav-toggle')).toHaveAttribute('aria-expanded', 'true');
      // The open drawer/backdrop intentionally intercepts the original topbar toggle.
      // Close with the drawer's own accessible button, not by clicking through the overlay.
      await page.locator('.sidebar-mobile-close').click();
      await expect(page.locator('.mobile-nav-toggle')).toHaveAttribute('aria-expanded', 'false');
    }
    console.log('ERP/WMS viewport metrics', JSON.stringify(m));
  }

  expect(mutations, 'Read-only preview must not perform mutation requests').toEqual([]);
  expect(runtimeErrors, 'Preview must not throw unhandled runtime errors').toEqual([]);
});
