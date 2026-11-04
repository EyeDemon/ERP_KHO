import React from 'react';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import { CANONICAL_WH01_LABEL, formatWarehouseLabel } from '../../utils/warehouseLabel';

export interface OnboardingWizardStepCardsProps {
  busy: boolean;
  wizardStatus: {
    completedSteps: number;
    totalSteps: number;
    statusText: string;
  };
  tenantCode: string;
  setTenantCode: (v: string) => void;
  companyName: string;
  setCompanyName: (v: string) => void;
  warehouseName: string;
  setWarehouseName: (v: string) => void;
  ownerUsername: string;
  setOwnerUsername: (v: string) => void;
  ownerPassword: string;
  setOwnerPassword: (v: string) => void;
  warehouseTemplate: 'RETAIL_KIOT' | 'ECOMMERCE_3ZONES' | 'DISTRIBUTION_PALLET';
  setWarehouseTemplate: (v: 'RETAIL_KIOT' | 'ECOMMERCE_3ZONES' | 'DISTRIBUTION_PALLET') => void;
  warehouses: Array<{ id: number; code?: string; name: string }>;
  openingWarehouseId: string;
  setOpeningWarehouseId: (v: string) => void;
  selectedOpeningFile: File | null;
  setSelectedOpeningFile: (f: File | null) => void;
  openingImportResult: {
    importBatchId: string;
    importedRows: number;
    status: string;
  } | null;
  handleProvisionTenant: (e: React.FormEvent) => void;
  handleQuickStartAllSteps: () => void;
  handleUploadOpeningBalance: (e: React.FormEvent) => void;
  handleDownloadOpeningTemplate: () => void;
}

export const OnboardingWizardStepCards: React.FC<OnboardingWizardStepCardsProps> = ({
  busy,
  wizardStatus,
  tenantCode,
  setTenantCode,
  companyName,
  setCompanyName,
  warehouseName,
  setWarehouseName,
  ownerUsername,
  setOwnerUsername,
  ownerPassword,
  setOwnerPassword,
  warehouseTemplate,
  setWarehouseTemplate,
  warehouses,
  openingWarehouseId,
  setOpeningWarehouseId,
  selectedOpeningFile,
  setSelectedOpeningFile,
  openingImportResult,
  handleProvisionTenant,
  handleQuickStartAllSteps,
  handleUploadOpeningBalance,
  handleDownloadOpeningTemplate,
}) => {
  return (
    <>
      <UiCard
        title={`Tiến trình Chuẩn hóa 5 Bước (${wizardStatus.completedSteps}/${wizardStatus.totalSteps} bước • ${wizardStatus.statusText})`}
        subtitle="Thiết kế tối ưu cho Chủ Shop & Quản lý Kho SME hoàn tất thiết lập kho trong 15 phút mà không cần đội kỹ thuật"
      >
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(5, minmax(0, 1fr))',
            gap: 10,
            alignItems: 'stretch',
            width: '100%',
          }}
        >
          {[
            { step: 1, text: 'Bước 1: Chọn mô hình kho mẫu 1-Click (3 giây)' },
            { step: 2, text: 'Bước 2: Nhập danh mục sản phẩm chuẩn (.xlsx)' },
            { step: 3, text: 'Bước 3: Nhập tồn kho đầu kỳ kèm giá vốn bình quân' },
            { step: 4, text: 'Bước 4: Liên kết gian hàng Shopee / TikTok Shop' },
            { step: 5, text: 'Bước 5: In thử tem nhiệt & xuất đơn hàng đầu tiên' },
          ].map((item) => (
            <div
              key={item.step}
              style={{
                padding: '10px 12px',
                borderRadius: 8,
                border: wizardStatus.completedSteps >= item.step ? '1px solid #86efac' : '1px solid #cbd5e1',
                background: wizardStatus.completedSteps >= item.step ? '#f0fdf4' : '#f8fafc',
                display: 'flex',
                flexDirection: 'column',
                justifyContent: 'space-between',
                gap: 6,
                boxSizing: 'border-box',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <UiBadge tone={wizardStatus.completedSteps >= item.step ? 'success' : 'neutral'}>
                  {wizardStatus.completedSteps >= item.step ? 'Hoàn tất' : `Bước ${item.step}/5`}
                </UiBadge>
              </div>
              <span style={{ fontSize: 12.5, fontWeight: 600, color: '#0f172a', lineHeight: 1.4, whiteSpace: 'normal', wordBreak: 'break-word' }}>
                {item.text}
              </span>
            </div>
          ))}
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
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              Tài khoản chủ kho
              <input
                aria-label="Tài khoản chủ kho khởi tạo"
                value={ownerUsername}
                onChange={(e) => setOwnerUsername(e.target.value)}
                placeholder="Tùy chọn (mặc định tự sinh)"
                style={{ width: '100%', boxSizing: 'border-box' }}
              />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              Mật khẩu chủ kho
              <input
                aria-label="Mật khẩu chủ kho khởi tạo"
                type="password"
                value={ownerPassword}
                onChange={(e) => setOwnerPassword(e.target.value)}
                placeholder="Tùy chọn (mặc định chuẩn bảo mật)"
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
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, minWidth: 220 }}>
            <span style={{ fontSize: 13, fontWeight: 700, color: '#0f172a' }}>Kho nhập tồn đầu kỳ</span>
            <select
              aria-label="Kho nhập tồn đầu kỳ"
              value={openingWarehouseId}
              onChange={(e) => setOpeningWarehouseId(e.target.value)}
              style={{ minHeight: 38 }}
            >
              {warehouses.length > 0 ? (
                warehouses.map((w) => (
                  <option key={w.id} value={String(w.id)}>
                    {formatWarehouseLabel(w.name, w.code, w.id)}
                  </option>
                ))
              ) : (
                <option value="1">{formatWarehouseLabel(warehouseName) || CANONICAL_WH01_LABEL}</option>
              )}
            </select>
          </label>
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
              <span style={{ color: selectedOpeningFile ? '#0f172a' : '#475569', fontWeight: selectedOpeningFile ? 600 : 400 }}>
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
    </>
  );
};
