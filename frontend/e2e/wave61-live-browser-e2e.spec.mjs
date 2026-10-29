import { test, expect } from '@playwright/test';
import path from 'node:path';

const FRONTEND_BASE = (process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173').replace(/\/$/, '');
const API_BASE = (process.env.PLAYWRIGHT_API_BASE_URL ?? 'http://localhost:5260').replace(/\/$/, '');
const ARTIFACT_DIR = 'C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb';

test.describe.configure({ mode: 'serial' });

test('Wave 61 Live Browser E2E: Login -> Dashboard -> Epic 90.1 ABC Slotting & Interleaving -> Epic 90.2 3PL Depositor Portal -> Epic 90.3 Accounting TT200 & E-Invoice ND70 -> Responsive 1440x900 & 390x844', async ({ page, request }) => {
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
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave61_e2e_1_dashboard_1440x900.png'), fullPage: false });

  // =========================================================================
  // 2. EPIC 90.1: DYNAMIC ABC VELOCITY SLOTTING & TASK INTERLEAVING (/putaway-tasks)
  // =========================================================================
  await page.goto(`${FRONTEND_BASE}/putaway-tasks`, { waitUntil: 'networkidle' });
  await assertNo404Page('PutawayTasks 1440x900');

  const abcBtn = page.getByTestId('btn-evaluate-abc-interleaving');
  await expect(abcBtn).toBeVisible({ timeout: 20_000 });

  const abcResPromise = page.waitForResponse(
    res => res.url().includes('/api/v1/inventory-control/slotting-interleaving/evaluate') && res.request().method() === 'GET',
    { timeout: 30_000 }
  );
  await abcBtn.click();
  const abcRes = await abcResPromise;
  expect(abcRes.status(), 'ABC Slotting & Interleaving API must return 200 OK').toBe(200);
  const abcBody = await abcRes.json();
  expect(abcBody.evaluatedSkuCount).toBeGreaterThanOrEqual(1);
  expect(abcBody.interleavedTaskPairCount).toBeGreaterThanOrEqual(1);

  const abcPanel = page.getByTestId('epic90-1-abc-slotting-interleaving-panel');
  await expect(abcPanel).toBeVisible({ timeout: 15_000 });
  await expect(abcPanel).toContainText('Tối ưu Vị trí Động ABC (Dynamic ABC Slotting)');
  await expect(abcPanel).toContainText('Chu trình Chuyến kép (Dual-Command Cycle)');

  await abcPanel.evaluate(el => el.scrollIntoView({ block: 'center' }));
  await page.waitForTimeout(200);

  const putawayOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(putawayOverflow1440, 'PutawayTasks 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave61_e2e_2_epic90_1_slotting_interleaving_1440x900.png'), fullPage: false });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(200);
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave61_e2e_6_mobile_putaway_slotting_390x844.png'), fullPage: false });

  // =========================================================================
  // 3. EPIC 90.2: SELF-SERVICE 3PL CLIENT DEPOSITOR PORTAL (/business-partners)
  // =========================================================================
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${FRONTEND_BASE}/business-partners`, { waitUntil: 'networkidle' });
  await assertNo404Page('BusinessPartners 1440x900');

  const depositorBtn = page.getByTestId('btn-load-3pl-depositor-portal');
  await expect(depositorBtn).toBeVisible({ timeout: 20_000 });

  const depositorResPromise = page.waitForResponse(
    res => res.url().includes('/api/v1/inventory-control/3pl-depositor-portal') && res.request().method() === 'GET',
    { timeout: 30_000 }
  );
  await depositorBtn.click();
  const depositorRes = await depositorResPromise;
  expect(depositorRes.status(), '3PL Depositor Portal API must return 200 OK').toBe(200);
  const depositorBody = await depositorRes.json();
  expect(depositorBody.crossOwnerLeakCount).toBe(0);
  expect(depositorBody.tenantAndOwnerIsolationVerified).toBe(true);

  const depositorPanel = page.getByTestId('epic90-2-3pl-depositor-portal-panel');
  await expect(depositorPanel).toBeVisible({ timeout: 15_000 });
  await expect(depositorPanel).toContainText('Cổng Chủ hàng 3PL (Depositor Portal)');
  await expect(depositorPanel).toContainText('Cô lập 9 chiều theo OwnerId • Rò rỉ chéo: 0');

  await depositorPanel.evaluate(el => el.scrollIntoView({ block: 'center' }));
  await page.waitForTimeout(200);

  const partnersOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(partnersOverflow1440, 'BusinessPartners 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave61_e2e_3_epic90_2_3pl_portal_1440x900.png'), fullPage: false });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(200);
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave61_e2e_7_mobile_partners_3pl_390x844.png'), fullPage: false });

  // =========================================================================
  // 4. EPIC 90.3: VIETNAM ACCOUNTING TT200 & E-INVOICE ND70 (/export-receipts)
  // =========================================================================
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${FRONTEND_BASE}/export-receipts`, { waitUntil: 'networkidle' });
  await assertNo404Page('ExportReceipts 1440x900');

  const syncAccBtn = page.getByTestId('btn-sync-accounting-einvoice');
  await expect(syncAccBtn).toBeVisible({ timeout: 20_000 });

  const accPreviewPromise = page.waitForResponse(
    res => res.url().includes('/api/v1/export-receipts/accounting-einvoice/preview') && res.request().method() === 'GET',
    { timeout: 30_000 }
  );
  await syncAccBtn.click();
  const accPreviewRes = await accPreviewPromise;
  expect(accPreviewRes.status(), 'Accounting & E-Invoice Preview API must return 200 OK').toBe(200);
  const accPreviewBody = await accPreviewRes.json();
  expect(accPreviewBody.journalImbalanceDelta).toBe(0);
  expect(accPreviewBody.doubleEntryBalanced).toBe(true);

  const accPanel = page.getByTestId('epic90-3-accounting-einvoice-panel');
  await expect(accPanel).toBeVisible({ timeout: 15_000 });
  await expect(accPanel).toContainText('Đồng bộ Kế toán Thông tư 200 (MISA_AMIS)');
  await expect(accPanel).toContainText('Bút toán Kép Thông tư 200/2014/TT-BTC');

  await accPanel.evaluate(el => el.scrollIntoView({ block: 'center' }));
  await page.waitForTimeout(200);

  const exportsOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(exportsOverflow1440, 'ExportReceipts 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave61_e2e_4_epic90_3_accounting_einvoice_1440x900.png'), fullPage: false });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(200);
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave61_e2e_8_mobile_exports_einvoice_390x844.png'), fullPage: false });

  // =========================================================================
  // 5. MOBILE PDA VIEWPORT (390x844) ON /dashboard
  // =========================================================================
  await page.goto(`${FRONTEND_BASE}/dashboard`, { waitUntil: 'networkidle' });
  await assertNo404Page('Mobile PDA 390x844 on /dashboard');
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave61_e2e_5_mobile_dashboard_390x844.png'), fullPage: false });

  expect(pageErrors, 'Zero unhandled page runtime errors across E2E journey').toEqual([]);
  expect(server5xxErrors, 'Zero HTTP 5xx responses across E2E journey').toEqual([]);
});
