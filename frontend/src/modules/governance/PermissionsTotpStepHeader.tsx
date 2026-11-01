import { UiBadge } from '../../ui/ProductionUi';

export type TotpEnrollmentStage = 'IDLE' | 'ENROLLING' | 'RECOVERY_CODES_PENDING' | 'VERIFIED';

export function PermissionsTotpStepHeader({
  totpEnrollmentStage,
  totpVerifyInput,
}: {
  totpEnrollmentStage: TotpEnrollmentStage;
  totpVerifyInput: string;
}) {
  return (
    <>
      <div
        data-testid="totp-admin-policy-banner"
        style={{
          padding: '10px 14px',
          borderRadius: 8,
          background: '#f0f9ff',
          border: '1px solid #bae6fd',
          color: '#0c4a6e',
          fontSize: 13,
          fontWeight: 600,
          lineHeight: 1.5,
        }}
      >
        Chính sách bắt buộc đối với tài khoản Quản trị viên khi thực hiện các thao tác trọng yếu: điều chỉnh tồn kho, phê duyệt kiểm kê, xuất dữ liệu sổ cái và thay đổi phân quyền.
      </div>

      <div
        data-testid="totp-3step-guide"
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))',
          gap: 10,
          padding: '12px 14px',
          borderRadius: 8,
          background: '#f8fafc',
          border: '1px solid #cbd5e1',
          fontSize: 12.5,
          color: '#0f172a',
        }}
      >
        <div
          data-testid="totp-step-card-1"
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 6,
            padding: '8px 10px',
            borderRadius: 6,
            background: '#ffffff',
            border: '1px solid #e2e8f0',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: 22,
                height: 22,
                borderRadius: '50%',
                background: '#0284c7',
                color: '#ffffff',
                fontWeight: 700,
                fontSize: 12,
              }}
            >
              1
            </span>
            <span data-testid="totp-step-badge-1">
              <UiBadge
                tone={
                  totpEnrollmentStage === 'IDLE'
                    ? 'info'
                    : totpEnrollmentStage === 'ENROLLING' && totpVerifyInput.trim().length === 0
                      ? 'warning'
                      : 'success'
                }
              >
                {totpEnrollmentStage === 'IDLE'
                  ? 'Bước bắt đầu'
                  : totpEnrollmentStage === 'ENROLLING' && totpVerifyInput.trim().length === 0
                    ? 'Đang thực hiện'
                    : 'Đã hoàn tất'}
              </UiBadge>
            </span>
          </div>
          <strong style={{ color: '#0f172a', fontSize: 12.5 }}>
            1. Quét mã QR bằng ứng dụng Google / Microsoft Authenticator
          </strong>
        </div>

        <div
          data-testid="totp-step-card-2"
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 6,
            padding: '8px 10px',
            borderRadius: 6,
            background: '#ffffff',
            border: '1px solid #e2e8f0',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: 22,
                height: 22,
                borderRadius: '50%',
                background: totpEnrollmentStage === 'IDLE' ? '#64748b' : '#0284c7',
                color: '#ffffff',
                fontWeight: 700,
                fontSize: 12,
              }}
            >
              2
            </span>
            <span data-testid="totp-step-badge-2">
              <UiBadge
                tone={
                  totpEnrollmentStage === 'IDLE' || (totpEnrollmentStage === 'ENROLLING' && totpVerifyInput.trim().length === 0)
                    ? 'neutral'
                    : totpEnrollmentStage === 'ENROLLING'
                      ? 'warning'
                      : 'success'
                }
              >
                {totpEnrollmentStage === 'IDLE' || (totpEnrollmentStage === 'ENROLLING' && totpVerifyInput.trim().length === 0)
                  ? 'Bước tiếp theo'
                  : totpEnrollmentStage === 'ENROLLING'
                    ? 'Đang thực hiện'
                    : 'Đã hoàn tất'}
              </UiBadge>
            </span>
          </div>
          <strong style={{ color: '#0f172a', fontSize: 12.5 }}>
            2. Nhập mã 6 chữ số từ ứng dụng để xác minh
          </strong>
        </div>

        <div
          data-testid="totp-step-card-3"
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 6,
            padding: '8px 10px',
            borderRadius: 6,
            background: '#ffffff',
            border: '1px solid #e2e8f0',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: 22,
                height: 22,
                borderRadius: '50%',
                background:
                  totpEnrollmentStage === 'RECOVERY_CODES_PENDING' || totpEnrollmentStage === 'VERIFIED'
                    ? '#0284c7'
                    : '#64748b',
                color: '#ffffff',
                fontWeight: 700,
                fontSize: 12,
              }}
            >
              3
            </span>
            <span data-testid="totp-step-badge-3">
              <UiBadge
                tone={
                  totpEnrollmentStage === 'RECOVERY_CODES_PENDING'
                    ? 'warning'
                    : totpEnrollmentStage === 'VERIFIED'
                      ? 'success'
                      : 'neutral'
                }
              >
                {totpEnrollmentStage === 'RECOVERY_CODES_PENDING'
                  ? 'Đang thực hiện'
                  : totpEnrollmentStage === 'VERIFIED'
                    ? 'Đã hoàn tất'
                    : 'Bước tiếp theo'}
              </UiBadge>
            </span>
          </div>
          <strong style={{ color: '#0f172a', fontSize: 12.5 }}>
            3. Lưu 8 mã khôi phục dự phòng (dùng khi mất điện thoại)
          </strong>
        </div>
      </div>
    </>
  );
}
