// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import BusinessPartners from './BusinessPartners';
import apiClient from '../services/apiClient';
vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));
const get = vi.mocked(apiClient.get); const post = vi.mocked(apiClient.post); const put = vi.mocked(apiClient.put);
const partner = {id:1,code:'BP01',name:'Đối tác',isSupplier:true,isCustomer:true,isActive:true,rowVersion:'AQ=='};
describe('BusinessPartners (mocked API)',()=>{
  afterEach(cleanup);
  beforeEach(()=>{vi.resetAllMocks();localStorage.clear();localStorage.setItem('role','Admin');localStorage.setItem('permissions','["partner.read","partner.create","partner.update","partner.deactivate"]');get.mockResolvedValue({data:{items:[partner],totalPages:1}} as never);post.mockReset();put.mockReset()});
  it('renders both roles and filters through the paged API',async()=>{const v=render(<BusinessPartners/>);expect(await v.findByRole('cell', { name: /Nhà cung cấp.*Khách hàng/ })).toBeTruthy();fireEvent.change(v.getByLabelText('Lọc vai trò'),{target:{value:'supplier'}});await waitFor(()=>expect(get).toHaveBeenLastCalledWith('/api/v1/business-partners',expect.objectContaining({params:expect.objectContaining({role:'supplier'})}))) });
  it('prevents a double submit while a create is pending',async()=>{let done!:()=>void;post.mockReturnValue(new Promise(r=>{done=()=>r({data:{}} as never)}));const v=render(<BusinessPartners/>);await v.findByText('BP01');fireEvent.click(v.getByText('Thêm đối tác'));fireEvent.change(v.getByLabelText('Mã đối tác'),{target:{value:' new-1 '}});fireEvent.change(v.getByLabelText('Tên đối tác'),{target:{value:'Mới'}});const save=v.getByText('Lưu');fireEvent.click(save);fireEvent.click(save);expect(post).toHaveBeenCalledTimes(1);done();await waitFor(()=>expect(v.queryByText('Đang lưu...')).toBeNull())});
  it('keeps code disabled and sends rowVersion on edit',async()=>{put.mockResolvedValue({} as never);const v=render(<BusinessPartners/>);await v.findByText('BP01');fireEvent.click(v.getByText('Sửa'));expect((v.getByLabelText('Mã đối tác') as HTMLInputElement).disabled).toBe(true);fireEvent.click(v.getByText('Lưu'));await waitFor(()=>expect(put).toHaveBeenCalledWith('/api/v1/business-partners/1',expect.objectContaining({rowVersion:'AQ=='}))) });
  it('instructs reload after a stale update conflict',async()=>{put.mockRejectedValue({response:{status:409,data:{message:'Đối tác đã được thay đổi đồng thời.'}}});const v=render(<BusinessPartners/>);await v.findByText('BP01');fireEvent.click(v.getByText('Sửa'));fireEvent.click(v.getByText('Lưu'));expect(await v.findByText(/Vui lòng tải lại danh sách/)).toBeTruthy()});
  it('renders 3PL billing summary card and tariff code TRF-3PL-STO-01',async()=>{const v=render(<BusinessPartners/>);expect(await v.findByText('Hợp đồng Dịch vụ Logistics 3PL & Bảng kê Tính phí Lưu kho / Tác nghiệp')).toBeTruthy();expect(v.getByText('Công nợ hiện tại')).toBeTruthy();expect(v.getByText('Hạn mức & Kỳ hạn')).toBeTruthy();expect(v.getByText(/Tổng số trên trang:/)).toBeTruthy();expect(v.getByText('TRF-3PL-STO-01')).toBeTruthy();expect(v.getByText('Phát hành Bảng kê & Ghi nhận Công nợ 3PL')).toBeTruthy();expect(v.getByRole('button',{name:'Mở Cổng chủ hàng 3PL'})).toBeTruthy();});
  it('enforces 3PL statement safety lock, secondary recalculate with confirm, and credit limit exceeded badge (FIND-W650-3PL-SAFE-001)',async()=>{
    get.mockImplementation((async (url: string) => {
      if (url === '/api/v1/business-partners/3pl-billing-summary') {
        return { data: { statementCode: 'STM-3PL-202610', totalBillingAmount: 25000000, storageFeeAmount: 15000000, inboundHandlingFeeAmount: 5000000, outboundHandlingFeeAmount: 5000000 } } as never;
      }
      return { data: { items: [{ ...partner, creditLimit: 1000000, receivableBalance: 2500000 }], totalPages: 1 } } as never;
    }) as never);
    post.mockResolvedValue({ data: { statementCode: 'STM-3PL-202610', billingPeriod: '2026-10', totalBillingAmount: 25000000 } } as never);
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const v = render(<BusinessPartners />);
    expect(await v.findByText('Vượt hạn mức tín dụng')).toBeTruthy();
    const lockedBtn = await v.findByTestId('btn-3pl-statement-locked') as HTMLButtonElement;
    expect(lockedBtn.disabled).toBe(true);
    expect(lockedBtn.textContent).toContain('✓ Đã phát hành bảng kê kỳ 2026-10');
    const recalcBtn = v.getByTestId('btn-recalculate-3pl-statement');
    expect(recalcBtn.textContent).toContain('Tính lại / Điều chỉnh bảng kê');
    fireEvent.click(recalcBtn);
    expect(confirmSpy).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/v1/business-partners/3pl-billing/generate-statement', expect.objectContaining({ billingPeriod: '2026-10' })));
    confirmSpy.mockRestore();
  });
  it('enforces partner-delete-confirm-modal balance guard (W-B3-01), Vai trò minWidth 145px (W-B3-02), and 3PL numeric alignment (W-B3-03)',async()=>{
    const del = vi.mocked(apiClient.delete);
    del.mockResolvedValue({ data: {} } as never);
    get.mockImplementation((async (url: string) => {
      if (url === '/api/v1/business-partners/3pl-billing-summary') return { data: {} } as never;
      return { data: { items: [{ ...partner, receivableBalance: 500000 }], totalPages: 1 } } as never;
    }) as never);
    const v = render(<BusinessPartners />);
    await v.findByText('BP01');
    expect((v.getByRole('columnheader', { name: 'Vai trò' }) as HTMLElement).style.minWidth).toBe('145px');
    expect((v.getByRole('columnheader', { name: 'Sản lượng' }) as HTMLElement).className).toContain('ui-cell-numeric');
    expect((v.getByRole('columnheader', { name: 'Đơn giá (₫)' }) as HTMLElement).className).toContain('ui-cell-numeric');
    expect((v.getByRole('columnheader', { name: 'Thành tiền (₫)' }) as HTMLElement).className).toContain('ui-cell-numeric');
    fireEvent.click(v.getByRole('button', { name: 'Xóa' }));
    expect(v.getByTestId('partner-delete-confirm-modal')).toBeTruthy();
    expect(v.getByTestId('partner-delete-balance-warning')).toBeTruthy();
    expect((v.getByTestId('btn-confirm-delete-partner') as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(v.getByTestId('btn-cancel-delete-partner'));
    expect(v.queryByTestId('partner-delete-confirm-modal')).toBeNull();
    cleanup();
    get.mockImplementation((async (url: string) => {
      if (url === '/api/v1/business-partners/3pl-billing-summary') return { data: {} } as never;
      return { data: { items: [{ ...partner, receivableBalance: 0, payableBalance: 0 }], totalPages: 1 } } as never;
    }) as never);
    const v2 = render(<BusinessPartners />);
    await v2.findByText('BP01');
    fireEvent.click(v2.getByRole('button', { name: 'Xóa' }));
    const confirmDeleteBtn = v2.getByTestId('btn-confirm-delete-partner') as HTMLButtonElement;
    expect(confirmDeleteBtn.disabled).toBe(false);
    fireEvent.click(confirmDeleteBtn);
    await waitFor(() => expect(del).toHaveBeenCalledWith('/api/v1/business-partners/1'));
  });
});
