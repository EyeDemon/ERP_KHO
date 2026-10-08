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

  it('renders OUT-08 foundation with production navigation and read-only Blueprint evidence', () => {
    const view = renderPreview('outbound', 'OUT-08');
    expect(view.getAllByText('Xác nhận giao hàng').length).toBeGreaterThan(0);
    expect(view.getByText('ĐÃ CÓ TRÊN HỆ THỐNG THẬT')).toBeTruthy();
    expect(view.getByText(/canonical Shipment LOADED dispatch/i)).toBeTruthy();
    expect(view.getAllByText('SHP-2026-5108').length).toBeGreaterThan(0);
    expect(view.getByText('Các trạng thái UX dự kiến')).toBeTruthy();
    expect(view.getByText('Hợp đồng kỹ thuật')).toBeTruthy();
  }, 10_000);

  it('keeps live production navigation outside the Blueprint preview', () => {
    const view = renderPreview('master-data', 'MD-01');
    expect(view.getByText('Sản phẩm / SKU')).toBeTruthy();
    expect(view.getByText('ĐÃ CÓ TRÊN HỆ THỐNG THẬT')).toBeTruthy();
    expect(view.queryByText('Mở chức năng hiện có')).toBeNull();
  });

  it('keeps the WH-02 specialized mock separate after the production route goes live', () => {
    const view = renderPreview('warehouse-structure', 'WH-02');
    const link = view.getByText('Mở mô phỏng chuyên biệt');
    expect(link.getAttribute('href')).toBe('/system-blueprint/warehouse-structure/WH-02/workbench');
    expect(view.queryByText('Mở chức năng hiện có')).toBeNull();
  });

  it('renders domain-specific warehouse mock panels for WH-03 through WH-07', () => {
    const cases = [
      ['WH-03', 'Sức chứa vị trí & Ràng buộc lưu trữ'],
      ['WH-04', 'Bản đồ kho & Bản đồ nhiệt'],
      ['WH-05', 'Lịch vận hành & Ca làm việc'],
      ['WH-06', 'Bảng điều hành cửa kho & sân bãi'],
      ['WH-07', 'Thứ tự ưu tiên ngoại lệ lịch vận hành'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('warehouse-structure', capabilityId);
      expect(view.getByTestId('warehouse-capability-mock-' + capabilityId)).toBeTruthy();
      expect(view.getByRole('heading', { name: title })).toBeTruthy();
      expect(view.getAllByText(/Không gọi API hệ thống thật|Production effect/).length).toBeGreaterThan(0);
      view.unmount();
    }
  });

  it('keeps the remaining planned inbound capability explicitly non-production', () => {
    const view = renderPreview('inbound', 'IN-09');
    expect(view.getByTestId('inbound-capability-mock-IN-09')).toBeTruthy();
    expect(view.getByRole('heading', { name: 'Giải thích khuyến nghị cất hàng' })).toBeTruthy();
    expect(view.getByText(/Không gọi API hệ thống thật/)).toBeTruthy();
    expect(view.getAllByText('Chưa triển khai hệ thống thật').length).toBeGreaterThan(0);
  });

  it('reflects merged inbound execution capabilities as live while keeping Blueprint mocks read-only', () => {
    const cases = [
      ['IN-05', 'Xử lý nhận thừa / thiếu'],
      ['IN-06', 'Kiểm tra chất lượng hàng nhập'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('inbound', capabilityId);
      expect(view.getByText('Đã triển khai hoàn thiện phạm vi hiện tại')).toBeTruthy();
      expect(view.getByText('ĐÃ CÓ TRÊN HỆ THỐNG THẬT')).toBeTruthy();
      expect(view.getByTestId('inbound-capability-mock-' + capabilityId)).toBeTruthy();
      expect(view.getByRole('heading', { name: title })).toBeTruthy();
      expect(view.getByText(/Không gọi API hệ thống thật/)).toBeTruthy();
      expect(view.queryByText('Chưa triển khai hệ thống thật')).toBeNull();
      view.unmount();
    }
  });

  it('shows IN-02 as foundation because Dock/Yard appointments are not yet linked canonically to inbound documents', () => {
    const view = renderPreview('inbound', 'IN-02');
    expect(view.getByText('Đã triển khai một phần / còn thiếu phạm vi')).toBeTruthy();
    expect(view.getByText('ĐÃ CÓ TRÊN HỆ THỐNG THẬT')).toBeTruthy();
    expect(view.getByTestId('inbound-capability-mock-IN-02')).toBeTruthy();
    expect(view.getByRole('heading', { name: 'Bảng lịch nhận hàng' })).toBeTruthy();
    expect(view.getAllByText(/liên kết canonical.*PO\/ASN\/Receipt.*hoàn thiện/i).length).toBeGreaterThan(0);
  });

  it('shows IN-01 as live while keeping its Blueprint mock read-only and separate', () => {
    const view = renderPreview('inbound', 'IN-01');
    expect(view.getByText('Đã triển khai hoàn thiện phạm vi hiện tại')).toBeTruthy();
    expect(view.getByText('ĐÃ CÓ TRÊN HỆ THỐNG THẬT')).toBeTruthy();
    expect(view.getByTestId('inbound-capability-mock-IN-01')).toBeTruthy();
    expect(view.getByRole('heading', { name: 'Đối chiếu Đơn mua / ASN' })).toBeTruthy();
    expect(view.getByText(/Không gọi API hệ thống thật/)).toBeTruthy();
    expect(view.queryByText('Chưa triển khai hệ thống thật')).toBeNull();
  });

  it('does not shadow live inbound work centers that already have production surfaces', () => {
    for (const capabilityId of ['IN-03', 'IN-04', 'IN-07', 'IN-08']) {
      const view = renderPreview('inbound', capabilityId);
      expect(view.queryByTestId('inbound-capability-mock-' + capabilityId)).toBeNull();
      view.unmount();
    }
  });

  it('renders specialized outbound mocks only for capabilities that still need domain workbenches', () => {
    const cases = [
      ['OUT-03', 'Bàn làm việc ứng viên phân bổ'],
      ['OUT-04', 'Lập kế hoạch đợt / lô / cụm'],
      ['OUT-07', 'Kiểm soát khu chờ & xếp hàng'],
      ['OUT-08', 'Ranh giới xác nhận giao hàng'],
      ['OUT-09', 'Đơn thiếu hàng & lập lại cam kết'],
      ['OUT-10', 'Dòng thời gian theo dõi giao hàng / POD'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('outbound', capabilityId);
      const panel = view.getByTestId('outbound-capability-mock-' + capabilityId);
      expect(panel).toBeTruthy();
      expect(panel.textContent).toContain(title);
      expect(panel.textContent).toContain('Không gọi API hệ thống thật');
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
      ['INV-05', 'Bảng điều kiện theo trạng thái tồn kho'],
      ['INV-06', 'Trình khám phá Lô / Sê-ri / Hạn dùng'],
      ['INV-07', 'Chính sách khóa / đóng băng tồn kho'],
      ['INV-09', 'Chuỗi đảo / hiệu chỉnh tồn kho'],
      ['INV-10', 'Đồ thị truy vết & phả hệ'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('inventory-control', capabilityId);
      const panel = view.getByTestId('inventory-capability-mock-' + capabilityId);
      expect(panel).toBeTruthy();
      expect(panel.textContent).toContain(title);
      expect(panel.textContent).toContain('Không gọi API hệ thống thật');
      view.unmount();
    }
  });

  it('does not shadow foundation or Ma trận màn hình inventory-control surfaces', () => {
    for (const capabilityId of ['INV-01', 'INV-02', 'INV-03', 'INV-04', 'INV-08', 'INV-11']) {
      const view = renderPreview('inventory-control', capabilityId);
      expect(view.queryByTestId('inventory-capability-mock-' + capabilityId)).toBeNull();
      view.unmount();
    }
  });

  it('renders specialized transfer-replenishment mocks for the remaining planned capabilities', () => {
    const cases = [
      ['TR-02', 'Đối chiếu tồn kho đang vận chuyển'],
      ['TR-05', 'Kế hoạch nguồn bổ sung & vị trí lấy hàng'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('transfer-replenishment', capabilityId);
      const panel = view.getByTestId('transfer-capability-mock-' + capabilityId);
      expect(panel).toBeTruthy();
      expect(panel.textContent).toContain(title);
      expect(panel.textContent).toContain('Không gọi API hệ thống thật');
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
      ['CT-03', 'Thực hiện kiểm kê mù'],
      ['CT-04', 'Chiến lược đóng băng kiểm kê'],
      ['CT-05', 'Kiểm đếm lại & lịch sử lần đếm bất biến'],
      ['CT-06', 'Bàn làm việc xử lý chênh lệch'],
      ['CT-07', 'Phê duyệt & ghi sổ điều chỉnh tồn kho'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('count-adjustment', capabilityId);
      const panel = view.getByTestId('count-capability-mock-' + capabilityId);
      expect(panel).toBeTruthy();
      expect(panel.textContent).toContain(title);
      expect(panel.textContent).toContain('Không gọi API hệ thống thật');
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

  it('renders specialized quality-return mocks for all planned QR capabilities', () => {
    const cases = [
      ['QR-01', 'Trung tâm công việc kiểm tra chất lượng'],
      ['QR-02', 'Điều kiện giữ QC / Cách ly'],
      ['QR-03', 'Phân loại xử lý tồn kho hư hỏng'],
      ['QR-04', 'Bàn làm việc trả hàng khách / RMA'],
      ['QR-05', 'Phạm vi ảnh hưởng & kiểm soát thu hồi'],
      ['QR-06', 'Phê duyệt tiêu hủy & ghi sổ tồn kho'],
    ];

    for (const [capabilityId, title] of cases) {
      const view = renderPreview('quality-returns', capabilityId);
      const panel = view.getByTestId('quality-capability-mock-' + capabilityId);
      expect(panel).toBeTruthy();
      expect(panel.textContent).toContain(title);
      expect(panel.textContent).toContain('Không gọi API hệ thống thật');
      expect(view.getAllByText('Chưa triển khai hệ thống thật').length).toBeGreaterThan(0);
      view.unmount();
    }
  });

  it('renders scan-first phone preview for mobile capabilities', () => {
    const view = renderPreview('mobile', 'MO-04');
    expect(view.getByText('Xem trước luồng quét trên di động')).toBeTruthy();
    expect(view.getByText('▣ Quét mã vạch / vị trí / sê-ri')).toBeTruthy();
    expect(view.getByText('Xác nhận • Mô phỏng chỉ đọc')).toBeTruthy();
    expect(view.getAllByText('FX-MO-04').length).toBeGreaterThan(0);
  });

  it('hides out-of-scope fixture samples after persona warehouse scope changes', () => {
    const PersonaSwitch = () => {
      const demo = useMockDemo();
      return <button type="button" onClick={() => demo.setSelectedUserCode('U-DN-MGR')}>Dùng vai trò Đà Nẵng</button>;
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
    fireEvent.click(view.getByText('Dùng vai trò Đà Nẵng'));
    const hiddenFixture = view.getByText('Bản ghi mẫu bị ẩn bởi phạm vi kho mô phỏng');
    expect(hiddenFixture).toBeTruthy();
    expect(view.getAllByText('GR-2026-1041').length).toBeGreaterThan(0);
    expect(hiddenFixture.closest('.capability-fixture-trace')?.textContent).not.toContain('GR-2026-1045');
  });

  it('shows the active shared scenario on linked capability previews', () => {
    const ScenarioStarter = () => {
      const demo = useMockDemo();
      return <button type="button" onClick={() => demo.runScenarioStep('GS-01')}>Chạy GS01 dùng chung</button>;
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

    fireEvent.click(view.getByText('Chạy GS01 dùng chung'));
    expect(view.getByTestId('shared-scenario-banner')).toBeTruthy();
    expect(view.getByText(/GS-01 • Nhận hàng nhập → Ghi sổ → Cất hàng/)).toBeTruthy();
    expect(view.getByText(/Bước 1 • trạng thái này dùng chung/)).toBeTruthy();
  });

  it('renders canonical governance metadata and specialized review-required screen content', () => {
    const picking = renderPreview('outbound', 'OUT-05');
    expect(picking.getByText('Bàn làm việc lấy hàng / Luồng quét')).toBeTruthy();
    expect(picking.getByText('Ma trận màn hình • Đã đóng truy vết')).toBeTruthy();
    expect(picking.getByText('Quản trị & mức hoàn thiện chức năng')).toBeTruthy();
    expect(picking.getAllByText('Đợt 2').length).toBeGreaterThan(0);
    expect(picking.getByText(/Lấy hàng không được làm giảm Tồn thực tế toàn kho/)).toBeTruthy();
    expect(picking.getByText(/Đã đóng truy vết — Ma trận màn hình 229/)).toBeTruthy();
  });

  it('supports interactive core WMS state transitions and exception recovery', () => {
    const view = renderPreview('outbound', 'OUT-08');
    expect(view.getByTestId('interactive-capability-demo')).toBeTruthy();
    fireEvent.click(view.getByText('Thực hiện bước tiếp theo'));
    expect(view.getByText('Trạng thái → VALIDATING')).toBeTruthy();

    fireEvent.click(view.getByText('Mô phỏng ngoại lệ'));
    expect(view.getByText('DISPATCH_INVENTORY_CONFLICT')).toBeTruthy();
    fireEvent.click(view.getByText('Giải quyết ngoại lệ'));
    expect(view.getByText(/Đã xử lý ngoại lệ/)).toBeTruthy();
  });

  it('shows domain-specific warehouse lifecycle instead of a spec-only card', () => {
    const view = renderPreview('warehouse-structure', 'WH-06');
    expect(view.getByRole('heading', { name: 'Bảng điều hành cửa kho & sân bãi' })).toBeTruthy();
    expect(view.getAllByText('CHECKED_IN').length).toBeGreaterThan(0);
    expect(view.getByText('ASSIGN_DOCK')).toBeTruthy();
    fireEvent.click(view.getByText('Mô phỏng ngoại lệ'));
    expect(view.getByText('DOCK_DOUBLE_ASSIGNMENT')).toBeTruthy();
  });

  it('keeps quantity conversion interactive for core execution demos', () => {
    const view = renderPreview('inventory-control', 'INV-08');
    const quantity = view.getByLabelText('Số lượng di chuyển') as HTMLInputElement;
    fireEvent.change(quantity, { target: { value: '7' } });
    expect(view.getByText('84 Cái')).toBeTruthy();
  });

  it('renders a dedicated enterprise SSO simulator for the remaining planned capability tranche', () => {
    const view = renderPreview('administration', 'AD-08');
    expect(view.getAllByText('SSO / Liên kết danh tính').length).toBeGreaterThan(0);
    expect(view.getByText('TEST_SSO')).toBeTruthy();
    fireEvent.click(view.getByText('Mô phỏng ngoại lệ'));
    expect(view.getByText('SSO_MAPPING_AMBIGUOUS')).toBeTruthy();
  });

  it('shows safe not-found UI for invalid capability', () => {
    const view = renderPreview('outbound', 'OUT-DOES-NOT-EXIST');
    expect(view.getByText('Không tìm thấy chức năng')).toBeTruthy();
  });
});
