// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ModuleBlueprint from './ModuleBlueprint';

const renderModule = (key: string) => render(
  <MemoryRouter initialEntries={[`/system-blueprint/${key}`]}>
    <Routes>
      <Route path="/system-blueprint/:moduleKey" element={<ModuleBlueprint />} />
    </Routes>
  </MemoryRouter>,
);

describe('ModuleBlueprint mock work center', () => {
  afterEach(cleanup);

  it('renders realistic inbound mock records and module coverage', () => {
    const view = renderModule('inbound');
    expect(view.getByText('GR-2026-1048')).toBeTruthy();
    expect(view.getByText('ASN-2026-0812')).toBeTruthy();
    expect(view.getByText('PUT-2026-3321')).toBeTruthy();
    expect(view.getByText('Goods Receipt Work Center')).toBeTruthy();
    expect(view.getByText('WORK CENTER • MOCK DATA')).toBeTruthy();
  });

  it('switches selected record detail without mutating the dataset', () => {
    const view = renderModule('inbound');
    expect(view.getAllByText('GR-2026-1048').length).toBeGreaterThan(0);
    const row = view.getByText('GR-2026-1045').closest('tr');
    expect(row).toBeTruthy();
    const button = row?.querySelector('button');
    expect(button).toBeTruthy();
    fireEvent.click(button!);
    expect(view.getAllByText('48 Cái').length).toBeGreaterThan(0);
    expect(view.getAllByText('QC-01').length).toBeGreaterThan(0);
  });

  it('renders transfer and mobile datasets from separate work centers', () => {
    const transfer = renderModule('transfer-replenishment');
    expect(transfer.getByText('TRF-2026-0018')).toBeTruthy();
    expect(transfer.getByText('REPL-2026-661')).toBeTruthy();
    transfer.unmount();

    const mobile = renderModule('mobile');
    expect(mobile.getByText('MOB-RCV-1048')).toBeTruthy();
    expect(mobile.getByText('OFF-QUEUE-021')).toBeTruthy();
    expect(mobile.getByText('SYNC-FAIL-008')).toBeTruthy();
  });

  it('filters mock records by search, warehouse and status and can reset filters', () => {
    const view = renderModule('inbound');
    const search = view.getByLabelText('Tìm trong work center');
    fireEvent.change(search, { target: { value: 'GR-2026-1045' } });
    expect(view.getByText('GR-2026-1045')).toBeTruthy();
    expect(view.queryByText('ASN-2026-0812')).toBeNull();

    fireEvent.click(view.getByText('Xóa lọc'));
    expect(view.getByText('ASN-2026-0812')).toBeTruthy();

    fireEvent.change(view.getByLabelText('Kho'), { target: { value: 'WH-DN-01' } });
    expect(view.getByText('GR-2026-1041')).toBeTruthy();
    expect(view.queryByText('GR-2026-1048')).toBeNull();

    fireEvent.click(view.getByText('Xóa lọc'));
    fireEvent.change(view.getByLabelText('Trạng thái'), { target: { value: 'QC_PENDING' } });
    expect(view.getByText('GR-2026-1045')).toBeTruthy();
    expect(view.queryByText('GR-2026-1041')).toBeNull();
  });

  it('shows a safe not-found state for an unknown module', () => {
    const view = renderModule('does-not-exist');
    expect(view.getByText('Không tìm thấy module')).toBeTruthy();
  });
});
