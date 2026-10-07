// @vitest-environment jsdom
import { cleanup, fireEvent, render, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import SystemCoverage from './SystemCoverage';

describe('SystemCoverage', () => {
  afterEach(cleanup);

  it('renders all traced capabilities and the four closed Screen Matrix mappings', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    expect(view.getByText('Độ phủ & mức sẵn sàng')).toBeTruthy();
    expect(view.getByText('chức năng đã truy vết').parentElement?.textContent).toContain('179');
    expect(view.getByText('Ma trận màn hình 229 • Đã đóng truy vết')).toBeTruthy();
    expect(view.getAllByText('Đã đóng truy vết').length).toBeGreaterThanOrEqual(4);
    expect(view.getByText(/inventory-control.*INV-08/)).toBeTruthy();
    expect(view.getByText(/outbound.*OUT-05/)).toBeTruthy();
    expect(view.getByText(/outbound.*OUT-06/)).toBeTruthy();
    expect(view.getByText(/transfer-replenishment.*TR-01/)).toBeTruthy();
    expect(view.queryByText(/UNMAPPED/)).toBeNull();
    expect(view.getAllByText('282').length).toBeGreaterThan(0);
    expect(view.getByText('Sổ đăng ký tài liệu chuẩn • Đặc tả 1–282')).toBeTruthy();
  });

  it('filters by release wave and implementation status', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    const table = within(view.getByTestId('coverage-table'));
    fireEvent.change(view.getByLabelText('Lọc đợt phát hành'), { target: { value: '5' } });
    expect(table.getByText(/AX-09 • Kitting/)).toBeTruthy();
    expect(table.queryByText(/INV-02 • Sổ cái tồn kho bất biến/)).toBeNull();

    fireEvent.change(view.getByLabelText('Lọc đợt phát hành'), { target: { value: 'all' } });
    fireEvent.change(view.getByLabelText('Lọc trạng thái triển khai'), { target: { value: 'live' } });
    expect(table.getByText(/TR-01 • Warehouse Transfer/)).toBeTruthy();
    expect(table.queryByText(/OUT-05 • Picking/)).toBeNull();
  });

  it('shows advanced canonical capabilities without promoting them to core implementation', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    const table = within(view.getByTestId('coverage-table'));
    fireEvent.change(view.getByLabelText('Tìm độ phủ'), { target: { value: 'Warehouse Safety' } });
    expect(table.getByText(/AX-14 • Warehouse Safety/)).toBeTruthy();
    expect(table.getByText(/Tùy chọn theo ngành/)).toBeTruthy();
  });

  it('indexes the complete canonical documentation set separately from capability readiness', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    expect(view.getByText(/Xem toàn bộ 282 tài liệu chuẩn/)).toBeTruthy();
  });

  it('supports capability text search', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    const table = within(view.getByTestId('coverage-table'));
    fireEvent.change(view.getByLabelText('Tìm độ phủ'), { target: { value: 'toàn vẹn' } });
    expect(table.getByText(/INV-11 • Bộ máy toàn vẹn/)).toBeTruthy();
    expect(table.queryByText(/OUT-05 • Picking/)).toBeNull();
  });
});
