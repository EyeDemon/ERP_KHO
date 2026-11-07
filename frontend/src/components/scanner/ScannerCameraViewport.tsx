import type { RefObject } from 'react';

interface ScannerCameraViewportProps {
  cameraActive: boolean;
  isCameraLoading: boolean;
  isLiveVideo: boolean;
  isDisconnectedCamera: boolean;
  cameraFlashSuccess: boolean;
  disabled: boolean;
  busy: boolean;
  compactMobile?: boolean;
  videoRef: RefObject<HTMLVideoElement | null>;
  canvasRef: RefObject<HTMLCanvasElement | null>;
  onVideoLoadedData: () => void;
  onToggleCamera: () => void;
  onRetryCamera: () => void;
}

export function ScannerCameraViewport({
  cameraActive,
  isCameraLoading,
  isLiveVideo,
  isDisconnectedCamera,
  cameraFlashSuccess,
  disabled,
  busy,
  compactMobile = false,
  videoRef,
  canvasRef,
  onVideoLoadedData,
  onToggleCamera,
  onRetryCamera,
}: ScannerCameraViewportProps) {
  return (
    <div
      className={`scanner-camera-viewport${isLiveVideo ? ' is-live' : ''}${isDisconnectedCamera ? ' is-warning' : ''}`}
      data-testid="scanner-camera-viewport"
      style={{
        ...(compactMobile ? { minHeight: 132, padding: '8px 10px' } : {}),
        ...(cameraFlashSuccess
          ? {
              borderColor: '#10b981',
              boxShadow: '0 0 0 4px rgba(16, 185, 129, 0.38)',
            }
          : {}),
      }}
    >
      <span className="viewfinder-corner tl" aria-hidden="true" />
      <span className="viewfinder-corner tr" aria-hidden="true" />
      <span className="viewfinder-corner bl" aria-hidden="true" />
      <span className="viewfinder-corner br" aria-hidden="true" />

      <canvas ref={canvasRef} style={{ display: 'none' }} aria-hidden="true" />

      <video
        ref={videoRef}
        muted
        playsInline
        onLoadedData={onVideoLoadedData}
        style={
          isLiveVideo
            ? {
                position: 'absolute',
                inset: 0,
                width: '100%',
                height: '100%',
                objectFit: 'cover',
                borderRadius: 11,
                zIndex: 1,
              }
            : {
                position: 'absolute',
                width: 1,
                height: 1,
                opacity: 0,
                pointerEvents: 'none',
              }
        }
      />

      {!cameraActive && (
        <div
          data-testid="scanner-camera-skeleton-idle"
          style={{
            position: 'relative',
            zIndex: 2,
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: compactMobile ? 4 : 8,
            maxWidth: 276,
          }}
        >
          <svg width={compactMobile ? 30 : 42} height={compactMobile ? 30 : 42} viewBox="0 0 48 48" fill="none" aria-hidden="true">
            <rect x="6" y="12" width="36" height="26" rx="5" stroke="#38bdf8" strokeWidth="2.5" fill="rgba(56, 189, 248, 0.1)" />
            <path d="M17 12L19.5 8H28.5L31 12" stroke="#38bdf8" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="24" cy="25" r="6.5" stroke="#e2e8f0" strokeWidth="2.2" />
            <path d="M13 21V29M35 21V29" stroke="#94a3b8" strokeWidth="2" strokeLinecap="round" />
          </svg>
          <strong style={{ fontSize: compactMobile ? 12.5 : 13.5, color: '#f8fafc', lineHeight: 1.25 }}>
            Camera quét mã đang tắt
          </strong>
          {!compactMobile && (
            <span style={{ fontSize: 11.5, color: '#cbd5e1', lineHeight: 1.4 }}>
              Bấm «Bật Camera» để quét bằng ống kính hoặc dùng súng quét bắn trực tiếp vào ô bên phải
            </span>
          )}
          <button
            type="button"
            data-testid={compactMobile ? 'btn-toggle-camera' : undefined}
            className="ui-btn ui-btn-secondary"
            disabled={disabled || busy}
            onClick={onToggleCamera}
            style={{
              marginTop: 2,
              minHeight: compactMobile ? 36 : 34,
              padding: '5px 12px',
              fontSize: 12,
              fontWeight: 700,
              background: 'rgba(15, 23, 42, 0.85)',
              color: '#38bdf8',
              borderColor: '#38bdf8',
            }}
          >
            Bật Camera
          </button>
        </div>
      )}

      {cameraActive && isCameraLoading && (
        <>
          <div className="scanner-skeleton-shimmer" />
          <div className="scanner-laser-line" />
          <div
            data-testid="scanner-camera-skeleton-loading"
            style={{
              position: 'relative',
              zIndex: 3,
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              gap: 6,
              maxWidth: 276,
              padding: '10px 14px',
              borderRadius: 10,
              background: 'rgba(15, 23, 42, 0.86)',
              border: '1px solid rgba(56, 189, 248, 0.28)',
            }}
          >
            <svg width="36" height="36" viewBox="0 0 48 48" fill="none" aria-hidden="true">
              <rect x="8" y="12" width="32" height="24" rx="4" stroke="#38bdf8" strokeWidth="2.5" strokeDasharray="4 3" />
              <path d="M16 18V30M21 18V30M26 18V30M32 18V30" stroke="#e2e8f0" strokeWidth="2.2" strokeLinecap="round" />
            </svg>
            <strong style={{ fontSize: 13, color: '#f8fafc' }}>
              Đang kết nối ống kính camera...
            </strong>
            <small style={{ fontSize: 11, color: '#cbd5e1', lineHeight: 1.35 }}>
              Hướng camera về phía mã vạch kệ hoặc sản phẩm (SKU)...
            </small>
          </div>
        </>
      )}

      {isDisconnectedCamera && (
        <>
          <div className="scanner-skeleton-shimmer" />
          <div className="scanner-laser-line" />
          <div
            data-testid="scanner-camera-skeleton-disconnected"
            style={{
              position: 'relative',
              zIndex: 3,
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 6,
              width: '100%',
              height: '100%',
            }}
          >
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: 5,
                maxWidth: 284,
                padding: '8px 12px',
                borderRadius: 10,
                background: 'rgba(15, 23, 42, 0.88)',
                border: '1px solid rgba(245, 158, 11, 0.35)',
              }}
            >
              <svg width="32" height="32" viewBox="0 0 48 48" fill="none" aria-hidden="true">
                <rect x="6" y="12" width="36" height="26" rx="5" stroke="#f59e0b" strokeWidth="2.5" fill="rgba(245, 158, 11, 0.12)" />
                <path d="M15 20V30M20 20V30M28 20V30M33 20V30" stroke="#fcd34d" strokeWidth="2" strokeLinecap="round" />
                <path d="M10 10L38 38" stroke="#f87171" strokeWidth="2.5" strokeLinecap="round" />
              </svg>
              <strong style={{ fontSize: 12.5, color: '#fef3c7', lineHeight: 1.25 }}>
                Chưa kết nối thiết bị Camera
              </strong>
              <span style={{ fontSize: 11, color: '#e2e8f0', lineHeight: 1.3 }}>
                Không tìm thấy camera vật lý hoặc chưa cấp quyền trình duyệt. Hãy kết nối camera hoặc dùng súng quét ở khung bên phải
              </span>
              <button
                type="button"
                className="ui-btn ui-btn-secondary"
                disabled={disabled || busy}
                onClick={onRetryCamera}
                style={{
                  minHeight: 28,
                  padding: '3px 10px',
                  fontSize: 11.5,
                  fontWeight: 700,
                  background: 'rgba(15, 23, 42, 0.95)',
                  color: '#fcd34d',
                  borderColor: '#f59e0b',
                }}
              >
                Thử kết nối lại
              </button>
            </div>
            <small style={{ fontSize: 10.5, color: '#cbd5e1', lineHeight: 1.2, background: 'rgba(15, 23, 42, 0.75)', padding: '2px 8px', borderRadius: 4 }}>
              Hướng camera về phía mã vạch kệ hoặc sản phẩm (SKU)...
            </small>
          </div>
        </>
      )}

      {isLiveVideo && (
        <>
          <div className="scanner-laser-line" />
          <small
            style={{
              position: 'absolute',
              bottom: 8,
              left: 12,
              right: 12,
              zIndex: 3,
              padding: '3px 8px',
              borderRadius: 6,
              background: 'rgba(15, 23, 42, 0.78)',
              color: '#f8fafc',
              fontSize: 11,
            }}
          >
            Hướng camera về phía mã vạch kệ hoặc sản phẩm (SKU)...
          </small>
        </>
      )}
    </div>
  );
}
