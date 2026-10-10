import { test, expect } from '@playwright/test';

const base = (process.env.PLAYWRIGHT_BASE_URL
  ?? 'https://erp-wms-blueprint-demo-git-part-syste-b0b8f5-bayuuandree99-8132.vercel.app').replace(/\/$/, '');

test('PR33: Real dashboard, Blueprint routing, and inventory fixture disclosures', async ({ page }) => {
  const mutations = [];
  const runtimeErrors = [];
  page.on('request', request => {
    if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method())) mutations.push(request.method() + ' ' + request.url());
  });
  page.on('pageerror', error => runtimeErrors.push(error.message));

  await page.goto(base + '/', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: 'Tổng quan vận hành', level: 1 })).toBeVisible();
  await expect(page).toHaveURL(base + '/');
  await page.getByRole('link', { name: /Mở bản đồ hệ thống/ }).first().click();
  await expect(page.getByRole('heading', { name: 'Bản đồ chức năng ERP/WMS hoàn chỉnh', level: 1 })).toBeVisible();
  await expect(page.getByText(/Commit mốc: 7980d9c01c7d/)).toBeVisible();
  await page.getByRole('link', { name: /Quay lại hệ thống thật/ }).first().click();
  await expect(page).toHaveURL(base + '/');
  await expect(page.getByRole('heading', { name: 'Tổng quan vận hành', level: 1 })).toBeVisible();

  await page.goto(base + '/system-blueprint/inventory-control', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('note').filter({ hasText: 'RECON-HCM-0930' })).toBeVisible();
  await page.goto(base + '/system-blueprint/inventory-control/INV-11', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('note').filter({ hasText: 'kịch bản minh họa độc lập' })).toBeVisible();

  expect(mutations, 'Read-only preview must not perform mutation requests').toEqual([]);
  expect(runtimeErrors, 'Preview must not throw unhandled runtime errors').toEqual([]);
});

test('PR33: reconciliation layout, table scroll and usable mobile nav', async ({ page }) => {
  const mutations = [];
  const runtimeErrors = [];
  page.on('request', request => {
    if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method())) mutations.push(request.method() + ' ' + request.url());
  });
  page.on('pageerror', error => runtimeErrors.push(error.message));

  for (const width of [320, 360, 390, 768, 1366]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(base + '/inventory-reconciliation', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /Đối chiếu tồn kho & ledger/, level: 1 })).toBeVisible();
    await expect(page.getByRole('note').filter({ hasText: /không phải kết quả từ SQL Server thật/ })).toBeVisible();
    await expect(page.getByText('Độ lệch tuyệt đối')).toBeVisible();
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
      await page.locator('.mobile-nav-toggle').click();
      await expect(page.locator('.mobile-nav-toggle')).toHaveAttribute('aria-expanded', 'false');
    }
    console.log('PR33 viewport metrics', JSON.stringify(m));
  }

  expect(mutations, 'Read-only preview must not perform mutation requests').toEqual([]);
  expect(runtimeErrors, 'Preview must not throw unhandled runtime errors').toEqual([]);
});
