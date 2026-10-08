import { useEffect, useMemo, useState } from 'react';
import type { BlueprintCapability } from '../config/erpWmsBlueprint';
import { getCapabilityDemoDefinition } from '../config/capabilityDemoScreens';
import { useMockDemo } from '../context/MockDemoContext';
import './CapabilityInteractiveDemo.css';

interface CapabilityInteractiveDemoProps {
  capability: BlueprintCapability;
  moduleName: string;
  moduleFlow?: string[];
  sampleReference?: string;
  sampleWarehouse?: string;
  recordCount: number;
}

const modeLabel = (mode: string) => ({
  workbench: 'Bàn làm việc',
  wizard: 'Trình hướng dẫn',
  control: 'Điều khiển',
  scan: 'Luồng quét',
  trace: 'Truy vết',
  configuration: 'Cấu hình',
}[mode] ?? mode);

const CapabilityInteractiveDemo = ({
  capability,
  moduleName,
  moduleFlow,
  sampleReference,
  sampleWarehouse,
  recordCount,
}: CapabilityInteractiveDemoProps) => {
  const mockDemo = useMockDemo();
  const definition = useMemo(
    () => getCapabilityDemoDefinition(capability, moduleName, moduleFlow),
    [capability, moduleFlow, moduleName],
  );
  const [stageIndex, setStageIndex] = useState(0);
  const [exceptionOpen, setExceptionOpen] = useState(false);
  const [quantity, setQuantity] = useState(definition.quantity?.initial ?? 0);
  const [activity, setActivity] = useState<string[]>([]);
  const linkedToSharedScenario = mockDemo.isCapabilityInActiveScenario(capability.id);

  useEffect(() => {
    setStageIndex(0);
    setExceptionOpen(false);
    setQuantity(definition.quantity?.initial ?? 0);
    setActivity([]);
  }, [capability.id, definition.quantity?.initial]);

  const stage = definition.stages[Math.min(stageIndex, definition.stages.length - 1)];
  const finalStage = stageIndex >= definition.stages.length - 1;
  const baseQuantity = definition.quantity ? quantity * definition.quantity.factor : undefined;

  const pushActivity = (message: string) =>
    setActivity((current) => [message, ...current].slice(0, 6));

  const advance = () => {
    if (exceptionOpen || finalStage) return;
    const nextIndex = Math.min(stageIndex + 1, definition.stages.length - 1);
    setStageIndex(nextIndex);
    pushActivity('Trạng thái → ' + definition.stages[nextIndex]);
  };

  const reset = () => {
    setStageIndex(0);
    setExceptionOpen(false);
    setQuantity(definition.quantity?.initial ?? 0);
    setActivity(['Đã đặt lại mô phỏng về trạng thái đầu.']);
  };

  const raiseException = () => {
    setExceptionOpen(true);
    pushActivity('Ngoại lệ → ' + definition.exceptionTitle);
  };

  const resolveException = () => {
    setExceptionOpen(false);
    pushActivity('Đã xử lý ngoại lệ • quay lại luồng thực thi.');
  };

  return (
    <section className="capability-panel interactive-demo" data-testid="interactive-capability-demo">
      <div className="interactive-demo-header">
        <div>
          <span className="interactive-demo-kicker">BẢN THIẾT KẾ TƯƠNG TÁC • CHỈ MÔ PHỎNG</span>
          <h2>{definition.title}</h2>
          <p>{definition.subtitle}</p>
        </div>
        <div className="interactive-demo-meta">
          <span>{modeLabel(definition.mode)}</span>
          <strong>{stage}</strong>
        </div>
      </div>

      {linkedToSharedScenario && (
        <div className="interactive-shared-scenario" data-testid="shared-scenario-banner">
          <span>KỊCH BẢN DÙNG CHUNG ĐANG HOẠT ĐỘNG</span>
          <strong>{mockDemo.activeScenarioId} • {mockDemo.activeScenarioTitle}</strong>
          <small>Bước {mockDemo.activeScenarioStep} • trạng thái này dùng chung với Phòng kịch bản chuẩn và Phòng dữ liệu mô phỏng.</small>
        </div>
      )}

      <div className="interactive-demo-progress" aria-label="Luồng trạng thái mô phỏng">
        {definition.stages.map((item, index) => {
          let stageClassName = '';
          if (index === stageIndex) stageClassName = 'active';
          else if (index < stageIndex) stageClassName = 'complete';

          return (
            <button
              type="button"
              key={item + index}
              className={stageClassName}
              onClick={() => {
                setStageIndex(index);
                setExceptionOpen(false);
                pushActivity('Chuyển nhanh trạng thái → ' + item);
              }}
            >
              <span>{index + 1}</span>
              <strong>{item}</strong>
            </button>
          );
        })}
      </div>

      <div className="interactive-demo-grid">
        <div className="interactive-demo-workbench">
          <div className="interactive-demo-context">
            <div><span>Tham chiếu</span><strong>{sampleReference ?? 'MOCK-' + capability.id}</strong></div>
            <div><span>Kho</span><strong>{sampleWarehouse ?? 'WH-HCM-01'}</strong></div>
            <div><span>Bản ghi mô phỏng</span><strong>{recordCount}</strong></div>
            <div><span>Ảnh hưởng hệ thống thật</span><strong>Không có</strong></div>
          </div>

          <div className="interactive-demo-fields">
            {definition.fields.map((field) => (
              <div key={field.label}>
                <span>{field.label}</span>
                <strong>{field.value}</strong>
                {field.helper ? <small>{field.helper}</small> : null}
              </div>
            ))}
          </div>

          {definition.quantity && (
            <div className="interactive-quantity">
              <label htmlFor={'demo-qty-' + capability.id}>{definition.quantity.label}</label>
              <div>
                <input
                  id={'demo-qty-' + capability.id}
                  type="number"
                  min="0"
                  value={quantity}
                  onChange={(event) => setQuantity(Math.max(0, Number(event.target.value) || 0))}
                />
                <span>{definition.quantity.operationUom}</span>
                <b>=</b>
                <strong>{baseQuantity?.toLocaleString('vi-VN')} {definition.quantity.baseUom}</strong>
              </div>
              <small>Đơn vị thao tác → Đơn vị cơ sở được hiển thị trước khi xác nhận.</small>
            </div>
          )}

          {exceptionOpen ? (
            <div className="interactive-exception" role="alert">
              <strong>{definition.exceptionTitle}</strong>
              <p>{definition.exceptionDetail}</p>
              <button type="button" onClick={resolveException}>Giải quyết ngoại lệ</button>
            </div>
          ) : (
            <div className="interactive-state-card">
              <span>Trạng thái mô phỏng hiện tại</span>
              <strong>{stage}</strong>
              <p>{finalStage ? 'Luồng minh họa đã tới trạng thái cuối.' : 'Có thể thực hiện lệnh, tạo ngoại lệ hoặc chuyển bước tiếp theo.'}</p>
            </div>
          )}

          <div className="interactive-demo-actions">
            <button type="button" className="interactive-primary" disabled={exceptionOpen || finalStage} onClick={advance}>
              {finalStage ? 'Đã tới bước cuối' : 'Thực hiện bước tiếp theo'}
            </button>
            <button type="button" disabled={exceptionOpen} onClick={raiseException}>Mô phỏng ngoại lệ</button>
            <button type="button" onClick={reset}>Đặt lại mô phỏng</button>
          </div>
        </div>

        <aside className="interactive-command-panel">
          <span className="interactive-demo-kicker">LỆNH / HÀNH ĐỘNG</span>
          <h3>Hành động mô phỏng</h3>
          <div className="interactive-command-list">
            {definition.commands.map((command) => (
              <button
                type="button"
                key={command}
                onClick={() => pushActivity('Lệnh mô phỏng → ' + command)}
              >
                {command}
              </button>
            ))}
          </div>

          <div className="interactive-activity">
            <strong>Nhật ký hoạt động</strong>
            {activity.length ? (
              <ul>{activity.map((item, index) => <li key={item + index}>{item}</li>)}</ul>
            ) : (
              <p>Chưa có thao tác trong phiên mô phỏng.</p>
            )}
          </div>

          <div className="interactive-demo-note">
            Đây là trình mô phỏng phía trình duyệt phục vụ bản thiết kế hệ thống. Không gọi API thật, không ghi cơ sở dữ liệu và không thay đổi tồn kho trên hệ thống thật.
          </div>
        </aside>
      </div>
    </section>
  );
};

export default CapabilityInteractiveDemo;
