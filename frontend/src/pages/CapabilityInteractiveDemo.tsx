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
    setActivity(['Demo reset về trạng thái đầu.']);
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
          <span className="interactive-demo-kicker">INTERACTIVE BLUEPRINT • MOCK ONLY</span>
          <h2>{definition.title}</h2>
          <p>{definition.subtitle}</p>
        </div>
        <div className="interactive-demo-meta">
          <span>{definition.mode}</span>
          <strong>{stage}</strong>
        </div>
      </div>

      {linkedToSharedScenario && (
        <div className="interactive-shared-scenario" data-testid="shared-scenario-banner">
          <span>ACTIVE SHARED SCENARIO</span>
          <strong>{mockDemo.activeScenarioId} • {mockDemo.activeScenarioTitle}</strong>
          <small>Step {mockDemo.activeScenarioStep} • trạng thái này dùng chung với Golden Scenario Lab và Mock Data Lab.</small>
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
            <div><span>Reference</span><strong>{sampleReference ?? 'MOCK-' + capability.id}</strong></div>
            <div><span>Warehouse</span><strong>{sampleWarehouse ?? 'WH-HCM-01'}</strong></div>
            <div><span>Mock records</span><strong>{recordCount}</strong></div>
            <div><span>Production effect</span><strong>Không có</strong></div>
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
              <small>Operation UOM → Base UOM được hiển thị trước khi xác nhận.</small>
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
            <button type="button" onClick={reset}>Reset demo</button>
          </div>
        </div>

        <aside className="interactive-command-panel">
          <span className="interactive-demo-kicker">COMMANDS / ACTIONS</span>
          <h3>Action mô phỏng</h3>
          <div className="interactive-command-list">
            {definition.commands.map((command) => (
              <button
                type="button"
                key={command}
                onClick={() => pushActivity('Command mock → ' + command)}
              >
                {command}
              </button>
            ))}
          </div>

          <div className="interactive-activity">
            <strong>Activity log</strong>
            {activity.length ? (
              <ul>{activity.map((item, index) => <li key={item + index}>{item}</li>)}</ul>
            ) : (
              <p>Chưa có thao tác trong phiên demo.</p>
            )}
          </div>

          <div className="interactive-demo-note">
            Đây là simulator client-side phục vụ Blueprint. Không gọi API thật, không ghi database và không thay đổi inventory production.
          </div>
        </aside>
      </div>
    </section>
  );
};

export default CapabilityInteractiveDemo;
