// @vitest-environment jsdom
import { cleanup, fireEvent, render, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import WarehouseStructure from './WarehouseStructure';

afterEach(cleanup);

describe('Cấu trúc vị trí kho mock-only', () => {
  it('renders canonical hierarchy, system, unmapped and lifecycle examples without mutation controls', () => {
    const view = render(<WarehouseStructure />);

    expect(view.getByText(/frontend mock-only/i)).toBeTruthy();
    expect(view.getAllByText('A01-R02-L03-B04').length).toBeGreaterThan(0);
    expect(view.getAllByText('ZONE-A / A01 / R02 / L03 / B04').length).toBeGreaterThan(0);
    expect(view.getByText('RECEIVING')).toBeTruthy();
    expect(view.getByText('LEGACY')).toBeTruthy();
    expect(view.getByText('LEGACY-UNMAPPED-01')).toBeTruthy();
    expect(view.getAllByText('Bị khóa').length).toBeGreaterThan(0);
    expect(view.getAllByText('Ngừng hoạt động').length).toBeGreaterThan(0);

    const damagedRow = view.getAllByText('A01-R02-L03-B08')[0].closest('tr');
    const rejectedRow = view.getAllByText('A01-R02-L03-B09')[0].closest('tr');
    expect(damagedRow).toBeTruthy();
    expect(rejectedRow).toBeTruthy();
    expect(within(damagedRow as HTMLTableRowElement).getByText('Không pick')).toBeTruthy();
    expect(within(rejectedRow as HTMLTableRowElement).getByText('Không pick')).toBeTruthy();

    expect(view.queryByText('Thêm cấu trúc vật lý')).toBeNull();
    expect(view.queryByText('Tạo vị trí')).toBeNull();
    expect(view.queryByText('Lưu vị trí')).toBeNull();
  });

  it('filters local mock data without requiring a WH-02 API', () => {
    const view = render(<WarehouseStructure />);

    fireEvent.change(view.getByLabelText('Trạng thái vị trí'), { target: { value: 'blocked' } });
    const table = view.getByRole('table', { name: 'Vị trí vật lý theo bộ lọc' });
    expect(within(table).getByText('A01-R02-L03-B06')).toBeTruthy();
    expect(within(table).queryByText('A01-R02-L03-B04')).toBeNull();

    fireEvent.change(view.getByLabelText('Trạng thái vị trí'), { target: { value: 'all' } });
    fireEvent.change(view.getByLabelText('Loại vị trí'), { target: { value: 'Damaged' } });
    expect(within(table).getByText('A01-R02-L03-B08')).toBeTruthy();
    expect(within(table).queryByText('A01-R02-L03-B09')).toBeNull();
  });
});
