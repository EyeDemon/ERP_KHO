// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import MockScenarioLab from './MockScenarioLab';
import { MockDemoProvider } from '../context/MockDemoContext';

const renderLab = () => render(<MockDemoProvider><MemoryRouter><MockScenarioLab /></MemoryRouter></MockDemoProvider>);

describe('MockScenarioLab', () => {
  afterEach(cleanup);

  it('renders canonical inbound scenario with steps and assertions', () => {
    const view = renderLab();
    expect(view.getAllByText('GS-01').length).toBeGreaterThan(0);
    expect(view.getByText(/22 kịch bản bao phủ tồn kho cốt lõi/)).toBeTruthy();
    expect(view.getAllByText('Nhận hàng nhập → Ghi sổ → Cất hàng').length).toBeGreaterThan(0);
    expect(view.getByText('Ghi sổ phiếu nhập')).toBeTruthy();
    expect(view.getByText('Sổ cái phiếu nhập chỉ ghi đúng một lần')).toBeTruthy();
    expect(view.getAllByText('100', { selector: 'strong' }).length).toBeGreaterThan(0);
  });

  it('simulates scenario steps locally and resets execution state', () => {
    const view = renderLab();
    const log = view.getByTestId('scenario-execution-log');
    expect(log.textContent).toContain('Chưa chạy bước nào');
    fireEvent.click(view.getByText('Chạy bước tiếp'));
    expect(log.textContent).toContain('Nhận 100');
    expect(log.textContent).toContain('Không tăng Tồn thực tế toàn kho');
    fireEvent.click(view.getByText('Đặt lại'));
    expect(log.textContent).toContain('Chưa chạy bước nào');
  });

  it('publishes core inventory state into the shared scenario session', () => {
    const view = renderLab();
    fireEvent.click(view.getByText('Chạy bước tiếp'));
    expect(view.getByText(/PHIÊN KỊCH BẢN DÙNG CHUNG/)).toBeTruthy();
    expect(view.getByText(/5 chức năng liên quan/)).toBeTruthy();
    expect(view.getByRole('link', { name: 'IN-07' }).getAttribute('href')).toBe('/system-blueprint/inbound/IN-07');
    expect(view.getAllByText(/Tồn thực tế 0 • Khả dụng 0 • Đang vận chuyển 0/).length).toBeGreaterThan(0);

    fireEvent.click(view.getByText('Chạy bước tiếp'));
    expect(view.getAllByText(/Tồn thực tế 100 • Khả dụng 100 • Đang vận chuyển 0/).length).toBeGreaterThan(0);
  });

  it('switches to concurrency and idempotency scenarios', () => {
    const view = renderLab();
    fireEvent.click(view.getByRole('button', { name: /Giữ hàng đồng thời/ }));
    expect(view.getByText('Chính xác một yêu cầu thành công')).toBeTruthy();
    expect(view.getByText('409 INV_INSUFFICIENT_AVAILABLE')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: /Xác nhận giao hàng có tính idempotent/ }));
    expect(view.getByText('5 yêu cầu → 1 lần xác nhận giao')).toBeTruthy();
    expect(view.getByText('Thử lại x4')).toBeTruthy();
  });

  it('opens advanced automation and finance scenarios', () => {
    const view = renderLab();
    fireEvent.click(view.getByRole('button', { name: /Đóng kỳ/ }));
    expect(view.getByText('Giữ nguyên sự thật số lượng WMS')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: /WCS \/ Robot/ }));
    expect(view.getByText('Callback thiết bị không phải sự thật tồn kho')).toBeTruthy();
  });

  it('covers returns and offline deferred synchronization', () => {
    const view = renderLab();
    fireEvent.click(view.getByRole('button', { name: /Kiểm tra & ghi sổ hàng khách trả/ }));
    expect(view.getByText('RESTOCK + QUARANTINE = 6')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: /Đồng bộ trì hoãn khi thiết bị di động ngoại tuyến/ }));
    expect(view.getByText('Lệnh rủi ro cao bị chặn khi ngoại tuyến')).toBeTruthy();
    expect(view.getByText('BLOCKED_OFFLINE')).toBeTruthy();
  });
});
