import { test, expect } from '@playwright/test';
import crypto from 'node:crypto';
import path from 'node:path';

const FRONTEND_BASE = (process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173').replace(/\/$/, '');
const API_BASE = (process.env.PLAYWRIGHT_API_BASE_URL ?? 'http://localhost:5260').replace(/\/$/, '');
const ARTIFACT_DIR = 'C:/Users/van40/.gemini/antigravity/brain/16f9bcc3-2924-4af8-9fa0-4b8b8bfc08bb';

test.describe.configure({ mode: 'serial' });

test('Wave 59 Live Browser E2E (UNV-02 Closure): Login -> Dashboard -> Epic 30.1 -> Epic 30.2 -> Epic 30.3 -> Responsive 1440x900 & 390x844', async ({ page, request }) => {
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
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave59_e2e_1_dashboard_1440x900.png'), fullPage: false });

  // =========================================================================
  // 2. EPIC 30.1: WAVE RELEASE TEMPLATE -> CLUSTER TOTE -> PUT-TO-WALL -> AUTO PACKINGSESSION
  // =========================================================================
  const waveReleaseRes = await request.post(`${API_BASE}/api/v1/picking-tasks/waves/release`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      warehouseId: 1,
      waveTemplateCode: 'WAVE-TPL-CUTOFF-1400',
      carrierCutoffWindow: '14:00',
      slaTierFilter: 'ALL',
      zoneCode: 'ZONE-A',
      maxOrdersPerClusterTote: 4,
      pickingTaskIds: [1]
    }
  });
  expect(waveReleaseRes.status()).toBe(200);
  const waveReleaseBody = await waveReleaseRes.json();
  expect(waveReleaseBody.waveCode).toBeTruthy();

  await page.goto(`${FRONTEND_BASE}/picking-tasks`, { waitUntil: 'networkidle' });
  await expect(page.getByTestId('wave-release-template-cluster-banner')).toBeVisible({ timeout: 20_000 });
  await assertNo404Page('PickingTasks 1440x900');

  await page.locator('input[aria-label="Số lượng chia vào ô Put-to-Wall"]').fill('4');
  const ptwSortResPromise = page.waitForResponse(
    res => res.url().includes('/put-to-wall-sort') && res.request().method() === 'POST',
    { timeout: 30_000 }
  );
  await page.getByTestId('btn-confirm-put-to-wall-sort').click();
  const ptwSortRes = await ptwSortResPromise;
  expect(ptwSortRes.status(), 'Put-to-Wall sort API must return 200 OK').toBe(200);
  const ptwSortBody = await ptwSortRes.json();
  expect(ptwSortBody.slotStatus).toBe('COMPLETED');
  expect(ptwSortBody.autoCreatedPackingSession).toBe(true);
  expect(ptwSortBody.packingSessionCode).toContain('PACK-PTW-');

  const ptwNotice = page.getByTestId('put-to-wall-sort-notice');
  await expect(ptwNotice).toBeVisible();
  await expect(ptwNotice).toContainText('PACK-PTW-');
  await page.locator('text=Tường Chia Chọn Đa Đơn & Giỏ Nhặt Cụm').first().evaluate(el => {
    const card = el.closest('section, .ui-card') ?? el;
    card.scrollIntoView({ block: 'start' });
  });
  await page.waitForTimeout(200);

  const pickingOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(pickingOverflow1440, 'PickingTasks 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave59_e2e_2_epic30_1_picking_wave_ptw_1440x900.png'), fullPage: false });

  // =========================================================================
  // 3. EPIC 30.2: CARRIER WAYBILL BOOKING -> 100x150mm THERMAL PDF -> COD RECONCILIATION
  // =========================================================================
  const bookShp2Res = await request.post(`${API_BASE}/api/v1/shipments/book-carrier-waybill`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      shipmentId: 2,
      warehouseId: 1,
      carrierCode: 'GHN',
      recipientName: 'Trần Thị B',
      recipientPhone: '0912345678',
      recipientAddress: '456 Lê Lợi, Phường Bến Thành, Quận 1, TP. Hồ Chí Minh',
      province: 'TP. Hồ Chí Minh',
      district: 'Quận 1',
      ward: 'Phường Bến Thành',
      codAmount: 450000,
      weightGrams: 1500
    }
  });
  expect(bookShp2Res.status()).toBe(200);
  const bookShp2Body = await bookShp2Res.json();
  expect(bookShp2Body.trackingNumber).toContain('GHN-');
  expect(bookShp2Body.outboxMessageId).toBeGreaterThan(0);

  const batchPrintRes = await request.post(`${API_BASE}/api/v1/export-receipts/batch-print-awb`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      warehouseId: 1,
      exportReceiptIds: [177]
    }
  });
  expect(batchPrintRes.status()).toBe(200);
  const batchPrintBody = await batchPrintRes.json();
  const pdfText = Buffer.from(batchPrintBody.pdfBase64, 'base64').toString('latin1');
  expect(pdfText.startsWith('%PDF-1.4')).toBe(true);
  expect(pdfText).toContain('/MediaBox [0 0 283.46 425.20]');

  const carrierWebhookRes = await request.post(`${API_BASE}/api/v1/shipments/webhooks/carrier/GHN`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      eventId: `EVT-GHN-W59-DELIVERED-${Date.now()}`,
      trackingNumber: bookShp2Body.trackingNumber,
      shipmentId: 2,
      warehouseId: 1,
      carrierStatus: 'DELIVERED',
      eventType: 'DELIVERED',
      status: 'DELIVERED',
      actualCodCollected: 450000,
      actualShippingFee: 28000,
      epodImageUrl: 'https://cdn.ghn.vn/epod/shp-qa-002.jpg',
      epodSignedBy: 'Nguyễn Văn Nhận'
    }
  });
  expect(carrierWebhookRes.status()).toBe(200);

  await page.goto(`${FRONTEND_BASE}/shipments`, { waitUntil: 'networkidle' });
  await assertNo404Page('Shipments 1440x900');
  const shipmentsTable = page.locator('table[aria-label="Danh sách Shipment"]');
  await expect(shipmentsTable).toBeVisible({ timeout: 20_000 });
  await expect(shipmentsTable.getByText('Đã giao').first()).toBeVisible({ timeout: 15_000 });
  const shp2Button = shipmentsTable.getByRole('button', { name: 'SHP-QA-002' });
  if (await shp2Button.count() > 0) {
    await shp2Button.first().click();
  }

  const shipmentsOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(shipmentsOverflow1440, 'Shipments 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave59_e2e_3_epic30_2_shipments_carrier_1440x900.png'), fullPage: false });

  await page.goto(`${FRONTEND_BASE}/cod-reconciliation`, { waitUntil: 'networkidle' });
  await assertNo404Page('CodReconciliation 1440x900');
  const codOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(codOverflow1440, 'CodReconciliation 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);

  // =========================================================================
  // 4. EPIC 30.3: SHOPEE/TIKTOK OAUTH 2.0 -> BUFFERED STOCK PUSH -> BUYER CANCEL GATE (409)
  // =========================================================================
  await page.goto(`${FRONTEND_BASE}/outbound-demand`, { waitUntil: 'networkidle' });
  await assertNo404Page('OutboundDemand 1440x900');
  const outboundOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(outboundOverflow1440, 'OutboundDemand 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);

  const oauthRes = await request.post(`${API_BASE}/api/v1/marketplace-shops/oauth/exchange`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      warehouseId: 1,
      channelCode: 'SHOPEE',
      shopCode: 'SHOPEE-VN-W59-LIVE',
      shopName: 'Shopee Official Store W59',
      authCode: 'AUTH-CODE-SHOPEE-W59',
      reserveRatioPct: 80,
      safetyBufferQty: 2
    }
  });
  expect(oauthRes.status()).toBe(200);

  const stockPushRes = await request.post(`${API_BASE}/api/v1/sales-orders/marketplace-shops/sync-inventory`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      warehouseId: 1,
      channelCode: 'SHOPEE',
      bufferPercent: 80
    }
  });
  expect(stockPushRes.status()).toBe(200);

  const cancelWebhookRes = await request.post(`${API_BASE}/api/v1/sales-orders/webhook-buyer-cancellation`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': crypto.randomUUID()
    },
    data: {
      exportReceiptId: 179,
      channelCode: 'SHOPEE',
      eventId: `EVT-SHOPEE-CANCEL-W59-${Date.now()}`,
      reason: 'Khách hàng hủy đơn trên sàn Shopee khi đơn đã đóng gói'
    }
  });
  expect(cancelWebhookRes.status()).toBe(200);

  await page.goto(`${FRONTEND_BASE}/handover-manifests`, { waitUntil: 'networkidle' });
  await assertNo404Page('HandoverManifests 1440x900');
  const scanAwbInput = page.getByTestId('input-scan-awb');
  await expect(scanAwbInput).toBeVisible({ timeout: 20_000 });
  await scanAwbInput.fill('ER-QA-003');

  const scanAwbResPromise = page.waitForResponse(
    res => res.url().includes('/handover-manifests/scan-awb') && res.request().method() === 'POST',
    { timeout: 30_000 }
  );
  await page.getByTestId('btn-scan-awb').click();
  const handoverBlockRes = await scanAwbResPromise;
  expect(handoverBlockRes.status(), 'Scanning AWB of buyer-cancelled order at handover must return 409 Conflict').toBe(409);
  const handoverBlockBody = await handoverBlockRes.json();
  expect(handoverBlockBody.errorCode).toBe('ORDER_CANCELLED_PUTBACK_REQUIRED');

  const criticalAlert = page.getByTestId('handover-critical-alert');
  await expect(criticalAlert).toBeVisible({ timeout: 15_000 });
  await expect(criticalAlert).toContainText('409 ORDER_CANCELLED_PUTBACK_REQUIRED');

  const handoverOverflow1440 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(handoverOverflow1440, 'HandoverManifests 1440x900 must have 0px horizontal overflow').toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave59_e2e_4_epic30_3_marketplace_cancel_1440x900.png'), fullPage: false });

  // =========================================================================
  // 5. MOBILE PDA VIEWPORT (390x844) - 0px HORIZONTAL OVERFLOW VERIFICATION
  // =========================================================================
  await page.setViewportSize({ width: 390, height: 844 });
  for (const routePath of ['/dashboard', '/picking-tasks', '/shipments', '/outbound-demand', '/handover-manifests']) {
    await page.goto(`${FRONTEND_BASE}${routePath}`, { waitUntil: 'networkidle' });
    await assertNo404Page(`Mobile PDA 390x844 on ${routePath}`);
    const overflow390 = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow390, `Mobile PDA 390x844 on ${routePath} must have 0px horizontal overflow`).toBeLessThanOrEqual(0);
  }
  await page.getByTestId('input-scan-awb').fill('ER-QA-003');
  await page.getByTestId('btn-scan-awb').click();
  await expect(page.getByTestId('handover-critical-alert')).toBeVisible({ timeout: 15_000 });
  await page.screenshot({ path: path.join(ARTIFACT_DIR, 'wave59_e2e_5_mobile_pda_390x844.png'), fullPage: false });

  expect(pageErrors, 'Zero unhandled page runtime errors across E2E journey').toEqual([]);
  expect(server5xxErrors, 'Zero HTTP 5xx responses across E2E journey').toEqual([]);
});
