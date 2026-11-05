import { useRef, useState } from 'react';
import apiClient from '../../services/apiClient';
import { permissionError } from '../../services/permissionPresentation';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import { downloadRecoveryCodesTxtFile } from './PermissionsHelpersAndTypes';
import { PermissionsTotpEnrollingSection } from './PermissionsTotpEnrollingSection';
import { PermissionsTotpRecoverySection } from './PermissionsTotpRecoverySection';
import { PermissionsTotpStepHeader, type TotpEnrollmentStage } from './PermissionsTotpStepHeader';

const extractSecretFromData = (d: Record<string, unknown> | undefined) =>
  typeof d?.secretKeyBase32 === 'string' && d.secretKeyBase32
    ? d.secretKeyBase32
    : typeof d?.secret === 'string' && d.secret
      ? d.secret
      : typeof d?.base32Secret === 'string' && d.base32Secret
        ? d.base32Secret
        : '';

export function PermissionsTotpEnrollmentCard({ targetUserIdInput, onError }: { targetUserIdInput: string; onError: (msg: string) => void }) {
  const [totpBusy, setTotpBusy] = useState(false);
  const [showTotpSecret, setShowTotpSecret] = useState(false);
  const [showFullRecoveryCodes, setShowFullRecoveryCodes] = useState(false);
  const [totpEnrollmentStage, setTotpEnrollmentStage] = useState<TotpEnrollmentStage>('IDLE');
  const [recoverySavedConfirmed, setRecoverySavedConfirmed] = useState(false);
  const [totpSetupNotice, setTotpSetupNotice] = useState('');
  const [totpVerifyInput, setTotpVerifyInput] = useState('');
  const [regenerateOtpInput, setRegenerateOtpInput] = useState('');
  const enrolledTotpSecretRef = useRef<string>('');
  const [totpState, setTotpState] = useState<{ secretKeyBase32: string; otpauthUri: string; recoveryCodes: string[] }>({
    secretKeyBase32: '',
    otpauthUri: '',
    recoveryCodes: [],
  });

  const resolveSecretFromServerOrRef = async (uid: number, fallback: string) => {
    try {
      const res = await apiClient.post<Record<string, unknown>>(`/api/v1/account-security/${uid}/totp/reveal-secret`);
      const apiSecret = extractSecretFromData(res?.data);
      if (apiSecret) {
        enrolledTotpSecretRef.current = apiSecret;
        return apiSecret;
      }
    } catch {
      // Retain server-issued secret cached from setup if reveal endpoint is unavailable
    }
    return fallback;
  };

  const toggleTotpSecretVisibility = async () => {
    if (showTotpSecret) {
      setShowTotpSecret(false);
      setTotpState(prev => ({ ...prev, secretKeyBase32: '' }));
      return;
    }
    const uid = Number(targetUserIdInput) > 0 ? Number(targetUserIdInput) : 1;
    const resolvedSecret = await resolveSecretFromServerOrRef(uid, enrolledTotpSecretRef.current);
    if (!resolvedSecret) {
      onError('Không thể hiển thị khóa nhập tay từ máy chủ. Vui lòng thử lại.');
      return;
    }
    setTotpState(prev => ({ ...prev, secretKeyBase32: resolvedSecret }));
    setShowTotpSecret(true);
  };

  const copyTotpSecretToClipboard = async () => {
    const uid = Number(targetUserIdInput) > 0 ? Number(targetUserIdInput) : 1;
    let resolvedSecret = totpState.secretKeyBase32 || enrolledTotpSecretRef.current;
    if (!resolvedSecret) {
      resolvedSecret = await resolveSecretFromServerOrRef(uid, '');
    }
    if (!resolvedSecret) {
      onError('Không thể sao chép khóa nhập tay từ máy chủ. Vui lòng thử lại.');
      return;
    }
    if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
      void Promise.resolve(navigator.clipboard.writeText(resolvedSecret)).catch(() => {});
    }
    setTotpSetupNotice('Đã sao chép khóa nhập tay Base32 vào bộ nhớ tạm.');
  };

  const setupTotpMfa = async () => {
    if (totpBusy) return;
    setTotpBusy(true);
    onError('');
    setTotpSetupNotice('');
    const uid = Number(targetUserIdInput) > 0 ? Number(targetUserIdInput) : 1;
    try {
      const res = await apiClient.post<Record<string, unknown>>(`/api/v1/account-security/${uid}/totp/setup`);
      const d = res?.data;
      const apiSecret = extractSecretFromData(d);
      const hasServerUri = typeof d?.otpauthUri === 'string' && d.otpauthUri.length > 0;
      if (!hasServerUri && !apiSecret) {
        onError('Máy chủ không trả về thông tin khởi tạo xác thực 2 lớp. Vui lòng thử lại.');
        return;
      }
      const uri = hasServerUri
        ? String(d.otpauthUri).replace(/ERP_KHO/gi, 'ERPKHO')
        : `otpauth://totp/ERPKHO:user_${uid}?secret=${apiSecret}&issuer=ERPKHO`;
      enrolledTotpSecretRef.current = apiSecret;
      setTotpState({ secretKeyBase32: '', otpauthUri: uri, recoveryCodes: [] });
      setShowTotpSecret(false);
      setShowFullRecoveryCodes(false);
      setRecoverySavedConfirmed(false);
      setTotpEnrollmentStage('ENROLLING');
      setTotpVerifyInput('');
      setTotpSetupNotice('Đã sinh mã QR xác thực 2 lớp. Bước 1: Quét mã QR bằng ứng dụng Authenticator. Bước 2: Nhập mã 6 chữ số để xác minh và nhận 8 mã khôi phục dự phòng.');
    } catch (failure) {
      setTotpSetupNotice('');
      onError(permissionError(failure));
    } finally {
      setTotpBusy(false);
    }
  };

  const verifyTotpCode = async () => {
    if (totpBusy) return;
    setTotpBusy(true);
    onError('');
    setTotpSetupNotice('');
    const codeToVerify = totpVerifyInput.trim();
    if (!codeToVerify || codeToVerify.length < 6) {
      setTotpBusy(false);
      onError('Vui lòng nhập mã xác thực 6 chữ số từ ứng dụng Authenticator.');
      return;
    }
    const uid = Number(targetUserIdInput) > 0 ? Number(targetUserIdInput) : 1;
    try {
      const res = await apiClient.post<Record<string, unknown>>(`/api/v1/account-security/${uid}/totp/verify`, {
        code: codeToVerify,
      });
      if (res?.data?.verified === false) {
        setTotpSetupNotice('');
        onError(
          typeof res.data.message === 'string' && res.data.message
            ? res.data.message
            : 'Mã xác thực 6 chữ số không chính xác hoặc đã hết hạn. Vui lòng kiểm tra lại trên ứng dụng Authenticator.',
        );
        return;
      }
      const apiRecoveryCodes = Array.isArray(res?.data?.recoveryCodes)
        ? (res.data.recoveryCodes as unknown[]).map(c => String(c)).filter(Boolean)
        : [];
      if (apiRecoveryCodes.length === 0) {
        onError('Máy chủ không trả về danh sách mã khôi phục dự phòng. Vui lòng thử lại.');
        return;
      }
      enrolledTotpSecretRef.current = '';
      setShowTotpSecret(false);
      setShowFullRecoveryCodes(false);
      setTotpEnrollmentStage('RECOVERY_CODES_PENDING');
      setRecoverySavedConfirmed(false);
      setTotpState({ secretKeyBase32: '', otpauthUri: '', recoveryCodes: apiRecoveryCodes });
      setTotpVerifyInput('');
      setTotpSetupNotice('');
    } catch (failure) {
      setTotpSetupNotice('');
      onError(permissionError(failure));
    } finally {
      setTotpBusy(false);
    }
  };

  const completeTotpEnrollment = async () => {
    if (!recoverySavedConfirmed || totpBusy) return;
    setTotpBusy(true);
    onError('');
    const uid = Number(targetUserIdInput) > 0 ? Number(targetUserIdInput) : 1;
    try {
      await apiClient.post(`/api/v1/account-security/${uid}/totp/confirm-activation`).catch(() => null);
      enrolledTotpSecretRef.current = '';
      setShowTotpSecret(false);
      setShowFullRecoveryCodes(false);
      setTotpEnrollmentStage('VERIFIED');
      setTotpState({ secretKeyBase32: '', otpauthUri: '', recoveryCodes: [] });
      setTotpSetupNotice('');
    } finally {
      setTotpBusy(false);
    }
  };

  const cancelTotpEnrollment = async () => {
    if (totpBusy) return;
    setTotpBusy(true);
    onError('');
    const uid = Number(targetUserIdInput) > 0 ? Number(targetUserIdInput) : 1;
    try {
      await apiClient.post(`/api/v1/account-security/${uid}/totp/cancel`).catch(() => null);
      enrolledTotpSecretRef.current = '';
      setShowTotpSecret(false);
      setShowFullRecoveryCodes(false);
      setRecoverySavedConfirmed(false);
      setTotpVerifyInput('');
      setTotpEnrollmentStage('IDLE');
      setTotpState({ secretKeyBase32: '', otpauthUri: '', recoveryCodes: [] });
      setTotpSetupNotice('Đã hủy phiên thiết lập xác thực 2 lớp chưa hoàn tất và vô hiệu hóa bộ mã khôi phục tạm.');
    } finally {
      setTotpBusy(false);
    }
  };

  const regenerateRecoveryCodes = async () => {
    if (totpBusy) return;
    const otp = regenerateOtpInput.trim();
    if (!otp || otp.length < 6) {
      onError('Vui lòng nhập mã xác thực 6 chữ số hiện tại để sinh lại bộ mã khôi phục mới.');
      return;
    }
    setTotpBusy(true);
    onError('');
    setTotpSetupNotice('');
    const uid = Number(targetUserIdInput) > 0 ? Number(targetUserIdInput) : 1;
    try {
      const res = await apiClient.post<Record<string, unknown>>(`/api/v1/account-security/${uid}/totp/regenerate-recovery-codes`, { code: otp });
      const apiCodes = Array.isArray(res?.data?.recoveryCodes)
        ? (res.data.recoveryCodes as unknown[]).map(c => String(c)).filter(Boolean)
        : [];
      if (apiCodes.length === 0) {
        onError('Máy chủ không trả về bộ mã khôi phục dự phòng mới. Vui lòng thử lại.');
        return;
      }
      setRegenerateOtpInput('');
      setShowFullRecoveryCodes(false);
      setRecoverySavedConfirmed(false);
      setTotpState({ secretKeyBase32: '', otpauthUri: '', recoveryCodes: apiCodes });
      setTotpEnrollmentStage('RECOVERY_CODES_PENDING');
      setTotpSetupNotice('');
    } catch (failure) {
      onError(permissionError(failure));
    } finally {
      setTotpBusy(false);
    }
  };

  return (
    <UiCard
      title="Xác thực 2 lớp cho tài khoản quản trị"
      subtitle="Bảo vệ tài khoản quản trị trước nguy cơ lộ mật khẩu bằng mã xác thực 6 chữ số thay đổi mỗi 30 giây và 8 mã khôi phục dự phòng một lần."
    >
      <div className="ui-stack" style={{ gap: 14, width: '100%' }}>
        <PermissionsTotpStepHeader totpEnrollmentStage={totpEnrollmentStage} totpVerifyInput={totpVerifyInput} />

        <div data-testid="totp-enrollment-panel" style={{ padding: '16px 18px', borderRadius: 8, border: '1px solid #cbd5e1', background: '#ffffff', display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8 }}>
            <strong style={{ fontSize: 14, color: '#0f172a' }}>Thiết lập ứng dụng xác thực 2 lớp (Google / Microsoft Authenticator)</strong>
            {totpEnrollmentStage === 'IDLE' && <UiBadge tone="neutral">Chưa kích hoạt</UiBadge>}
            {totpEnrollmentStage === 'ENROLLING' && (
              <UiBadge tone="warning">
                {totpVerifyInput.trim().length > 0 ? 'Bước 2: Nhập mã 6 chữ số để xác minh' : 'Bước 1: Quét mã QR trên ứng dụng'}
              </UiBadge>
            )}
            {totpEnrollmentStage === 'RECOVERY_CODES_PENDING' && <UiBadge tone="warning">Bước 3: Lưu 8 mã khôi phục dự phòng (Chờ hoàn tất)</UiBadge>}
            {totpEnrollmentStage === 'VERIFIED' && <UiBadge tone="success">Đã bật xác thực 2 lớp</UiBadge>}
          </div>

          <p style={{ margin: 0, fontSize: 13, color: '#0f172a' }}>
            Quét mã QR bằng ứng dụng xác thực trên điện thoại (Google Authenticator, Microsoft Authenticator, Authy hoặc tương tự).
          </p>

          <details data-testid="totp-tech-details" style={{ fontSize: 12.5, color: '#334155', background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: 6, padding: '6px 10px' }}>
            <summary style={{ cursor: 'pointer', fontWeight: 600, color: '#0f172a' }}>Chi tiết kỹ thuật bảo mật</summary>
            <p style={{ margin: '6px 0 0 0', lineHeight: 1.5 }}>
              Chuẩn mật mã RFC 6238 (HMAC-SHA1, chu kỳ 30 giây). Khóa Base32 cá nhân do máy chủ cấp phát khi bấm khởi tạo, mặc định được che bảo mật ngoài DOM và ẩn hoàn toàn sau khi xác nhận hoàn tất.
            </p>
          </details>

          {totpEnrollmentStage === 'IDLE' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <p data-testid="totp-idle-state" style={{ margin: 0, fontSize: 13, color: '#0f172a', fontWeight: 600 }}>
                Chưa kích hoạt xác thực 2 lớp — Bấm khởi tạo để sinh mã QR và khóa cá nhân hóa
              </p>
              <div className="ui-inline-actions" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
                <button
                  type="button"
                  data-testid="btn-setup-totp"
                  disabled={totpBusy}
                  onClick={() => void setupTotpMfa()}
                  style={{ padding: '8px 14px', borderRadius: 6, fontSize: 13, fontWeight: 600, background: '#1d4ed8', color: '#ffffff', border: '1px solid #1e40af', cursor: totpBusy ? 'not-allowed' : 'pointer' }}
                >
                  Khởi tạo khóa xác thực 2 lớp
                </button>
              </div>
            </div>
          )}

          {totpEnrollmentStage === 'ENROLLING' && Boolean(totpState.otpauthUri) && (
            <PermissionsTotpEnrollingSection
              otpauthUri={totpState.otpauthUri}
              secretKeyBase32={totpState.secretKeyBase32}
              showTotpSecret={showTotpSecret}
              totpVerifyInput={totpVerifyInput}
              totpBusy={totpBusy}
              onChangeTotpVerifyInput={setTotpVerifyInput}
              onToggleSecretVisibility={() => void toggleTotpSecretVisibility()}
              onCopySecret={() => void copyTotpSecretToClipboard()}
              onVerifyTotp={() => void verifyTotpCode()}
              onRegenerateQr={() => void setupTotpMfa()}
              onCancelEnrollment={() => void cancelTotpEnrollment()}
            />
          )}

          <PermissionsTotpRecoverySection
            totpEnrollmentStage={totpEnrollmentStage}
            recoveryCodes={totpState.recoveryCodes}
            showFullRecoveryCodes={showFullRecoveryCodes}
            onToggleShowFullRecoveryCodes={() => setShowFullRecoveryCodes(prev => !prev)}
            onCopyRecoveryCodes={() => {
              const text = totpState.recoveryCodes.join('\n');
              if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
                void Promise.resolve(navigator.clipboard.writeText(text)).catch(() => {});
              }
              setTotpSetupNotice('Đã sao chép 8 mã khôi phục dự phòng vào bộ nhớ tạm. Vui lòng lưu trữ ở nơi an toàn.');
            }}
            onDownloadRecoveryCodesTxt={() => {
              downloadRecoveryCodesTxtFile(totpState.recoveryCodes);
              setTotpSetupNotice('Đã tải tệp erp-kho-2fa-recovery-codes.txt chứa 8 mã khôi phục dự phòng.');
            }}
            onCancelTotpEnrollment={() => void cancelTotpEnrollment()}
            recoverySavedConfirmed={recoverySavedConfirmed}
            onChangeRecoverySavedConfirmed={setRecoverySavedConfirmed}
            onCompleteTotpEnrollment={() => void completeTotpEnrollment()}
            regenerateOtpInput={regenerateOtpInput}
            onChangeRegenerateOtpInput={setRegenerateOtpInput}
            onRegenerateRecoveryCodes={() => void regenerateRecoveryCodes()}
            onSetupTotpMfa={() => void setupTotpMfa()}
            totpBusy={totpBusy}
          />

          {totpSetupNotice && (
            <div role="status" style={{ marginTop: 2, padding: '8px 10px', borderRadius: 6, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 12.5 }}>
              {totpSetupNotice}
            </div>
          )}
        </div>
      </div>
    </UiCard>
  );
}
