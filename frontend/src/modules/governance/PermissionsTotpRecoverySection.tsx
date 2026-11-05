import { AlertTriangle, CheckCircle2 } from 'lucide-react';
import { UiBadge } from '../../ui/ProductionUi';
import { maskRecoveryCodeDisplay } from './PermissionsHelpersAndTypes';
import type { TotpEnrollmentStage } from './PermissionsTotpStepHeader';

export function PermissionsTotpRecoverySection({
  totpEnrollmentStage,
  recoveryCodes,
  showFullRecoveryCodes,
  onToggleShowFullRecoveryCodes,
  onCopyRecoveryCodes,
  onDownloadRecoveryCodesTxt,
  onCancelTotpEnrollment,
  recoverySavedConfirmed,
  onChangeRecoverySavedConfirmed,
  onCompleteTotpEnrollment,
  regenerateOtpInput,
  onChangeRegenerateOtpInput,
  onRegenerateRecoveryCodes,
  onSetupTotpMfa,
  totpBusy,
}: {
  totpEnrollmentStage: TotpEnrollmentStage;
  recoveryCodes: string[];
  showFullRecoveryCodes: boolean;
  onToggleShowFullRecoveryCodes: () => void;
  onCopyRecoveryCodes: () => void;
  onDownloadRecoveryCodesTxt: () => void;
  onCancelTotpEnrollment: () => void;
  recoverySavedConfirmed: boolean;
  onChangeRecoverySavedConfirmed: (checked: boolean) => void;
  onCompleteTotpEnrollment: () => void;
  regenerateOtpInput: string;
  onChangeRegenerateOtpInput: (val: string) => void;
  onRegenerateRecoveryCodes: () => void;
  onSetupTotpMfa: () => void;
  totpBusy: boolean;
}) {
  return (
    <>
      {(totpEnrollmentStage === 'RECOVERY_CODES_PENDING' || totpEnrollmentStage === 'VERIFIED') && (
        <div
          data-testid={totpEnrollmentStage === 'VERIFIED' ? 'totp-verified-banner' : 'totp-pending-recovery-banner'}
          role="status"
          style={{
            margin: 0,
            padding: '10px 14px',
            borderRadius: 8,
            background: totpEnrollmentStage === 'VERIFIED' ? '#ecfdf5' : '#fffbeb',
            border: `1px solid ${totpEnrollmentStage === 'VERIFIED' ? '#10b981' : '#f59e0b'}`,
            color: totpEnrollmentStage === 'VERIFIED' ? '#065f46' : '#78350f',
            fontSize: 13,
            fontWeight: 600,
            display: 'flex',
            alignItems: 'flex-start',
            gap: 8,
          }}
        >
          {totpEnrollmentStage === 'VERIFIED' ? (
            <>
              <CheckCircle2 size={16} aria-hidden="true" style={{ flexShrink: 0, marginTop: 1 }} />
              <span>Đã bật xác thực 2 lớp. Khóa cấu hình và mã khôi phục đã được ẩn an toàn.</span>
            </>
          ) : (
            <>
              <AlertTriangle size={16} aria-hidden="true" style={{ flexShrink: 0, marginTop: 1 }} />
              <span>Bạn đã xác minh mã OTP 6 chữ số thành công. Vui lòng sao chép hoặc tải 8 mã khôi phục dự phòng và bấm Hoàn tất để kích hoạt xác thực 2 lớp.</span>
            </>
          )}
        </div>
      )}

      {totpEnrollmentStage === 'RECOVERY_CODES_PENDING' && recoveryCodes.length > 0 && (
        <div
          data-testid="totp-recovery-codes-panel"
          style={{
            padding: '14px 16px',
            borderRadius: 8,
            border: '1px solid #94a3b8',
            background: '#f8fafc',
            display: 'flex',
            flexDirection: 'column',
            gap: 12,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8 }}>
            <strong style={{ fontSize: 14, color: '#0f172a' }}>
              Bước 3: Lưu 8 mã khôi phục dự phòng (hiển thị duy nhất 1 lần sau khi xác minh OTP)
            </strong>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <button
                type="button"
                data-testid="btn-toggle-recovery-codes-mask"
                onClick={onToggleShowFullRecoveryCodes}
                style={{
                  padding: '4px 10px',
                  borderRadius: 6,
                  fontSize: 12,
                  fontWeight: 600,
                  background: '#ffffff',
                  border: '1px solid #94a3b8',
                  color: '#0f172a',
                  cursor: 'pointer',
                }}
              >
                {showFullRecoveryCodes ? 'Ẩn mã' : 'Hiện đủ 8 mã'}
              </button>
              <UiBadge tone="info">{`${recoveryCodes.length} mã dự phòng hiệu lực`}</UiBadge>
            </div>
          </div>

          <p style={{ margin: 0, fontSize: 12.5, color: '#334155', lineHeight: 1.5 }}>
            Mỗi mã định dạng XXXX-XXXX chỉ dùng 1 lần để đăng nhập khẩn cấp khi mất điện thoại (mặc định ẩn 4 ký tự cuối; hãy sao chép hoặc tải tệp .txt trước khi hoàn tất).
          </p>

          <div
            data-testid="totp-recovery-codes"
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(4, minmax(110px, 1fr))',
              gap: 8,
            }}
          >
            {recoveryCodes.map((code, idx) => (
              <code
                key={`rec-code-${idx}`}
                style={{
                  padding: '8px 10px',
                  borderRadius: 6,
                  border: '1px solid #94a3b8',
                  background: '#ffffff',
                  color: '#0f172a',
                  fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace',
                  fontSize: 13,
                  fontWeight: 700,
                  textAlign: 'center',
                }}
              >
                {showFullRecoveryCodes ? code : maskRecoveryCodeDisplay(code)}
              </code>
            ))}
          </div>

          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
              <button
                type="button"
                data-testid="btn-copy-recovery-codes"
                onClick={onCopyRecoveryCodes}
                style={{
                  padding: '6px 12px',
                  borderRadius: 6,
                  fontSize: 12.5,
                  fontWeight: 600,
                  background: '#ffffff',
                  border: '1px solid #94a3b8',
                  color: '#0f172a',
                  cursor: 'pointer',
                }}
              >
                Sao chép 8 mã khôi phục
              </button>
              <button
                type="button"
                data-testid="btn-download-recovery-codes"
                onClick={onDownloadRecoveryCodesTxt}
                style={{
                  padding: '6px 12px',
                  borderRadius: 6,
                  fontSize: 12.5,
                  fontWeight: 600,
                  background: '#ffffff',
                  border: '1px solid #94a3b8',
                  color: '#0f172a',
                  cursor: 'pointer',
                }}
              >
                Tải tệp .txt
              </button>
              <button
                type="button"
                data-testid="btn-cancel-totp-enrollment"
                disabled={totpBusy}
                onClick={onCancelTotpEnrollment}
                style={{
                  padding: '6px 12px',
                  borderRadius: 6,
                  fontSize: 12.5,
                  fontWeight: 600,
                  background: '#ffffff',
                  border: '1px solid #cbd5e1',
                  color: '#475569',
                  cursor: totpBusy ? 'not-allowed' : 'pointer',
                }}
              >
                Hủy thiết lập
              </button>
            </div>

            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 10 }}>
              <label
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 6,
                  fontSize: 12.5,
                  fontWeight: 600,
                  color: '#0f172a',
                  cursor: 'pointer',
                }}
              >
                <input
                  type="checkbox"
                  data-testid="checkbox-confirm-recovery-saved"
                  checked={recoverySavedConfirmed}
                  onChange={e => onChangeRecoverySavedConfirmed(e.target.checked)}
                />
                Tôi đã lưu 8 mã khôi phục dự phòng ở nơi an toàn
              </label>
              <button
                type="button"
                data-testid="btn-complete-totp-enrollment"
                disabled={!recoverySavedConfirmed || totpBusy}
                onClick={onCompleteTotpEnrollment}
                style={{
                  padding: '7px 14px',
                  borderRadius: 6,
                  fontSize: 12.5,
                  fontWeight: 700,
                  background: recoverySavedConfirmed ? '#1d4ed8' : '#cbd5e1',
                  color: recoverySavedConfirmed ? '#ffffff' : '#475569',
                  border: `1px solid ${recoverySavedConfirmed ? '#1e40af' : '#94a3b8'}`,
                  cursor: recoverySavedConfirmed && !totpBusy ? 'pointer' : 'not-allowed',
                }}
              >
                Hoàn tất kích hoạt xác thực 2 lớp
              </button>
            </div>
          </div>
        </div>
      )}

      {totpEnrollmentStage === 'VERIFIED' && (
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 10,
            padding: '12px 14px',
            borderRadius: 8,
            background: '#f8fafc',
            border: '1px solid #cbd5e1',
          }}
        >
          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
            <span style={{ fontSize: 12.5, color: '#334155' }}>
              8 mã khôi phục dự phòng đã được xác nhận lưu trữ và ẩn khỏi màn hình. Nếu cần cấp lại bộ 8 mã mới (thu hồi bộ cũ), vui lòng nhập mã OTP 6 chữ số hiện tại:
            </span>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
              <input
                aria-label="Mã OTP 6 chữ số hiện tại"
                data-testid="input-regenerate-recovery-otp"
                placeholder="Nhập 6 số OTP hiện tại"
                value={regenerateOtpInput}
                onChange={e => onChangeRegenerateOtpInput(e.target.value)}
                maxLength={6}
                style={{
                  height: 34,
                  width: 165,
                  padding: '4px 10px',
                  borderRadius: 6,
                  border: '1px solid #94a3b8',
                  background: '#ffffff',
                  fontVariantNumeric: 'tabular-nums',
                  fontSize: 12.5,
                }}
              />
              <button
                type="button"
                data-testid="btn-regenerate-recovery-codes"
                disabled={totpBusy}
                onClick={onRegenerateRecoveryCodes}
                style={{
                  padding: '6px 12px',
                  borderRadius: 6,
                  fontSize: 12.5,
                  fontWeight: 600,
                  background: '#ffffff',
                  color: '#1d4ed8',
                  border: '1.5px solid #1d4ed8',
                  cursor: totpBusy ? 'not-allowed' : 'pointer',
                }}
              >
                Sinh lại bộ mã khôi phục mới
              </button>
              <button
                type="button"
                data-testid="btn-setup-totp"
                disabled={totpBusy}
                onClick={onSetupTotpMfa}
                style={{
                  padding: '6px 12px',
                  borderRadius: 6,
                  fontSize: 12.5,
                  fontWeight: 600,
                  background: '#ffffff',
                  color: '#0f172a',
                  border: '1px solid #94a3b8',
                  cursor: totpBusy ? 'not-allowed' : 'pointer',
                }}
              >
                Thiết lập lại khóa xác thực 2 lớp mới
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
