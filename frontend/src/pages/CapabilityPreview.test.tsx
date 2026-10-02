// @vitest-environment jsdom
import { cleanup, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import CapabilityPreview from './CapabilityPreview';

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

  it('shows safe not-found UI for invalid capability', () => {
    const view = renderPreview('outbound', 'OUT-DOES-NOT-EXIST');
    expect(view.getByText('Không tìm thấy capability')).toBeTruthy();
  });
});
