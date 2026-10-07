// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import MockDataLab from './MockDataLab';
import { MockDemoProvider, useMockDemo } from '../context/MockDemoContext';

const ScenarioStarter = () => {
  const demo = useMockDemo();
  return <button type="button" onClick={() => demo.runScenarioStep('GS-01')}>Run shared GS01</button>;
};

describe('MockDataLab', () => {
  afterEach(cleanup);

  it('renders warehouse fixtures and switches across linked datasets', () => {
    const view = render(<MemoryRouter><MockDataLab /></MemoryRouter>);
    expect(view.getByText('WH-HCM-01')).toBeTruthy();
    expect(view.getByText('DC Hồ Chí Minh')).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: 'Sản phẩm & mã vạch' }));
    expect(view.getByText('SKU-1001')).toBeTruthy();
    expect(view.getByText('8938501000011, 8938501000012')).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: '179 bộ dữ liệu chức năng' }));
    expect(view.getByText('FX-OUT-08')).toBeTruthy();
    expect(view.getByText(/OUT-08 — Xác nhận giao hàng/)).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: 'Nhóm tồn kho' }));
    expect(view.getByText('1250')).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: 'Bảo toàn điều chuyển' }));
    expect(view.getAllByText('ĐẠT').length).toBeGreaterThanOrEqual(3);

    fireEvent.click(view.getByRole('tab', { name: 'Các lần kiểm đếm lại' }));
    expect(view.getByText(/#1: 1242/)).toBeTruthy();
    expect(view.getByText(/#3: 1248 ✓/)).toBeTruthy();
  });

  it('shows the same shared scenario balances produced outside the data lab', () => {
    const view = render(
      <MockDemoProvider>
        <MemoryRouter>
          <ScenarioStarter />
          <MockDataLab />
        </MemoryRouter>
      </MockDemoProvider>,
    );

    fireEvent.click(view.getByText('Run shared GS01'));
    fireEvent.click(view.getByText('Run shared GS01'));
    fireEvent.click(view.getByRole('tab', { name: 'Phiên kịch bản dùng chung' }));

    expect(view.getByText(/GS-01 • Inbound Receipt → Post → Putaway/)).toBeTruthy();
    expect(view.getByText('RECV-01')).toBeTruthy();
    expect(view.getAllByText('100').length).toBeGreaterThan(0);
    expect(view.getByText('Ghi sổ phiếu nhập')).toBeTruthy();
  });
});
