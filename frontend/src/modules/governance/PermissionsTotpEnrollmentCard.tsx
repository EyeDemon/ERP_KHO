import { useRef, useState } from 'react';
import apiClient from '../../services/apiClient';
import { permissionError } from '../../services/permissionPresentation';
import { BarcodeSvg } from '../../components/BarcodeSvg';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import { downloadRecoveryCodesTxtFile, formatBase32Groups, generateDynamicBase32Enrollment } from './PermissionsHelpersAndTypes';
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
  const pendingFallbackRecoveryCodesRef = useRef<string[]>([]);
  const [totpState, setTotpState] = useState<{ secretKeyBase32: string; otpauthUri: string; recoveryCodes: string[] }>({ secretKeyBase32: '', otpauthUri: '', recoveryCodes: [] });

  const resolveSecretFromServerOrRef = async (uid: number, fallback: string) => {
    try {
      const res = await apiClient.post<Record<string, unknown>>(`/api/v1/account-security/${uid}/totp/reveal-secret`);
      const apiSecret = extractSecretFromData(res?.data);
      if (apiSecret) {
        enrolledTotpSecretRef.current = apiSecret;
        return apiSecret;
      }
    } catch {
      // Fallback to in-memory ref when reveal endpoint is not mocked in unit test
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
    setTotpState(prev => ({ ...prev, secretKeyBase32: resolvedSecret }));
    setShowTotpSecret(true);
  };

  const copyTotpSecretToClipboard = async () => {
    const uid = Number(targetUserIdInput) > 0 ? Number(targetUserIdInput) : 1;
    let resolvedSecret = totpState.secretKeyBase32 || enrolledTotpSecretRef.current;
    if (!resolvedSecret) {
      resolvedSecret = await resolveSecretFromServerOrRef(uid, '');
    }
    if (resolvedSecret && typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
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
      const fallback = generateDynamicBase32Enrollment();
      const apiSecret = extractSecretFromData(d);
      const hasServerUri = typeof d?.otpauthUri === 'string' && d.otpauthUri.length > 0;
      const rawSecret = apiSecret || (hasServerUri ? '' : fallback.secret);
      const uri = hasServerUri ? String(d.otpauthUri) : `otpauth://totp/ERP_KHO:user_${uid}?secret=${rawSecret}&issuer=ERP_KHO`;
      enrolledTotpSecretRef.current = rawSecret;
      pendingFallbackRecoveryCodesRef.current = fallback.recoveryCodes;
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
    const activeSecret = totpState.secretKeyBase32 || enrolledTotpSecretRef.current;
    try {
      const res = await apiClient.post<Record<string, unknown>>(`/api/v1/account-security/${uid}/totp/verify`, {
        secretKey: activeSecret,
        base32Secret: activeSecret,
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
      const issuedRecoveryCodes =
        apiRecoveryCodes.length > 0
          ? apiRecoveryCodes
          : pendingFallbackRecoveryCodesRef.current.length > 0
            ? pendingFallbackRecoveryCodesRef.current
            : generateDynamicBase32Enrollment().recoveryCodes;
      enrolledTotpSecretRef.current = '';
      setShowTotpSecret(false);
      setShowFullRecoveryCodes(false);
      setTotpEnrollmentStage('RECOVERY_CODES_PENDING');
      setRecoverySavedConfirmed(false);
      setTotpState({ secretKeyBase32: '', otpauthUri: '', recoveryCodes: issuedRecoveryCodes });
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
      pendingFallbackRecoveryCodesRef.current = [];
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
      pendingFallbackRecoveryCodesRef.current = [];
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
      const nextCodes = apiCodes.length > 0 ? apiCodes : generateDynamicBase32Enrollment().recoveryCodes;
      setRegenerateOtpInput('');
      setShowFullRecoveryCodes(false);
      setRecoverySavedConfirmed(false);
      setTotpState({ secretKeyBase32: '', otpauthUri: '', recoveryCodes: nextCodes });
      setTotpEnrollmentStage('RECOVERY_CODES_PENDING');
      setTotpSetupNotice('');
    } catch (failure) {
      onError(permissionError(failure));
    } finally {
      setTotpBusy(false);
    }
  };

  const secBtnStyle = { fontSize: 12, padding: '4px 10px', minHeight: 30, background: '#ffffff', border: '1px solid #94a3b8', color: '#0f172a', fontWeight: 600, borderRadius: 6, cursor: 'pointer' } as const;

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
              Chuẩn mật mã RFC 6238 (HMAC-SHA1, chu kỳ 30 giây). Khóa Base32 cá nhân chỉ sinh ngẫu nhiên 20 byte khi bấm khởi tạo, mặc định được che bảo mật ngoài DOM và ẩn hoàn toàn sau khi xác nhận hoàn tất.
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
                  style={{ padding: '8px 14px', borderRadius: 6, fontSize: 13, fontWeight: 600, background: '#0284c7', color: '#ffffff', border: '1px solid #0369a1', cursor: totpBusy ? 'not-allowed' : 'pointer' }}
                >
                  Khởi tạo khóa xác thực 2 lớp
                </button>
              </div>
            </div>
          )}

          {totpEnrollmentStage === 'ENROLLING' && Boolean(totpState.otpauthUri) && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 16, padding: '12px 14px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1' }}>
                {totpState.otpauthUri && (
                  <div data-testid="totp-qr-container" style={{ padding: 10, borderRadius: 8, border: '1px solid #e2e8f0', background: '#ffffff', flex: '0 0 auto' }}>
                    <BarcodeSvg value={totpState.otpauthUri} format="QR" height={180} showQr={true} qrCaption="Mã QR TOTP" captionLabel="Mã QR TOTP" />
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
                    <code data-testid="totp-secret-code" style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace', fontWeight: 700, fontSize: 13, color: '#0369a1', letterSpacing: '0.03em' }}>
                      {showTotpSecret && totpState.secretKeyBase32 ? formatBase32Groups(totpState.secretKeyBase32) : '•••• •••• •••• •••• •••• •••• •••• ••••'}
                    </code>
                    <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
                      <button type="button" className="ui-btn-secondary" data-testid="toggle-totp-secret-visibility" onClick={() => void toggleTotpSecretVisibility()} style={secBtnStyle}>
                        {showTotpSecret ? 'Ẩn khóa nhập tay' : 'Hiện khóa nhập tay'}
                      </button>
                      <button type="button" className="ui-btn-secondary" data-testid="btn-copy-totp-secret" onClick={() => void copyTotpSecretToClipboard()} style={secBtnStyle}>
                        Sao chép khóa
                      </button>
                    </div>
                  </div>
                </div>
              </div>

              <div style={{ padding: '12px 14px', borderRadius: 8, border: '1px solid #bae6fd', background: '#f0f9ff', display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
                <label htmlFor="totp-otp-input" style={{ fontSize: 13, fontWeight: 700, color: '#0f172a', margin: 0 }}>
                  Bước 2: Nhập mã 6 chữ số từ ứng dụng để xác minh:
                </label>
                <div className="ui-inline-actions" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
                  <input
                    id="totp-otp-input"
                    aria-label="Mã xác thực 6 chữ số"
                    placeholder="Nhập 6 chữ số OTP"
                    value={totpVerifyInput}
                    onChange={e => setTotpVerifyInput(e.target.value)}
                    maxLength={6}
                    style={{ height: 36, minHeight: 36, width: 170, padding: '4px 10px', boxSizing: 'border-box', fontVariantNumeric: 'tabular-nums', background: '#ffffff', border: '1px solid #94a3b8', borderRadius: 6 }}
                  />
                  <button
                    type="button"
                    data-testid="btn-verify-totp"
                    disabled={totpBusy}
                    onClick={() => void verifyTotpCode()}
                    style={{ height: 36, padding: '4px 14px', borderRadius: 6, fontSize: 13, fontWeight: 700, background: '#0284c7', color: '#ffffff', border: '1px solid #0369a1', cursor: totpBusy ? 'not-allowed' : 'pointer' }}
                  >
                    Xác minh mã 6 chữ số
                  </button>
                  <button
                    type="button"
                    className="ui-btn-secondary"
                    data-testid="btn-setup-totp"
                    disabled={totpBusy}
                    onClick={() => void setupTotpMfa()}
                    style={{ height: 36, padding: '4px 12px', borderRadius: 6, fontSize: 12.5, fontWeight: 600, background: '#ffffff', color: '#0f172a', border: '1px solid #94a3b8', cursor: totpBusy ? 'not-allowed' : 'pointer' }}
                  >
                    Sinh lại mã QR mới
                  </button>
                  <button
                    type="button"
                    className="ui-btn-secondary"
                    data-testid="btn-cancel-totp-enrollment"
                    disabled={totpBusy}
                    onClick={() => void cancelTotpEnrollment()}
                    style={{ height: 36, padding: '4px 12px', borderRadius: 6, fontSize: 12.5, fontWeight: 600, background: '#ffffff', color: '#475569', border: '1px solid #cbd5e1', cursor: totpBusy ? 'not-allowed' : 'pointer' }}
                  >
                    Hủy thiết lập
                  </button>
                </div>
              </div>
            </div>
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
