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


  it('opens a read-only SQL evidence investigation without creating stock corrections',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')return Promise.resolve({data:[
        {id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?'))return Promise.resolve({data:{
        warehouseId:1,warehouseName:'Kho HCM',productId:10,
        productCode:'SKU-010',productName:'Sản phẩm test',
        eventAnchorId:72,eventCount:2,bucketCount:1,
        currentQuantity:12,expectedQuantity:10,difference:2,isReadOnly:true,
        eventsTruncated:true,bucketsTruncated:false,
        buckets:[{inventoryStockId:22,locationId:4,locationCode:'A-01',
          lotId:5,lotNumber:'LOT-05',quantity:12,reservedQuantity:2}],
        events:[{transactionId:72,transactionType:'Import',locationId:4,
          locationCode:'A-01',lotNumber:'LOT-05',quantity:10,
          signedQuantity:10,referenceType:'GoodsReceipt',referenceId:12,
          transactionDate:'2026-10-09T10:00:00Z'}]
      }});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:12,
        expectedQuantity:10,difference:2,status:'Mismatch',
        importQuantity:10,exportQuantity:0,transferInQuantity:0,
        transferOutQuantity:0,adjustmentIncreaseQuantity:0,adjustmentDecreaseQuantity:0
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    const trigger=view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'});
    fireEvent.click(trigger);
    const buckets=await view.findByRole('table',{name:'Bucket tồn phục vụ điều tra chênh lệch'});
    expect(buckets.textContent).toContain('LOT-05');
    expect(buckets.textContent).toContain('A-01');
    expect(buckets.textContent).toContain('22');
    const events=view.getByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'});
    expect(events.textContent).toContain('GoodsReceipt #12');
    expect(events.textContent).toContain('72');
    expect(view.getByText(/Bằng chứng đã giới hạn/)).toBeTruthy();
    expect(view.getByText(/Không sử dụng số chênh lệch này để tự sửa tồn kho/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/InventoryReconciliation/investigation?warehouseId=1&productId=10&limit=50'
    );
    fireEvent.click(view.getByRole('button',{name:'Đóng hồ sơ'}));
    expect(view.queryByRole('table',{name:'Bucket tồn phục vụ điều tra chênh lệch'})).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('does not call real investigation API on blueprint demo',async()=>{
    vi.mocked(isBlueprintDemoRuntime).mockReturnValue(true);
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-1001');
    const buttons=view.getAllByRole('button',{name:/Xem bằng chứng/});
    expect(buttons.length).toBeGreaterThan(0);
    expect(buttons.every(b=>(b as HTMLButtonElement).disabled)).toBe(true);
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('clears stale evidence when filters change while investigation is in flight',async()=>{
    let resolveInvestigation: ((value:unknown)=>void)|undefined;
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'W1',name:'Kho HCM'}]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?'))
        return new Promise(resolve=>{resolveInvestigation=resolve;});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:10,expectedQuantity:8,
        difference:2,status:'Mismatch',importQuantity:8,exportQuantity:0,
        transferInQuantity:0,transferOutQuantity:0,
        adjustmentIncreaseQuantity:0,adjustmentDecreaseQuantity:0
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    expect(await view.findByText('Đang tải bằng chứng theo kho và sản phẩm...')).toBeTruthy();
    fireEvent.change(view.getByLabelText('Mã / tên sản phẩm'),{target:{value:'other'}});
    fireEvent.click(view.getByRole('button',{name:'Đối chiếu'}));
    await waitFor(()=>expect(view.queryByText('Đang tải bằng chứng theo kho và sản phẩm...')).toBeNull());
    resolveInvestigation?.({data:{
      warehouseId:1,productId:10,isReadOnly:true,events:[],buckets:[],
      productCode:'SKU-010',productName:'Sản phẩm test',warehouseName:'Kho HCM'
    }});
    await waitFor(()=>expect(view.queryByRole('table',{name:'Bucket tồn phục vụ điều tra chênh lệch'})).toBeNull());
  });

  it('shows 403 investigation failure without reusing stale evidence',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'W1',name:'Kho HCM'}]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?'))
        return Promise.reject({response:{status:403}});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:10,expectedQuantity:8,
        difference:2,status:'Mismatch',importQuantity:8,exportQuantity:0,
        transferInQuantity:0,transferOutQuantity:0,adjustmentIncreaseQuantity:0,
        adjustmentDecreaseQuantity:0
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    expect(await view.findByText('Bạn không có quyền xem bằng chứng đối chiếu.')).toBeTruthy();
    expect(view.queryByRole('table',{name:'Bucket tồn phục vụ điều tra chênh lệch'})).toBeNull();
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
