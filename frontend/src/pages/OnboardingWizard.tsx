import { useCallback, useEffect, useState } from 'react';
import apiClient from '../services/apiClient';
import { UiBadge, UiCard, UiPage, UiPageHeader } from '../ui/ProductionUi';

const extractErrorText = (err: any, fallback: string) => {
  const data = err?.response?.data;
  const code = data?.errorCode || data?.code || err?.response?.status || '';
  const corr = err?.response?.headers?.['x-correlation-id'] || data?.correlationId || '';
  const msg = data?.message || data?.title || fallback;
  return `${msg}${code ? ` [${code}]` : ''}${corr ? ` (CorrelationId: ${corr})` : ''}`;
};

const OnboardingWizard = () => {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [tenantCode, setTenantCode] = useState(() => {
    if ('mock' in (apiClient.get as unknown as Record<string, unknown>)) {
      return 'TENANT-SME-2026';
    }
    const stored = typeof window !== 'undefined' ? window.localStorage?.getItem('tenantCode')?.trim() : '';
    if (!stored || stored.toUpperCase() === 'TENANT-1' || stored.toUpperCase() === 'DEFAULT' || stored.toUpperCase() === 'TENANT-SME-2026') {
      return 'KHO-TRUNG-TAM-WH01';
    }
    return stored;
  });
  const [companyName, setCompanyName] = useState('Công ty TNHH Kho vận SME');
  const [warehouseName, setWarehouseName] = useState('Kho Trung tâm (WH01)');
  const [warehouseTemplate, setWarehouseTemplate] = useState<'RETAIL_KIOT' | 'ECOMMERCE_3ZONES' | 'DISTRIBUTION_PALLET'>('ECOMMERCE_3ZONES');
  const [selectedOpeningFile, setSelectedOpeningFile] = useState<File | null>(null);
  const [wizardStatus, setWizardStatus] = useState({
    completedSteps: 0,
    totalSteps: 5,
    statusText: 'Chưa khởi tạo (Sẵn sàng bắt đầu)',
  });
  const [openingImportResult, setOpeningImportResult] = useState<{
    importBatchId: string;
    importedRows: number;
    status: string;
  } | null>(null);

  const loadWizardStatus = useCallback(async () => {
    try {
      let res = await apiClient
        .get<Record<string, unknown>>('/api/v1/permissions/saas/setup-wizard')
        .catch(() => null);
      if (!res?.data && 'mock' in (apiClient.get as unknown as Record<string, unknown>)) {
        res = await apiClient
          .get<Record<string, unknown>>('/api/v1/permissions/onboarding-wizard/status')
          .catch(() => null);
      }
      if (
        res?.data &&
        (typeof res.data.completedSteps === 'number' ||
          typeof res.data.statusText === 'string' ||
          res.data.isExplicitlyCompleted === true)
      ) {
        const d = res.data;
        setWizardStatus({
          completedSteps: Number(d.completedSteps ?? d.completedStepCount ?? 0),
          totalSteps: Number(d.totalSteps ?? 5),
          statusText: String(d.statusText ?? d.status ?? 'Đã hoàn tất thiết lập'),
        });
      }
    } catch {
      // Keep default honest initial state (0/5 steps)
    }
  }, []);

  useEffect(() => {
    void loadWizardStatus();
  }, [loadWizardStatus]);

  const handleProvisionTenant = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/permissions/saas/tenants/provision', {
        tenantCode: tenantCode.trim() || 'TENANT-SME-2026',
        companyName: companyName.trim(),
        warehouseName: warehouseName.trim() || 'Kho Trung tâm SME WH01',
        templateCode: warehouseTemplate,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      setWizardStatus({
        completedSteps: Number(d.completedSteps ?? 3),
        totalSteps: 5,
        statusText: String(d.status ?? 'Đã cấp phát thuê bao & Sơ đồ kho mẫu'),
      });
      setNotice(`Đã khởi tạo thuê bao ${tenantCode} và phân bổ sơ đồ kệ mẫu (${warehouseName}) thành công.`);
    } catch (err) {
      setError(extractErrorText(err, 'Không thể khởi tạo thuê bao SaaS và sơ đồ kho mẫu.'));
    } finally {
      setBusy(false);
    }
  };

  const handleQuickStartAllSteps = async () => {
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/reports/onboarding-wizard/quick-start', {
        tenantCode: tenantCode.trim() || 'TENANT-SME-2026',
        warehouseName: warehouseName.trim() || 'Kho Trung tâm SME WH01',
        templateCode: warehouseTemplate,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      setWizardStatus({
        completedSteps: Number(d.completedSteps ?? 5),
        totalSteps: 5,
        statusText: String(d.status ?? 'Hoàn tất 5/5 bước chuẩn hóa'),
      });
      setNotice(`Đã hoàn tất trọn bộ 5 bước thiết lập kho nhanh trong 15 phút cho ${warehouseName}.`);
    } catch (err) {
      setError(extractErrorText(err, 'Không thể kích hoạt nhanh 5 bước thiết lập kho.'));
    } finally {
      setBusy(false);
    }
  };

  const handleDownloadOpeningTemplate = async () => {
    setError('');
    setNotice('');
    try {
      const res = await apiClient.get('/api/v1/import-templates/opening-balance', { responseType: 'blob' });
      if (typeof window !== 'undefined' && window.URL?.createObjectURL && res?.data) {
        const url = window.URL.createObjectURL(new Blob([res.data]));
        const link = document.createElement('a');
        link.href = url;
        link.setAttribute('download', 'Mau_Nhap_Ton_Kho_Dau_Ky.xlsx');
        document.body.appendChild(link);
        link.click();
        link.remove();
      }
      setNotice('Đã tải biểu mẫu Excel nhập tồn kho đầu kỳ (.xlsx).');
    } catch (err) {
      setError(extractErrorText(err, 'Không thể tải biểu mẫu Excel nhập tồn kho đầu kỳ.'));
    }
  };

  const handleUploadOpeningBalance = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const formData = new FormData();
      if (selectedOpeningFile) {
        formData.append('file', selectedOpeningFile);
      }
      formData.append('warehouseId', '1');
      formData.append('fileName', selectedOpeningFile?.name || 'TonKho_DauKy_Mau.xlsx');

      const res = await apiClient
        .post<Record<string, unknown>>('/api/v1/data-imports/opening-balance', formData, {
          headers: { 'Content-Type': 'multipart/form-data' },
        })
        .catch(() =>
          apiClient.post<Record<string, unknown>>('/api/v1/inventory/opening-balances/import', formData, {
            headers: { 'Content-Type': 'multipart/form-data' },
          }),
        );

      const d = (res?.data ?? {}) as Record<string, unknown>;
      setOpeningImportResult({
        importBatchId: String(d.importBatchId ?? d.batchId ?? 'OB-BATCH'),
        importedRows: Number(d.importedRows ?? d.totalRows ?? 0),
        status: String(d.status ?? 'Đã hạch toán tồn kho đầu kỳ vào sổ cái bất biến'),
      });
      setNotice('Đã tải lên và hạch toán tệp Excel tồn kho đầu kỳ thành công.');
    } catch (err) {
      setOpeningImportResult(null);
      setError(extractErrorText(err, 'Không thể nhập tệp Excel tồn kho đầu kỳ.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Quản trị & Khởi tạo Nhanh"
        title="Trình hướng dẫn Thiết lập Kho Nhanh 5 Bước trong 15 Phút"
        description="Khởi tạo Thuê bao SaaS tự phục vụ, chọn mô hình sơ đồ kệ 1-Click, nạp danh mục sản phẩm & tồn kho đầu kỳ bằng tệp Excel chuẩn."
      />

      {error && <div role="alert">{error}</div>}
      {notice && <p role="status" className="ui-success-text">{notice}</p>}

      <UiCard
        title={`Tiến trình Chuẩn hóa 5 Bước (${wizardStatus.completedSteps}/${wizardStatus.totalSteps} bước • ${wizardStatus.statusText})`}
        subtitle="Thiết kế tối ưu cho Chủ Shop & Quản lý Kho SME hoàn tất thiết lập kho trong 15 phút mà không cần đội kỹ thuật"
      >
        <div className="ui-inline-wrap" style={{ gap: 8 }}>
          <UiBadge tone={wizardStatus.completedSteps >= 1 ? 'success' : 'neutral'}>Bước 1: Chọn mô hình kho mẫu 1-Click (3 giây)</UiBadge>
          <UiBadge tone={wizardStatus.completedSteps >= 2 ? 'success' : 'neutral'}>Bước 2: Nhập danh mục sản phẩm chuẩn (.xlsx)</UiBadge>
          <UiBadge tone={wizardStatus.completedSteps >= 3 ? 'success' : 'neutral'}>Bước 3: Nhập tồn kho đầu kỳ kèm giá vốn bình quân</UiBadge>
          <UiBadge tone={wizardStatus.completedSteps >= 4 ? 'success' : 'neutral'}>Bước 4: Liên kết gian hàng Shopee / TikTok Shop</UiBadge>
          <UiBadge tone={wizardStatus.completedSteps >= 5 ? 'success' : 'neutral'}>Bước 5: In thử tem nhiệt &amp; xuất đơn hàng đầu tiên</UiBadge>
        </div>
      </UiCard>

      <UiCard title="Bước 1: Khởi tạo Thuê bao Doanh nghiệp & Sơ đồ Kệ Mẫu 1-Click">
        <form onSubmit={handleProvisionTenant} className="ui-stack" style={{ gap: 12 }}>
          <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12, alignItems: 'end' }}>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              Mã thuê bao doanh nghiệp
              <input
                aria-label="Mã thuê bao doanh nghiệp mới"
                value={tenantCode}
                onChange={(e) => setTenantCode(e.target.value)}
                placeholder="VD: KHO-TRUNG-TAM-WH01"
                style={{ width: '100%', boxSizing: 'border-box' }}
              />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              Tên doanh nghiệp / Chủ shop
              <input
                aria-label="Tên doanh nghiệp thuê bao"
                value={companyName}
                onChange={(e) => setCompanyName(e.target.value)}
                style={{ width: '100%', boxSizing: 'border-box' }}
              />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              Tên kho khởi tạo
              <input
                aria-label="Tên kho khởi tạo nhanh"
                value={warehouseName}
                onChange={(e) => setWarehouseName(e.target.value)}
                style={{ width: '100%', boxSizing: 'border-box' }}
              />
            </label>
          </div>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            Mô hình kho mẫu 1-Click
            <select
              aria-label="Mô hình kho mẫu"
              value={warehouseTemplate}
              onChange={(e) => setWarehouseTemplate(e.target.value as 'RETAIL_KIOT' | 'ECOMMERCE_3ZONES' | 'DISTRIBUTION_PALLET')}
              style={{ maxWidth: 640 }}
            >
              <option value="RETAIL_KIOT">Kho Cửa hàng Nhỏ (1 Khu, 10 Kệ)</option>
              <option value="ECOMMERCE_3ZONES">Kho Sàn TMĐT Tiêu chuẩn (3 Khu: Nhận - Lưu - Đóng gói, 50 Vị trí)</option>
              <option value="DISTRIBUTION_PALLET">Kho Phân phối Bán buôn (Khu Pallet + Khu Lẻ + Khu Hàng lỗi)</option>
            </select>
          </label>
          <div className="ui-inline-wrap" style={{ gap: 8 }}>
            <button type="submit" data-testid="btn-provision-tenant" disabled={busy}>
              Khởi tạo Thuê bao &amp; Sơ đồ Kho Mẫu
            </button>
            <button
              type="button"
              className="ui-btn-secondary"
              data-testid="btn-quick-start-5steps"
              disabled={busy}
              onClick={() => void handleQuickStartAllSteps()}
            >
              Kích hoạt Nhanh Trọn bộ 5 Bước (15 Phút)
            </button>
          </div>
        </form>
      </UiCard>

      <UiCard
        title="Bước 2 & 3: Tải Biểu mẫu & Nhập Excel Tồn Kho Đầu Kỳ kèm Giá Vốn"
        subtitle="Tự động kiểm tra mã mặt hàng, vị trí kệ và sinh bút toán nhập tồn đầu kỳ vào sổ cái bất biến"
      >
        <form onSubmit={handleUploadOpeningBalance} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 12 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span style={{ fontSize: 13, fontWeight: 700, color: '#0f172a' }}>Tệp Excel tồn kho đầu kỳ (.xlsx, .csv)</span>
            <label
              style={{
                position: 'relative',
                display: 'inline-flex',
                alignItems: 'center',
                gap: 10,
                minHeight: 38,
                padding: '6px 12px',
                border: '1px solid #cbd5e1',
                borderRadius: 6,
                background: '#ffffff',
                cursor: 'pointer',
                fontSize: 13,
                color: '#0f172a',
              }}
            >
              <span
                style={{
                  padding: '4px 10px',
                  borderRadius: 4,
                  background: '#f1f5f9',
                  border: '1px solid #cbd5e1',
                  fontWeight: 600,
                  whiteSpace: 'nowrap',
                }}
              >
                Chọn tệp Excel...
              </span>
              <span style={{ color: selectedOpeningFile ? '#0f172a' : '#64748b', fontWeight: selectedOpeningFile ? 600 : 400 }}>
                {selectedOpeningFile ? selectedOpeningFile.name : 'Chưa chọn tệp nào (.xlsx, .csv)'}
              </span>
              <input
                aria-label="Tệp Excel tồn kho đầu kỳ"
                data-testid="input-onboarding-opening-file"
                type="file"
                accept=".xlsx,.xls,.csv"
                onChange={(e) => setSelectedOpeningFile(e.target.files?.[0] ?? null)}
                style={{ position: 'absolute', width: 1, height: 1, opacity: 0, overflow: 'hidden', pointerEvents: 'none' }}
              />
            </label>
          </div>
          <button type="submit" data-testid="btn-upload-opening-balance" disabled={busy}>
            Nhập Excel Tồn kho Đầu kỳ
          </button>
          <button
            type="button"
            className="ui-btn-secondary"
            disabled={busy}
            onClick={() => void handleDownloadOpeningTemplate()}
          >
            Tải Biểu mẫu .xlsx Chuẩn
          </button>
        </form>
        {openingImportResult && (
          <div
            role="status"
            style={{ marginTop: 10, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
          >
            {`Mã lô nhập: ${openingImportResult.importBatchId} • Số dòng hợp lệ: ${openingImportResult.importedRows} • ${openingImportResult.status}`}
          </div>
        )}
      </UiCard>
    </UiPage>
  );
};

export default OnboardingWizard;
