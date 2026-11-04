import React from 'react';

export interface SaasBillingTaxProfileCardProps {
  busy: boolean;
  taxCodeDraft: string;
  setTaxCodeDraft: (v: string) => void;
  buyerCompanyName: string;
  setBuyerCompanyName: (v: string) => void;
  buyerTaxpayerType: 'ENTERPRISE' | 'HOUSEHOLD_INDIVIDUAL' | 'INDIVIDUAL_CONSUMER';
  setBuyerTaxpayerType: (v: 'ENTERPRISE' | 'HOUSEHOLD_INDIVIDUAL' | 'INDIVIDUAL_CONSUMER') => void;
  legalRegisteredAddress: string;
  setLegalRegisteredAddress: (v: string) => void;
  onSubmit: (e: React.FormEvent) => void;
}

export const SaasBillingTaxProfileCard: React.FC<SaasBillingTaxProfileCardProps> = ({
  busy,
  taxCodeDraft,
  setTaxCodeDraft,
  buyerCompanyName,
  setBuyerCompanyName,
  buyerTaxpayerType,
  setBuyerTaxpayerType,
  legalRegisteredAddress,
  setLegalRegisteredAddress,
  onSubmit,
}) => {
  return (
    <section className="ui-card">
      <form onSubmit={onSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 6, margin: 0 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
          <div>
            <h2 className="ui-card-title" style={{ margin: 0, fontSize: 14 }}>
              Hồ sơ Thuế Doanh nghiệp Thuê bao (Thông tư 86/2024/TT-BTC)
            </h2>
            <p className="ui-muted-text" style={{ margin: 0, fontSize: 11.5, lineHeight: 1.25 }}>
              Cập nhật Mã số thuế (10/13 số) hoặc CCCD (12 số) để xuất hóa đơn GTGT dịch vụ SaaS
            </p>
          </div>
          <button
            type="submit"
            data-testid="btn-save-saas-tax-profile"
            disabled={busy}
            style={{ whiteSpace: 'nowrap', fontWeight: 600, minHeight: 30, padding: '4px 10px', fontSize: 12, flexShrink: 0 }}
          >
            Lưu Hồ sơ Thuế
          </button>
        </div>
        <div
          className="saas-tax-profile-grid"
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
            gap: '6px 10px',
            alignItems: 'end',
          }}
        >
          <label className="ui-stack" style={{ gap: 2 }}>
            <span style={{ fontSize: 11.5, fontWeight: 600 }}>Mã số thuế / CCCD (TT 86/2024/TT-BTC)</span>
            <input
              aria-label="Mã số thuế người mua SaaS"
              data-testid="input-saas-buyer-tax-code"
              value={taxCodeDraft}
              onChange={(e) => setTaxCodeDraft(e.target.value)}
              placeholder="10/13 số hoặc 12 số CCCD"
            />
          </label>
          <label className="ui-stack" style={{ gap: 2 }}>
            <span style={{ fontSize: 11.5, fontWeight: 600 }}>Phân loại người nộp thuế</span>
            <select
              aria-label="Phân loại người nộp thuế SaaS"
              value={buyerTaxpayerType}
              onChange={(e) => setBuyerTaxpayerType(e.target.value as 'ENTERPRISE' | 'HOUSEHOLD_INDIVIDUAL' | 'INDIVIDUAL_CONSUMER')}
            >
              <option value="ENTERPRISE">Doanh nghiệp / Tổ chức (MST 10 hoặc 13 số)</option>
              <option value="HOUSEHOLD_INDIVIDUAL">Hộ / Cá nhân kinh doanh (CCCD 12 số)</option>
              <option value="INDIVIDUAL_CONSUMER">Cá nhân tiêu dùng</option>
            </select>
          </label>
          <label className="ui-stack" style={{ gap: 2 }}>
            <span style={{ fontSize: 11.5, fontWeight: 600 }}>Tên đơn vị / Chủ hộ thuê bao</span>
            <input
              aria-label="Tên đơn vị mua SaaS"
              data-testid="input-saas-buyer-company-name"
              value={buyerCompanyName}
              onChange={(e) => setBuyerCompanyName(e.target.value)}
              placeholder="Tên công ty / hộ kinh doanh"
            />
          </label>
          <label className="ui-stack" style={{ gap: 2 }}>
            <span style={{ fontSize: 11.5, fontWeight: 600 }}>Địa chỉ đăng ký thuế hợp pháp</span>
            <input
              aria-label="Địa chỉ đăng ký thuế hợp pháp"
              value={legalRegisteredAddress}
              onChange={(e) => setLegalRegisteredAddress(e.target.value)}
              placeholder="Địa chỉ trụ sở theo ĐKKD / CCCD"
            />
          </label>
        </div>
      </form>
    </section>
  );
};
