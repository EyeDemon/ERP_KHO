// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryReconciliation from './InventoryReconciliation';
import apiClient from '../services/apiClient';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn() },
}));

vi.mock('../services/runtimeMode', () => ({
  isBlueprintDemoRuntime: vi.fn(() => false),
}));

describe('InventoryReconciliation', () => {
  beforeEach(() => {
    vi.mocked(isBlueprintDemoRuntime).mockReturnValue(false);
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('uses read-only demo data on the Vercel blueprint runtime without calling APIs', async () => {
    vi.mocked(isBlueprintDemoRuntime).mockReturnValue(true);
    const view = render(<InventoryReconciliation />);

    expect(await view.findByText('SKU-1001')).toBeTruthy();
    expect(view.getByText('Đối chiếu tồn kho & ledger')).toBeTruthy();
    expect(view.getByText(/chỉ đọc/)).toBeTruthy();
    expect(view.getByText(/Phạm vi hiện tại chỉ đối chiếu trạng thái AVAILABLE/)).toBeTruthy();
    expect(view.getAllByText('Lệch').length).toBeGreaterThan(0);
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('renders reconciliation rows and highlights mismatches', async () => {
    vi.mocked(apiClient.get).mockImplementation((url: string) => {
      if (url === '/api/InventoryReconciliation/warehouses') {
        return Promise.resolve({ data: [{ id: 1, code: 'HCM', name: 'Kho HCM' }] });
      }
      return Promise.resolve({
        data: {
          items: [
            {
              productId: 10,
              productCode: 'SKU-010',
              productName: 'Sản phẩm test',
              warehouseId: 1,
              warehouseName: 'Kho HCM',
              currentQuantity: 12,
              expectedQuantity: 10,
              difference: 2,
              importQuantity: 20,
              exportQuantity: 10,
              transferInQuantity: 0,
              transferOutQuantity: 0,
              adjustmentIncreaseQuantity: 0,
              adjustmentDecreaseQuantity: 0,
              status: 'Mismatch',
            },
          ],
          totalRecords: 1,
          pageIndex: 1,
          pageSize: 20,
          totalPages: 1,
        },
      });
    });

    const view = render(<InventoryReconciliation />);

    expect(await view.findByText('SKU-010')).toBeTruthy();
    expect(view.getByText('Lệch')).toBeTruthy();
    expect(view.getByText('Mismatch trang hiện tại').previousSibling?.textContent).toBe('1');
    expect(view.getByText('Độ lệch tuyệt đối').previousSibling?.textContent).toBe('2');
  });


  it('loads only permission-scoped warehouses and never calls the generic warehouse directory',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')return Promise.resolve({data:[
        {id:7,code:'W-7',name:'Kho được phân quyền'}
      ]});
      return Promise.resolve({data:{items:[],totalRecords:0,pageIndex:1,pageSize:20,totalPages:0}});
    });
    const view=render(<InventoryReconciliation />);
    expect(await view.findByRole('option',{name:'Kho được phân quyền'})).toBeTruthy();
    expect(view.queryByRole('option',{name:'Kho không được phân quyền'})).toBeNull();
    expect(vi.mocked(apiClient.get).mock.calls.some(([url])=>url==='/api/warehouses')).toBe(false);
    fireEvent.change(view.getByLabelText('Kho'),{target:{value:'7'}});
    fireEvent.click(view.getByRole('button',{name:'Đối chiếu'}));
    await waitFor(()=>expect(vi.mocked(apiClient.get).mock.calls.some(([url])=>
      String(url).startsWith('/api/InventoryReconciliation?') &&
      String(url).includes('warehouseId=7'))).toBe(true));
  });

  it('fails closed when authorized warehouse selector returns malformed or duplicate data',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')return Promise.resolve({data:[
        {id:7,code:'W-7',name:'Kho 7'},
        {id:7,code:'W-7',name:'Kho trùng ID'}
      ]});
      return Promise.resolve({data:{items:[],totalRecords:0,pageIndex:1,pageSize:20,totalPages:0}});
    });
    const view=render(<InventoryReconciliation />);
    expect(await view.findByText(/Không thể tải danh sách kho được cấp quyền/)).toBeTruthy();
    expect((view.getByLabelText('Kho') as HTMLSelectElement).disabled).toBe(true);
    expect(view.queryByRole('option',{name:'Kho 7'})).toBeNull();
  });

  it('rejects malformed, fractional and unsafe product IDs before requesting a filtered report',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')return Promise.resolve({data:[]});
      return Promise.resolve({data:{items:[],totalRecords:0,pageIndex:1,pageSize:20,totalPages:0}});
    });
    const view=render(<InventoryReconciliation />);
    await waitFor(()=>expect(vi.mocked(apiClient.get).mock.calls.some(([url])=>
      String(url).startsWith('/api/InventoryReconciliation?'))).toBe(true));
    const product=view.getByLabelText('ID sản phẩm');
    const initial=vi.mocked(apiClient.get).mock.calls.length;
    for(const invalid of ['1.5','0','-1','9007199254740993','abc']){
      fireEvent.change(product,{target:{value:invalid}});
      fireEvent.click(view.getByRole('button',{name:'Đối chiếu'}));
      expect(view.getByText('ID sản phẩm phải là số nguyên dương hợp lệ và an toàn.')).toBeTruthy();
      expect(document.activeElement).toBe(product);
      expect(vi.mocked(apiClient.get).mock.calls.length).toBe(initial);
    }
    fireEvent.change(product,{target:{value:'17'}});
    fireEvent.click(view.getByRole('button',{name:'Đối chiếu'}));
    await waitFor(()=>expect(vi.mocked(apiClient.get).mock.calls.some(([url])=>
      String(url).includes('productId=17'))).toBe(true));
    expect(view.queryByText('ID sản phẩm phải là số nguyên dương hợp lệ và an toàn.')).toBeNull();
  });

  it('shows permission-specific 403 and 404 fail-closed errors',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')return Promise.resolve({data:[]});
      return Promise.reject({response:{status:403}});
    });
    const view=render(<InventoryReconciliation />);
    expect(await view.findByText('Bạn không có quyền xem sổ cái đối chiếu tồn kho.')).toBeTruthy();
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')return Promise.resolve({data:[]});
      return Promise.reject({response:{status:404}});
    });
    fireEvent.change(view.getByLabelText('Mã / tên sản phẩm'),{target:{value:'abc'}});
    fireEvent.click(view.getByRole('button',{name:'Đối chiếu'}));
    expect(await view.findByText('Không tìm thấy kho đối chiếu trong phạm vi được cấp quyền.')).toBeTruthy();
  });

  it('applies warehouse and keyword filters to the reconciliation request', async () => {
    vi.mocked(apiClient.get).mockImplementation((url: string) => {
      if (url === '/api/InventoryReconciliation/warehouses') {
        return Promise.resolve({ data: [{ id: 1, code: 'HCM', name: 'Kho HCM' }] });
      }
      return Promise.resolve({ data: { items: [], totalRecords: 0, pageIndex: 1, pageSize: 20, totalPages: 0 } });
    });

    const view = render(<InventoryReconciliation />);
    await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.some(([url]) => String(url).startsWith('/api/InventoryReconciliation?'))).toBe(true));

    fireEvent.change(view.getByLabelText('Kho'), { target: { value: '1' } });
    fireEvent.change(view.getByLabelText('Mã / tên sản phẩm'), { target: { value: 'milk' } });
    fireEvent.click(view.getByRole('button', { name: 'Đối chiếu' }));

    await waitFor(() => {
      const urls = vi.mocked(apiClient.get).mock.calls.map(([url]) => String(url));
      expect(urls.some(url => url.includes('warehouseId=1') && url.includes('keyword=milk') && url.includes('page=1'))).toBe(true);
    });
  });
});
