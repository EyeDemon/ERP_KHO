// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import CapabilityPreview from './CapabilityPreview';
import { MockDemoProvider, useMockDemo } from '../context/MockDemoContext';

const renderPreview = (moduleKey: string, capabilityId: string) => render(
  <MemoryRouter initialEntries={[`/system-blueprint/${moduleKey}/${capabilityId}`]}>
    <Routes>
      <Route path="/system-blueprint/:moduleKey/:capabilityId" element={<CapabilityPreview />} />
    </Routes>
  </MemoryRouter>,
);

describe('CapabilityPreview', () => {
  afterEach(cleanup);

  it('renders a planned capability with spec, states and mock records', () => {
    const view = renderPreview('outbound', 'OUT-08');
    expect(view.getAllByText('Shipment Dispatch').length).toBeGreaterThan(0);
    expect(view.getByText('MOCK / SPEC PREVIEW')).toBeTruthy();
    expect(view.getByText(/Dispatch mới giảm OnHand|Boundary trừ OnHand/)).toBeTruthy();
    expect(view.getAllByText('SHP-2026-5108').length).toBeGreaterThan(0);
    expect(view.getByText('Expected UX states')).toBeTruthy();
    expect(view.getByText('Technical contract')).toBeTruthy();
  });

  it('renders a live capability with link to the real screen', () => {
    const view = renderPreview('master-data', 'MD-01');
    expect(view.getByText('Sản phẩm / SKU')).toBeTruthy();
    const link = view.getByText('Mở chức năng hiện có');
    expect(link.getAttribute('href')).toBe('/products');
  });

  it('labels the planned WH-02 route as a specialized mock instead of a production feature', () => {
    const view = renderPreview('warehouse-structure', 'WH-02');
    const link = view.getByText('Mở mock chuyên biệt');
    expect(link.getAttribute('href')).toBe('/system-blueprint/warehouse-structure/WH-02/workbench');
    expect(view.queryByText('Mở chức năng hiện có')).toBeNull();
  });

  it('renders domain-specific warehouse mock panels for WH-03 through WH-07', () => {
    const cases = [
      ['WH-03', 'Sức chứa vị trí & Storage Constraints'],
      ['WH-04', 'Bản đồ kho & Heatmap'],
      ['WH-05', 'Lịch vận hành & Ca làm việc'],
      ['WH-06', 'Dock & Yard Control Board'],
      ['WH-07', 'Operational Calendar Exception Precedence'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('warehouse-structure', capabilityId);
      expect(view.getByTestId('warehouse-capability-mock-' + capabilityId)).toBeTruthy();
      expect(view.getByText(title)).toBeTruthy();
      expect(view.getAllByText(/Không gọi API production|Production effect/).length).toBeGreaterThan(0);
      view.unmount();
    }
  });

  it('renders domain-specific planned inbound mocks without promoting them to production', () => {
    const cases = [
      ['IN-01', 'Purchase Order / ASN Reconciliation'],
      ['IN-02', 'Receiving Appointment Board'],
      ['IN-05', 'Over / Under Receipt Resolution'],
      ['IN-06', 'Inbound QC Inspection'],
      ['IN-09', 'Putaway Recommendation Explainability'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('inbound', capabilityId);
      expect(view.getByTestId('inbound-capability-mock-' + capabilityId)).toBeTruthy();
      expect(view.getByRole('heading', { name: title })).toBeTruthy();
      expect(view.getByText(/Không gọi API production/)).toBeTruthy();
      expect(view.getAllByText('Theo đặc tả — chưa triển khai').length).toBeGreaterThan(0);
      view.unmount();
    }
  });

  it('does not add planned-domain panels on inbound foundation work centers', () => {
    for (const capabilityId of ['IN-03', 'IN-04', 'IN-07', 'IN-08']) {
      const view = renderPreview('inbound', capabilityId);
      expect(view.queryByTestId('inbound-capability-mock-' + capabilityId)).toBeNull();
      view.unmount();
    }
  });

  it('renders specialized outbound mocks only for capabilities that still need domain workbenches', () => {
    const cases = [
      ['OUT-03', 'Allocation Candidate Workbench'],
      ['OUT-04', 'Wave / Batch / Cluster Planning'],
      ['OUT-07', 'Staging & Loading Control'],
      ['OUT-08', 'Shipment Dispatch Boundary'],
      ['OUT-09', 'Backorder & Promise Replanning'],
      ['OUT-10', 'Shipment Tracking / POD Timeline'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('outbound', capabilityId);
      const panel = view.getByTestId('outbound-capability-mock-' + capabilityId);
      expect(panel).toBeTruthy();
      expect(panel.textContent).toContain(title);
      expect(panel.textContent).toContain('Không gọi API production');
      view.unmount();
    }
  });

  it('does not shadow live or already-specialized outbound screens with another domain panel', () => {
    for (const capabilityId of ['OUT-01', 'OUT-02', 'OUT-05', 'OUT-06']) {
      const view = renderPreview('outbound', capabilityId);
      expect(view.queryByTestId('outbound-capability-mock-' + capabilityId)).toBeNull();
      view.unmount();
    }
  });

  it('renders specialized inventory-control mocks for planned capabilities that still need domain panels', () => {
    const cases = [
      ['INV-05', 'Inventory Status Eligibility Board'],
      ['INV-06', 'Lot / Serial / Expiry Explorer'],
      ['INV-07', 'Inventory Lock / Freeze Policy'],
      ['INV-09', 'Inventory Reversal / Corrective Chain'],
      ['INV-10', 'Traceability & Genealogy Graph'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('inventory-control', capabilityId);
      const panel = view.getByTestId('inventory-capability-mock-' + capabilityId);
      expect(panel).toBeTruthy();
      expect(panel.textContent).toContain(title);
      expect(panel.textContent).toContain('Không gọi API production');
      view.unmount();
    }
  });

  it('does not shadow foundation or Screen Matrix inventory-control surfaces', () => {
    for (const capabilityId of ['INV-01', 'INV-02', 'INV-03', 'INV-04', 'INV-08', 'INV-11']) {
      const view = renderPreview('inventory-control', capabilityId);
      expect(view.queryByTestId('inventory-capability-mock-' + capabilityId)).toBeNull();
      view.unmount();
    }
  });

  it('renders specialized transfer-replenishment mocks for the remaining planned capabilities', () => {
    const cases = [
      ['TR-02', 'In-Transit Inventory Reconciliation'],
      ['TR-05', 'Replenishment Source & Pick-Face Plan'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('transfer-replenishment', capabilityId);
      const panel = view.getByTestId('transfer-capability-mock-' + capabilityId);
      expect(panel).toBeTruthy();
      expect(panel.textContent).toContain(title);
      expect(panel.textContent).toContain('Không gọi API production');
      view.unmount();
    }
  });

  it('does not shadow live or foundation transfer surfaces', () => {
    for (const capabilityId of ['TR-01', 'TR-03', 'TR-04']) {
      const view = renderPreview('transfer-replenishment', capabilityId);
      expect(view.queryByTestId('transfer-capability-mock-' + capabilityId)).toBeNull();
      view.unmount();
    }
  });

  it('renders specialized count-adjustment mocks only for planned capabilities', () => {
    const cases = [
      ['CT-03', 'Blind Count Execution'],
      ['CT-04', 'Count Freeze Strategy'],
      ['CT-05', 'Recount & Immutable Attempt History'],
      ['CT-06', 'Variance Resolution Workbench'],
      ['CT-07', 'Inventory Adjustment Approval & Posting'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('count-adjustment', capabilityId);
      const panel = view.getByTestId('count-capability-mock-' + capabilityId);
      expect(panel).toBeTruthy();
      expect(panel.textContent).toContain(title);
      expect(panel.textContent).toContain('Không gọi API production');
      view.unmount();
    }
  });

  it('does not shadow live or foundation count surfaces', () => {
    for (const capabilityId of ['CT-01', 'CT-02']) {
      const view = renderPreview('count-adjustment', capabilityId);
      expect(view.queryByTestId('count-capability-mock-' + capabilityId)).toBeNull();
      view.unmount();
    }
  });

  it('renders scan-first phone preview for mobile capabilities', () => {
    const view = renderPreview('mobile', 'MO-04');
    expect(view.getByText('Mobile scan-first preview')).toBeTruthy();
    expect(view.getByText('▣ Quét barcode / location / serial')).toBeTruthy();
    expect(view.getByText('Xác nhận • Mock read-only')).toBeTruthy();
    expect(view.getAllByText('FX-MO-04').length).toBeGreaterThan(0);
  });

  it('hides out-of-scope fixture samples after persona warehouse scope changes', () => {
    const PersonaSwitch = () => {
      const demo = useMockDemo();
      return <button type="button" onClick={() => demo.setSelectedUserCode('U-DN-MGR')}>Use DN persona</button>;
    };
    const view = render(
      <MemoryRouter initialEntries={['/system-blueprint/inbound/IN-03']}>
        <MockDemoProvider>
          <PersonaSwitch />
          <Routes>
            <Route path="/system-blueprint/:moduleKey/:capabilityId" element={<CapabilityPreview />} />
          </Routes>
        </MockDemoProvider>
      </MemoryRouter>,
    );
    fireEvent.click(view.getByText('Use DN persona'));
    const hiddenFixture = view.getByText('Sample record ẩn bởi simulated warehouse scope');
    expect(hiddenFixture).toBeTruthy();
    expect(view.getAllByText('GR-2026-1041').length).toBeGreaterThan(0);
    expect(hiddenFixture.closest('.capability-fixture-trace')?.textContent).not.toContain('GR-2026-1045');
  });

  it('shows the active shared scenario on linked capability previews', () => {
    const ScenarioStarter = () => {
      const demo = useMockDemo();
      return <button type="button" onClick={() => demo.runScenarioStep('GS-01')}>Run GS01 shared</button>;
    };
    const view = render(
      <MemoryRouter initialEntries={['/system-blueprint/inbound/IN-07']}>
        <MockDemoProvider>
          <ScenarioStarter />
          <Routes>
            <Route path="/system-blueprint/:moduleKey/:capabilityId" element={<CapabilityPreview />} />
          </Routes>
        </MockDemoProvider>
      </MemoryRouter>,
    );

    fireEvent.click(view.getByText('Run GS01 shared'));
    expect(view.getByTestId('shared-scenario-banner')).toBeTruthy();
    expect(view.getByText(/GS-01 • Inbound Receipt → Post → Putaway/)).toBeTruthy();
    expect(view.getByText(/Step 1 • trạng thái này dùng chung/)).toBeTruthy();
  });

  it('renders canonical governance metadata and specialized review-required screen content', () => {
    const picking = renderPreview('outbound', 'OUT-05');
    expect(picking.getByText('Picking Workbench / Scan Flow')).toBeTruthy();
    expect(picking.getByText('Screen Matrix • Traceability Closed')).toBeTruthy();
    expect(picking.getByText('Capability governance & completeness')).toBeTruthy();
    expect(picking.getAllByText('Wave 2').length).toBeGreaterThan(0);
    expect(picking.getByText(/Picking không được giảm warehouse OnHand/)).toBeTruthy();
    expect(picking.getByText(/Traceability Closed — Screen Matrix 229/)).toBeTruthy();
  });

  it('supports interactive core WMS state transitions and exception recovery', () => {
    const view = renderPreview('outbound', 'OUT-08');
    expect(view.getByTestId('interactive-capability-demo')).toBeTruthy();
    fireEvent.click(view.getByText('Thực hiện bước tiếp theo'));
    expect(view.getByText('State → LOAD_READY')).toBeTruthy();

    fireEvent.click(view.getByText('Mô phỏng ngoại lệ'));
    expect(view.getByText('DISPATCH_INVENTORY_CONFLICT')).toBeTruthy();
    fireEvent.click(view.getByText('Giải quyết ngoại lệ'));
    expect(view.getByText(/Exception resolved/)).toBeTruthy();
  });

  it('shows domain-specific warehouse lifecycle instead of a spec-only card', () => {
    const view = renderPreview('warehouse-structure', 'WH-06');
    expect(view.getAllByText(/Dock & Yard Control/).length).toBeGreaterThan(0);
    expect(view.getAllByText('CHECKED_IN').length).toBeGreaterThan(0);
    expect(view.getByText('ASSIGN_DOCK')).toBeTruthy();
    fireEvent.click(view.getByText('Mô phỏng ngoại lệ'));
    expect(view.getByText('DOCK_DOUBLE_ASSIGNMENT')).toBeTruthy();
  });

  it('keeps quantity conversion interactive for core execution demos', () => {
    const view = renderPreview('inventory-control', 'INV-08');
    const quantity = view.getByLabelText('Move quantity') as HTMLInputElement;
    fireEvent.change(quantity, { target: { value: '7' } });
    expect(view.getByText('84 Cái')).toBeTruthy();
  });

  it('renders a dedicated enterprise SSO simulator for the remaining planned capability tranche', () => {
    const view = renderPreview('administration', 'AD-08');
    expect(view.getAllByText('SSO / Identity Federation').length).toBeGreaterThan(0);
    expect(view.getByText('TEST_SSO')).toBeTruthy();
    fireEvent.click(view.getByText('Mô phỏng ngoại lệ'));
    expect(view.getByText('SSO_MAPPING_AMBIGUOUS')).toBeTruthy();
  });

  it('shows safe not-found UI for invalid capability', () => {
    const view = renderPreview('outbound', 'OUT-DOES-NOT-EXIST');
    expect(view.getByText('Không tìm thấy capability')).toBeTruthy();
  });
});
