// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import SystemBlueprint from './SystemBlueprint';
import { mockRecordCount } from '../mocks/erpWmsMockData';

describe('SystemBlueprint', () => {
  afterEach(cleanup);

  it('renders the complete module map and mock dataset summary', () => {
    const view = render(<MemoryRouter><SystemBlueprint /></MemoryRouter>);
    expect(view.getByText('Bản đồ chức năng ERP/WMS hoàn chỉnh')).toBeTruthy();
    expect(view.getByText(`${mockRecordCount}`)).toBeTruthy();
    expect(view.getAllByText('Nhập kho').length).toBeGreaterThan(0);
    expect(view.getAllByText('Xuất kho').length).toBeGreaterThan(0);
    expect(view.getAllByText('Mobile WMS').length).toBeGreaterThan(0);
    expect(view.getAllByText('Advanced WMS & Planning').length).toBeGreaterThan(0);
  });

  it('filters capability cards by text and implementation status', () => {
    const view = render(<MemoryRouter><SystemBlueprint /></MemoryRouter>);
    const search = view.getByPlaceholderText('Tìm chức năng, mã capability, spec...');
    fireEvent.change(search, { target: { value: 'Receipt Posting' } });
    expect(view.getByText('Receipt Posting')).toBeTruthy();
    expect(view.queryByText('Shipment Dispatch')).toBeNull();

    fireEvent.change(search, { target: { value: '' } });
    const statusSelect = view.getAllByRole('combobox')[0];
    fireEvent.change(statusSelect, { target: { value: 'live' } });
    expect(view.getByText('Sản phẩm / SKU')).toBeTruthy();
    expect(view.queryByText('Allocation')).toBeNull();
  });

  it('links every visible module to its mock work center preview', () => {
    const view = render(<MemoryRouter><SystemBlueprint /></MemoryRouter>);
    const links = view.getAllByText('Xem work center →');
    expect(links.length).toBeGreaterThanOrEqual(10);
    expect(links[0].getAttribute('href')).toContain('/system-blueprint/');
  });
});
