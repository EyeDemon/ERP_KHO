import React, { useState } from 'react';
import { useInRouterContext, useLocation, useNavigate } from 'react-router-dom';

export type OutboundFlowMode = 'EXPRESS_2_STEP' | 'FULL_5_STEP';

export interface OutboundPipelineStep {
  stepNumber: number;
  label: string;
  subtitle: string;
  path: string;
  matchPaths: string[];
}

const EXPRESS_STEPS: OutboundPipelineStep[] = [
  {
    stepNumber: 1,
    label: 'Đơn bán hàng và phân bổ',
    subtitle: 'Tạo đơn bán và giữ tồn ưu tiên hạn dùng',
    path: '/outbound-demand',
    matchPaths: ['/outbound-demand', '/sales-orders', '/stock-reservations', '/stock-allocations', '/backorders'],
  },
  {
    stepNumber: 2,
    label: 'Duyệt và xuất kho nhanh',
    subtitle: 'Trừ tồn 1 chặng và ghi sổ cái',
    path: '/export-receipts',
    matchPaths: ['/export-receipts', '/shipments', '/handover-manifests'],
  },
];

const FULL_WMS_STEPS: OutboundPipelineStep[] = [
  {
    stepNumber: 1,
    label: 'Đơn bán hàng và đợt lấy',
    subtitle: 'Phân bổ và gom cụm lấy hàng',
    path: '/outbound-demand',
    matchPaths: ['/outbound-demand', '/sales-orders', '/stock-reservations', '/stock-allocations', '/backorders'],
  },
  {
    stepNumber: 2,
    label: 'Lấy hàng tại kệ',
    subtitle: 'Quét vị trí kệ và lấy hàng ưu tiên hạn dùng',
    path: '/picking-tasks',
    matchPaths: ['/picking-tasks'],
  },
  {
    stepNumber: 3,
    label: 'Đóng gói đơn hàng',
    subtitle: 'Quét kiểm mã hàng và đóng kiện',
    path: '/packing-sessions',
    matchPaths: ['/packing-sessions'],
  },
  {
    stepNumber: 4,
    label: 'Phiếu xuất và vận đơn',
    subtitle: 'Hạch toán xuất và tạo vận đơn',
    path: '/export-receipts',
    matchPaths: ['/export-receipts', '/shipments'],
  },
  {
    stepNumber: 5,
    label: 'Bàn giao vận đơn',
    subtitle: 'Niêm phong tải và ký nhận ĐVVC',
    path: '/handover-manifests',
    matchPaths: ['/handover-manifests'],
  },
];

interface StepperCoreProps {
  pathname: string;
  onNavigate: (path: string) => void;
  defaultMode?: OutboundFlowMode;
}

const OutboundPipelineStepperCore: React.FC<StepperCoreProps> = ({ pathname, onNavigate, defaultMode }) => {
  const [flowMode, setFlowMode] = useState<OutboundFlowMode>(() => {
    if (defaultMode) return defaultMode;
    if (typeof window !== 'undefined' && window.localStorage) {
      const saved = window.localStorage.getItem('outboundFlowMode');
      if (saved === 'EXPRESS_2_STEP' || saved === 'FULL_5_STEP') return saved;
      if (window.localStorage.getItem('wmsComplexityMode') === 'EXPRESS') return 'EXPRESS_2_STEP';
    }
    return 'FULL_5_STEP';
  });

  const handleToggleMode = (nextMode: OutboundFlowMode) => {
    setFlowMode(nextMode);
    if (typeof window !== 'undefined' && window.localStorage) {
      window.localStorage.setItem('outboundFlowMode', nextMode);
    }
  };

  const steps = flowMode === 'EXPRESS_2_STEP' ? EXPRESS_STEPS : FULL_WMS_STEPS;
  const activeIndex = Math.max(
    0,
    steps.findIndex((s) => s.matchPaths.some((mp) => pathname === mp || pathname.startsWith(mp + '/'))),
  );
  const currentStep = steps[activeIndex] ?? steps[0];

  return (
    <section
      className="outbound-pipeline-stepper outbound-pipeline-stepper-mobile-hide"
      data-testid="outbound-pipeline-stepper"
      aria-label="Tiến trình nghiệp vụ xuất kho"
      style={{ background: 'var(--surface-card, #ffffff)', border: '1px solid var(--border-subtle, #e2e8f0)', borderRadius: 10, padding: '12px 16px', marginBottom: 14 }}
    >
      <div className="outbound-stepper-mobile-compact" data-testid="outbound-stepper-mobile-compact">
        <span className="tabular-nums">{`Chặng ${activeIndex + 1}/${steps.length} • ${currentStep?.label ?? ''}`}</span>
      </div>

      <div className="outbound-stepper-desktop-header" style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', alignItems: 'center', gap: 10, marginBottom: 10 }}>
        <div>
          <div style={{ fontSize: 13, fontWeight: 700, color: '#0f172a' }}>
            Chuỗi điều phối xuất kho và giao vận
          </div>
          <div style={{ fontSize: 12, color: '#475569' }}>
            {flowMode === 'EXPRESS_2_STEP'
              ? 'Chế độ 1 — Xuất kho nhanh 2 chặng: Rút gọn thao tác lấy và đóng gói cho đơn xuất sỉ.'
              : 'Chế độ 2 — Xuất kho tiêu chuẩn 5 chặng: Kiểm soát chặt từ gom đợt, lấy hàng, đóng gói, lập phiếu xuất đến bàn giao vận đơn.'}
          </div>
        </div>

        <div data-testid="outbound-mode-toggle" role="group" aria-label="Chuyển đổi chế độ quy trình xuất kho" style={{ display: 'inline-flex', gap: 6 }}>
          <button
            type="button"
            data-testid="outbound-mode-express"
            aria-pressed={flowMode === 'EXPRESS_2_STEP'}
            onClick={() => handleToggleMode('EXPRESS_2_STEP')}
            style={{ fontSize: 12, padding: '5px 10px', borderRadius: 6, border: flowMode === 'EXPRESS_2_STEP' ? '1px solid #0f172a' : '1px solid #cbd5e1', background: flowMode === 'EXPRESS_2_STEP' ? '#0f172a' : '#ffffff', color: flowMode === 'EXPRESS_2_STEP' ? '#ffffff' : '#1e293b', fontWeight: flowMode === 'EXPRESS_2_STEP' ? 600 : 500, cursor: 'pointer' }}
          >
            Xuất kho nhanh 2 chặng
          </button>
          <button
            type="button"
            data-testid="outbound-mode-full"
            aria-pressed={flowMode === 'FULL_5_STEP'}
            onClick={() => handleToggleMode('FULL_5_STEP')}
            style={{ fontSize: 12, padding: '5px 10px', borderRadius: 6, border: flowMode === 'FULL_5_STEP' ? '1px solid #0f172a' : '1px solid #cbd5e1', background: flowMode === 'FULL_5_STEP' ? '#0f172a' : '#ffffff', color: flowMode === 'FULL_5_STEP' ? '#ffffff' : '#1e293b', fontWeight: flowMode === 'FULL_5_STEP' ? 600 : 500, cursor: 'pointer' }}
          >
            Xuất kho tiêu chuẩn 5 chặng
          </button>
        </div>
      </div>

      <div className="outbound-stepper-desktop-list" style={{ display: 'grid', gridTemplateColumns: `repeat(${steps.length}, minmax(150px, 1fr))`, gap: 8, overflowX: 'auto' }}>
        {steps.map((step, idx) => {
          const isCurrent = idx === activeIndex;
          const isCompleted = idx < activeIndex;
          return (
            <button
              key={step.path + '-' + step.stepNumber}
              type="button"
              data-testid={`outbound-step-${step.stepNumber}`}
              aria-current={isCurrent ? 'step' : undefined}
              onClick={() => onNavigate(step.path)}
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'flex-start',
                textAlign: 'left',
                padding: '8px 10px',
                borderRadius: 8,
                border: isCurrent ? '2px solid #0f172a' : isCompleted ? '1px solid #86efac' : '1px solid #cbd5e1',
                background: isCurrent ? '#0f172a' : isCompleted ? '#f0fdf4' : '#f8fafc',
                color: isCurrent ? '#ffffff' : '#0f172a',
                cursor: 'pointer',
              }}
            >
              <span className="tabular-nums" style={{ fontSize: 11, fontWeight: 700, color: isCurrent ? '#93c5fd' : isCompleted ? '#15803d' : '#475569' }}>
                CHẶNG {step.stepNumber}/{steps.length} {isCompleted ? '• ĐÃ QUA' : isCurrent ? '• ĐANG MỞ' : ''}
              </span>
              <span style={{ fontSize: 12.5, fontWeight: 600, marginTop: 2 }}>{step.label}</span>
              <span style={{ fontSize: 11, color: isCurrent ? '#cbd5e1' : '#475569', marginTop: 2 }}>{step.subtitle}</span>
            </button>
          );
        })}
      </div>
    </section>
  );
};

const OutboundPipelineStepperWithRouter: React.FC<{ defaultMode?: OutboundFlowMode }> = ({ defaultMode }) => {
  const location = useLocation();
  const navigate = useNavigate();
  return <OutboundPipelineStepperCore pathname={location.pathname} onNavigate={(path) => navigate(path)} defaultMode={defaultMode} />;
};

export const OutboundPipelineStepper: React.FC<{ defaultMode?: OutboundFlowMode }> = ({ defaultMode }) => {
  const inRouter = useInRouterContext();
  if (inRouter) return <OutboundPipelineStepperWithRouter defaultMode={defaultMode} />;
  const fallbackPathname = typeof window !== 'undefined' ? window.location.pathname : '/export-receipts';
  return (
    <OutboundPipelineStepperCore
      pathname={fallbackPathname}
      onNavigate={(path) => {
        if (typeof window !== 'undefined' && window.history?.pushState) window.history.pushState({}, '', path);
      }}
      defaultMode={defaultMode}
    />
  );
};

export default OutboundPipelineStepper;
