import { BarcodeSvg } from '../../components/BarcodeSvg';
import { formatBase32Groups } from './PermissionsHelpersAndTypes';

interface PermissionsTotpEnrollingSectionProps {
  otpauthUri: string;
  secretKeyBase32: string;
  showTotpSecret: boolean;
  totpVerifyInput: string;
  totpBusy: boolean;
  onChangeTotpVerifyInput: (val: string) => void;
  onToggleSecretVisibility: () => void;
  onCopySecret: () => void;
  onVerifyTotp: () => void;
  onRegenerateQr: () => void;
  onCancelEnrollment: () => void;
}

const secBtnStyle = {
  fontSize: 12,
  padding: '4px 10px',
  minHeight: 30,
  background: '#ffffff',
  border: '1px solid #94a3b8',
  color: '#0f172a',
  fontWeight: 600,
  borderRadius: 6,
  cursor: 'pointer',
} as const;

export function PermissionsTotpEnrollingSection({
  otpauthUri,
  secretKeyBase32,
  showTotpSecret,
  totpVerifyInput,
  totpBusy,
  onChangeTotpVerifyInput,
  onToggleSecretVisibility,
  onCopySecret,
  onVerifyTotp,
  onRegenerateQr,
  onCancelEnrollment,
}: PermissionsTotpEnrollingSectionProps) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 16, padding: '12px 14px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1' }}>
        {otpauthUri && (
          <div data-testid="totp-qr-container" style={{ padding: 10, borderRadius: 8, border: '1px solid #e2e8f0', background: '#ffffff', flex: '0 0 auto' }}>
            <BarcodeSvg value={otpauthUri} format="QR" height={180} showQr={true} qrCaption="Mã QR TOTP" captionLabel="Mã QR TOTP" />
          </div>
        )}

        <div data-testid="totp-enrolling-secret-box" style={{ flex: '1 1 340px', display: 'flex', flexDirection: 'column', gap: 10 }}>
          <div style={{ fontSize: 13, color: '#0f172a', fontWeight: 600 }}>
            Bước 1: Mở ứng dụng Google / Microsoft Authenticator trên điện thoại và quét mã QR bên cạnh.
          </div>
          <div style={{ fontSize: 12.5, color: '#334155' }}>
            Nếu không quét được camera, bạn có thể xem hoặc sao chép khóa nhập tay dưới đây:
          </div>
          <div style={{ padding: '8px 10px', borderRadius: 6, background: '#ffffff', border: '1px solid #cbd5e1', display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
            <code data-testid="totp-secret-code" style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace', fontWeight: 700, fontSize: 13, color: '#1e40af', letterSpacing: '0.03em' }}>
              {showTotpSecret && secretKeyBase32 ? formatBase32Groups(secretKeyBase32) : '•••• •••• •••• •••• •••• •••• •••• ••••'}
            </code>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
              <button type="button" className="ui-btn-secondary" data-testid="toggle-totp-secret-visibility" onClick={onToggleSecretVisibility} style={secBtnStyle}>
                {showTotpSecret ? 'Ẩn khóa nhập tay' : 'Hiện khóa nhập tay'}
              </button>
              <button type="button" className="ui-btn-secondary" data-testid="btn-copy-totp-secret" onClick={onCopySecret} style={secBtnStyle}>
                Sao chép khóa
              </button>
            </div>
          </div>
        </div>
      </div>

      <div style={{ padding: '12px 14px', borderRadius: 8, border: '1px solid #bfdbfe', background: '#eff6ff', display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
        <label htmlFor="totp-otp-input" style={{ fontSize: 13, fontWeight: 700, color: '#0f172a', margin: 0 }}>
          Bước 2: Nhập mã 6 chữ số từ ứng dụng để xác minh:
        </label>
        <div className="ui-inline-actions" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
          <input
            id="totp-otp-input"
            aria-label="Mã xác thực 6 chữ số"
            placeholder="Nhập 6 chữ số OTP"
            value={totpVerifyInput}
            onChange={e => onChangeTotpVerifyInput(e.target.value)}
            maxLength={6}
            style={{ height: 36, minHeight: 36, width: 170, padding: '4px 10px', boxSizing: 'border-box', fontVariantNumeric: 'tabular-nums', background: '#ffffff', border: '1px solid #94a3b8', borderRadius: 6 }}
          />
          <button
            type="button"
            data-testid="btn-verify-totp"
            disabled={totpBusy}
            onClick={onVerifyTotp}
            style={{ height: 36, padding: '4px 14px', borderRadius: 6, fontSize: 13, fontWeight: 700, background: '#1d4ed8', color: '#ffffff', border: '1px solid #1e40af', cursor: totpBusy ? 'not-allowed' : 'pointer' }}
          >
            Xác minh mã 6 chữ số
          </button>
          <button
            type="button"
            className="ui-btn-secondary"
            data-testid="btn-setup-totp"
            disabled={totpBusy}
            onClick={onRegenerateQr}
            style={{ height: 36, padding: '4px 12px', borderRadius: 6, fontSize: 12.5, fontWeight: 600, background: '#ffffff', color: '#0f172a', border: '1px solid #94a3b8', cursor: totpBusy ? 'not-allowed' : 'pointer' }}
          >
            Sinh lại mã QR mới
          </button>
          <button
            type="button"
            className="ui-btn-secondary"
            data-testid="btn-cancel-totp-enrollment"
            disabled={totpBusy}
            onClick={onCancelEnrollment}
            style={{ height: 36, padding: '4px 12px', borderRadius: 6, fontSize: 12.5, fontWeight: 600, background: '#ffffff', color: '#475569', border: '1px solid #cbd5e1', cursor: totpBusy ? 'not-allowed' : 'pointer' }}
          >
            Hủy thiết lập
          </button>
        </div>
      </div>
    </div>
  );
}
