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
    expect(view.getByText('Shipment Dispatch')).toBeTruthy();
    expect(view.getByText('MOCK / SPEC PREVIEW')).toBeTruthy();
    expect(view.getByText(/Dispatch mới giảm OnHand|Boundary trừ OnHand/)).toBeTruthy();
    expect(view.getByText('SHP-2026-5108')).toBeTruthy();
    expect(view.getByText('Expected UX states')).toBeTruthy();
    expect(view.getByText('Technical contract')).toBeTruthy();
  });

  it('renders a live capability with link to the real screen', () => {
    const view = renderPreview('master-data', 'MD-01');
    expect(view.getByText('Sản phẩm / SKU')).toBeTruthy();
    const link = view.getByText('Mở chức năng hiện có');
    expect(link.getAttribute('href')).toBe('/products');
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
    expect(view.getByText('Sample record ẩn bởi simulated warehouse scope')).toBeTruthy();
    expect(view.getByText('GR-2026-1041')).toBeTruthy();
    expect(view.queryByText('GR-2026-1045')).toBeNull();
  });

  it('renders canonical governance metadata and specialized review-required screen content', () => {
    const picking = renderPreview('outbound', 'OUT-05');
    expect(picking.getByText('Picking Workbench / Scan Flow')).toBeTruthy();
    expect(picking.getByText('Screen Matrix • Review Required')).toBeTruthy();
    expect(picking.getByText('Capability governance & completeness')).toBeTruthy();
    expect(picking.getAllByText('Wave 2').length).toBeGreaterThan(0);
    expect(picking.getByText(/Picking không được giảm warehouse OnHand/)).toBeTruthy();
  });

  it('shows safe not-found UI for invalid capability', () => {
    const view = renderPreview('outbound', 'OUT-DOES-NOT-EXIST');
    expect(view.getByText('Không tìm thấy capability')).toBeTruthy();
  });
});
