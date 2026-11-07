import { test, expect } from '@playwright/test';
import crypto from 'node:crypto';
import path from 'node:path';

const FRONTEND_BASE = (process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173').replace(/\/$/, '');
const API_BASE = (process.env.PLAYWRIGHT_API_BASE_URL ?? 'http://localhost:5260').replace(/\/$/, '');
const ARTIFACT_DIR = 'C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb';

test.describe.configure({ mode: 'serial' });

test('Wave 60 Live Browser E2E: Login -> Dashboard -> Epic 60.1 Kitting -> Epic 60.2 Cross-Dock -> Epic 60.3 Break-Bulk -> Responsive 1440x900 & 390x844', async ({ page, request }) => {
  test.setTimeout(180_000);

  const pageErrors = [];
  const server5xxErrors = [];

  page.on('pageerror', err => {
    pageErrors.push(err.message);
  });

  page.on('response', response => {
    if (response.url().includes('/api/') && response.status() >= 500) {
      server5xxErrors.push(`${response.status()} ${response.request().method()} ${response.url()}`);
    }
  });

  const assertNo404Page = async (label) => {
    await expect(
      page.locator('text=404 - Không tìm thấy trang'),
      `${label}: must not render 404 NotFound page`
    ).toHaveCount(0);
  };

  // =========================================================================
  // 1. DESKTOP VIEWPORT (1440x900) - LOGIN & DASHBOARD
  // =========================================================================
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${FRONTEND_BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(250);

  await page.locator('#login-username').fill('qa_admin');
  await page.locator('#login-password').fill('QaAdmin@123456!');

  const loginResponsePromise = page.waitForResponse(
    res => res.url().toLowerCase().includes('/auth/login') && res.request().method() === 'POST',
    { timeout: 30_000 }
  );
  await page.locator('button[type="submit"]').first().click();
  const loginRes = await loginResponsePromise;
  expect(loginRes.status(), 'Login API must return 200 OK').toBe(200);
  const loginJson = await loginRes.json();
  const accessToken = loginJson.accessToken ?? loginJson.token;
  expect(accessToken, 'JWT accessToken must be present').toBeTruthy();

  await page.waitForURL(url => !url.pathname.includes('/login'), { timeout: 30_000 });
  await page.goto(`${FRONTEND_BASE}/dashboard`, { waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: 'Tổng quan vận hành' })).toBeVisible({ timeout: 20_000 });
  await assertNo404Page('Dashboard 1440x900');

  const authMeRes = await request.get(`${API_BASE}/api/v1/auth/me`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });
  expect(authMeRes.status(), 'GET /api/v1/auth/me must return 200 OK').toBe(200);

  const dashboardOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(dashboardOverflow1440, 'Dashboard 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave60_e2e_1_dashboard_1440x900.png'), fullPage: false });

  // =========================================================================
  // 2. EPIC 60.1: PHYSICAL FLOOR KITTING & DE-KITTING WORK ORDERS (/products)
  // =========================================================================
  await page.goto(`${FRONTEND_BASE}/products`, { waitUntil: 'networkidle' });
  await assertNo404Page('Products 1440x900');

  await page.getByTestId('tab-bundle-gift').click();
  await page.waitForTimeout(250);

  const executeKwoBtn = page.getByTestId('btn-execute-kit-work-order');
  await expect(executeKwoBtn).toBeVisible({ timeout: 20_000 });

  const completeKwoPromise = page.waitForResponse(
    res => res.url().includes('/api/v1/products/kit-work-orders/') && res.url().includes('/complete') && res.request().method() === 'POST',
    { timeout: 30_000 }
  );
  await executeKwoBtn.click();
  const completeKwoRes = await completeKwoPromise;
  expect(completeKwoRes.status(), 'Complete KitWorkOrder API must return 200 OK').toBe(200);
  const completedKwo = await completeKwoRes.json();
  expect(completedKwo.status).toBe('Completed');
  expect(completedKwo.completedKitQty).toBeGreaterThan(0);

  const kwoTable = page.getByTestId('kit-work-orders-table');
  await expect(kwoTable).toBeVisible();
  await expect(kwoTable).toContainText(completedKwo.workOrderCode);
  await expect(kwoTable).toContainText('Đã hoàn thành');

  await kwoTable.evaluate(el => {
    const card = el.closest('section, .ui-card') ?? el;
    card.scrollIntoView({ block: 'start' });
  });
  await page.waitForTimeout(200);

  const productsOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(productsOverflow1440, 'Products 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave60_e2e_2_epic60_1_kitting_1440x900.png'), fullPage: false });

  // Cancel the E2E KitWorkOrder after capturing screenshot so component & kit balances return to exact baseline
  const cancelKwoRes = await request.post(`${API_BASE}/api/v1/products/kit-work-orders/${completedKwo.id}/cancel`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      reason: 'Hoàn trả tồn kho sau khi chụp bằng chứng E2E Wave 60'
    }
  });
  expect(cancelKwoRes.status(), 'Cancel KitWorkOrder reversal must return 200 OK').toBe(200);

  // =========================================================================
  // 3. EPIC 60.2: INBOUND-TO-OUTBOUND CROSS-DOCKING ENGINE (/putaway-tasks)
  // =========================================================================
  await page.goto(`${FRONTEND_BASE}/putaway-tasks`, { waitUntil: 'networkidle' });
  await assertNo404Page('PutawayTasks 1440x900');

  const crossDockBtn = page.getByTestId('btn-execute-cross-dock');
  await expect(crossDockBtn).toBeVisible({ timeout: 20_000 });

  const crossDockResPromise = page.waitForResponse(
    res => res.url().includes('/api/v1/putaway-tasks/') && res.url().includes('/cross-dock') && res.request().method() === 'POST',
    { timeout: 30_000 }
  );
  await crossDockBtn.click();
  const crossDockRes = await crossDockResPromise;
  expect(crossDockRes.status(), 'Cross-Dock API must return 200 OK').toBe(200);
  const crossDockTask = await crossDockRes.json();
  expect(crossDockTask.isCrossDock).toBe(true);
  expect(crossDockTask.crossDockAllocatedQty).toBeGreaterThan(0);

  const crossDockBanner = page.getByTestId('cross-dock-status-banner');
  await expect(crossDockBanner).toBeVisible({ timeout: 15_000 });
  await expect(crossDockBanner).toContainText('CROSS-DOCK THÀNH CÔNG');

  const putawayOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(putawayOverflow1440, 'PutawayTasks 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave60_e2e_3_epic60_2_crossdock_1440x900.png'), fullPage: false });

  // Reverse the Cross-Dock after capturing screenshot so PutawayTask and InventoryStocks return to exact baseline
  const reverseCrossDockRes = await request.post(`${API_BASE}/api/v1/putaway-tasks/${crossDockTask.id}/cross-dock`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      reverse: true
    }
  });
  expect(reverseCrossDockRes.status(), 'Reverse Cross-Dock API must return 200 OK').toBe(200);

  // =========================================================================
  // 4. EPIC 60.3: AUTOMATED UOM BREAK-BULK REPLENISHMENT (/inventory-movements)
  // =========================================================================
  await page.goto(`${FRONTEND_BASE}/inventory-movements`, { waitUntil: 'networkidle' });
  await assertNo404Page('InventoryMovements 1440x900');

  await page.getByTestId('tab-replenishment').click();
  await page.waitForTimeout(200);

  const breakBulkBtn = page.getByTestId('btn-auto-break-bulk');
  await expect(breakBulkBtn).toBeVisible({ timeout: 20_000 });

  const breakBulkResPromise = page.waitForResponse(
    res => res.url().includes('/api/v1/inventory-control/replenishment/break-bulk') && res.request().method() === 'POST',
    { timeout: 30_000 }
  );
  await breakBulkBtn.click();
  const breakBulkRes = await breakBulkResPromise;
  expect(breakBulkRes.status(), 'Break-Bulk Replenishment API must return 200 OK').toBe(200);
  const breakBulkBody = await breakBulkRes.json();
  expect(breakBulkBody.movedBaseQty).toBeGreaterThan(0);
  expect(breakBulkBody.baseUomDelta).toBe(0);

  const breakBulkNotice = page.getByTestId('break-bulk-replenish-notice');
  await expect(breakBulkNotice).toBeVisible({ timeout: 15_000 });
  await expect(breakBulkNotice).toContainText('Δ BaseUom = 0.0000');

  const breakBulkTable = page.getByTestId('break-bulk-conversion-table');
  await expect(breakBulkTable).toBeVisible();
  await expect(breakBulkTable).toContainText('Khớp 2 bút toán (-1 THÙNG / +12 ĐV • Δ = 0.0000)');

  const movementsOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(movementsOverflow1440, 'InventoryMovements 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave60_e2e_4_epic60_3_breakbulk_1440x900.png'), fullPage: false });

  // Capture Mobile PDA 390x844 on /inventory-movements immediately so notice & badge are visible
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(250);
  const ovMovementsMobile = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(ovMovementsMobile, 'Mobile PDA 390x844 on /inventory-movements must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave60_e2e_5_mobile_pda_390x844.png'), fullPage: false });

  // Reverse the Break-Bulk Replenishment after capturing screenshots so bins return to exact baseline
  const revBreakBulkRes = await request.post(`${API_BASE}/api/v1/inventory-control/replenishment/break-bulk`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      warehouseId: breakBulkBody.warehouseId,
      productId: breakBulkBody.productId,
      sourceLocationId: breakBulkBody.sourceLocationId,
      targetLocationId: breakBulkBody.targetLocationId,
      sourceUomId: breakBulkBody.sourceUomId,
      sourceUomCode: breakBulkBody.sourceUomCode,
      sourcePackQty: breakBulkBody.sourcePackQty,
      conversionFactor: breakBulkBody.conversionFactor,
      sourceHandlingUnitId: breakBulkBody.sourceHandlingUnitId,
      reverse: true,
      originalReferenceId: breakBulkBody.referenceId
    }
  });
  expect(revBreakBulkRes.status(), 'Reverse Break-Bulk Replenishment API must return 200 OK').toBe(200);

  // =========================================================================
  // 5. MOBILE PDA VIEWPORT (390x844) - ALL 4 ROUTES + SCREENSHOTS 6, 7, 8
  // =========================================================================
  await page.setViewportSize({ width: 390, height: 844 });

  await page.goto(`${FRONTEND_BASE}/dashboard`, { waitUntil: 'networkidle' });
  await assertNo404Page('Mobile PDA 390x844 on /dashboard');
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave60_e2e_6_mobile_dashboard_390x844.png'), fullPage: false });

  await page.goto(`${FRONTEND_BASE}/products`, { waitUntil: 'networkidle' });
  await assertNo404Page('Mobile PDA 390x844 on /products');
  await page.getByTestId('tab-bundle-gift').click();
  await page.waitForTimeout(250);
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave60_e2e_7_mobile_products_390x844.png'), fullPage: false });

  await page.goto(`${FRONTEND_BASE}/putaway-tasks`, { waitUntil: 'networkidle' });
  await assertNo404Page('Mobile PDA 390x844 on /putaway-tasks');
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave60_e2e_8_mobile_putaway_390x844.png'), fullPage: false });

  expect(pageErrors, 'Zero unhandled page runtime errors across E2E journey').toEqual([]);
  expect(server5xxErrors, 'Zero HTTP 5xx responses across E2E journey').toEqual([]);
});
