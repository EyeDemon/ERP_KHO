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
    expect(view.getAllByRole('note').some(note =>
      note.textContent?.includes('không phải kết quả từ SQL Server thật')
      && note.textContent?.includes('INV-11')
    )).toBe(true);
    expect(apiClient.get).not.toHaveBeenCalled();
  });


  it('shows status mismatch even though AVAILABLE and grand total both match', async () => {
    vi.mocked(apiClient.get).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/warehouses')
        ? [{ id: 1, code: 'W1', name: 'Kho thử' }]
        : { items: [{
          warehouseId: 1, productId: 10, warehouseName: 'Kho thử',
          productCode: 'SKU-OFFSET', productName: 'Sản phẩm bù trừ',
          currentQuantity: 10, expectedQuantity: 10, difference: 0,
          status: 'Match', allStatusCurrentQuantity: 14,
          allStatusExpectedQuantity: 14, allStatusDifference: 0,
          allStatusStatus: 'Mismatch', importQuantity: 10, exportQuantity: 0,
          transferInQuantity: 0, transferOutQuantity: 0,
          adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0,
        }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1 } }));
    const view = render(<InventoryReconciliation />);
    expect(await view.findByText('SKU-OFFSET')).toBeTruthy();
    expect(view.getByText('Lệch theo trạng thái')).toBeTruthy();
    expect(view.getByText('Lệch 8 trạng thái trang hiện tại').previousSibling?.textContent).toBe('1');
    expect(view.getByText('Chênh lệch tổng: 0')).toBeTruthy();
  });

  it('rejects contradictory eight-status totals from API', async () => {
    vi.mocked(apiClient.get).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/warehouses')
        ? [{ id: 1, code: 'W1', name: 'Kho thử' }]
        : { items: [{
          warehouseId: 1, productId: 10, warehouseName: 'Kho thử',
          productCode: 'SKU-BAD', productName: 'Không hợp lệ',
          currentQuantity: 10, expectedQuantity: 10, difference: 0,
          status: 'Match', allStatusCurrentQuantity: 14,
          allStatusExpectedQuantity: 12, allStatusDifference: 0,
          allStatusStatus: 'Match', importQuantity: 10, exportQuantity: 0,
          transferInQuantity: 0, transferOutQuantity: 0,
          adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0,
        }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1 } }));
    const view = render(<InventoryReconciliation />);
    expect(await view.findByText(/Máy chủ trả dữ liệu đối chiếu không hợp lệ/)).toBeTruthy();
    expect(view.queryByText('SKU-BAD')).toBeNull();
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
    expect(view.getByText('Độ lệch đã xác định').previousSibling?.textContent).toBe('2');
  });


  it.each([
    ['phạm vi kho không khớp', { warehouseId: 9 }],
    ['tuyên bố Khớp sai', { status: 'Match' }],
    ['Ledger chưa phân loại nhưng được báo Khớp', {
      status: 'Match', currentQuantity: 12, expectedQuantity: 12, difference: 0,
      unclassifiedLedgerEventCount: 2,
    }],
    ['số lượng không hữu hạn', { currentQuantity: Infinity }],
  ])('không hiển thị bằng chứng đối chiếu không hợp lệ: %s', async (_scenario, patch) => {
    vi.mocked(apiClient.get).mockImplementation((url: string) => {
      if (url === '/api/InventoryReconciliation/warehouses')
        return Promise.resolve({ data: [{ id: 1, code: 'HCM', name: 'Kho HCM' }] });
      return Promise.resolve({ data: {
        items: [{
          productId: 10, productCode: 'SKU-UNTRUSTED', productName: 'Hàng thử',
          warehouseId: 1, warehouseName: 'Kho HCM',
          currentQuantity: 12, expectedQuantity: 10, difference: 2,
          status: 'Mismatch', importQuantity: 10, exportQuantity: 0,
          transferInQuantity: 0, transferOutQuantity: 0,
          adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0,
          ...patch,
        }],
        totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1,
      } });
    });
    const view = render(<InventoryReconciliation />);
    // Cross-warehouse evidence can be rejected against an explicitly
    // requested warehouse; an unfiltered report has no selected scope.
    if (_scenario === 'phạm vi kho không khớp') {
      await view.findByRole('option', { name: 'Kho HCM' });
      fireEvent.change(view.getByLabelText('Kho'), { target: { value: '1' } });
      fireEvent.click(view.getByRole('button', { name: 'Đối chiếu' }));
    }
    expect(await view.findByText(/Máy chủ trả dữ liệu đối chiếu không hợp lệ/)).toBeTruthy();
    expect(view.queryByText('SKU-UNTRUSTED')).toBeNull();
    expect(view.queryByRole('button', { name: /Xem bằng chứng SKU-UNTRUSTED/ })).toBeNull();
  });

  it('chặn kết quả phân trang giả hoặc lặp cặp kho-sản phẩm và chỉ khôi phục khi tải nguồn mới hợp lệ', async () => {
    let valid = false;
    const item = {
      productId: 10, productCode: 'SKU-VALID', productName: 'Hàng thử',
      warehouseId: 1, warehouseName: 'Kho HCM',
      currentQuantity: 12, expectedQuantity: 10, difference: 2,
      status: 'Mismatch', importQuantity: 10, exportQuantity: 0,
      transferInQuantity: 0, transferOutQuantity: 0,
      adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0,
    };
    vi.mocked(apiClient.get).mockImplementation((url: string) => {
      if (url === '/api/InventoryReconciliation/warehouses')
        return Promise.resolve({ data: [{ id: 1, code: 'HCM', name: 'Kho HCM' }] });
      return Promise.resolve({ data: valid
        ? { items: [item], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1 }
        : { items: [item, item], totalRecords: 2, pageIndex: 1, pageSize: 20, totalPages: 1 },
      });
    });
    const view = render(<InventoryReconciliation />);
    expect(await view.findByText(/Máy chủ trả dữ liệu đối chiếu không hợp lệ/)).toBeTruthy();
    expect(view.queryByText('SKU-VALID')).toBeNull();
    valid = true;
    fireEvent.change(view.getByLabelText('Mã / tên sản phẩm'), { target: { value: 'SKU' } });
    fireEvent.click(view.getByRole('button', { name: 'Đối chiếu' }));
    expect(await view.findByText('SKU-VALID')).toBeTruthy();
    expect(view.queryByText(/Máy chủ trả dữ liệu đối chiếu không hợp lệ/)).toBeNull();
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


  it('loads 102 bucket evidence rows in two keyset pages while keeping the Ledger anchor', async () => {
    const pageOneBuckets = Array.from({ length: 100 }, (_, i) => ({
      inventoryStockId: i + 1, quantity: 1, reservedQuantity: 0,
    }));
    const evidence = {
      warehouseId: 1, warehouseName: 'Kho HCM', productId: 10,
      productCode: 'SKU-010', productName: 'Sản phẩm test',
      eventAnchorId: 72, eventCount: 1, bucketCount: 102,
      bucketAnchorId: 102, bucketHasRowsAfterAnchor: false,
      currentQuantity: 102, expectedQuantity: 102, difference: 0,
      isReadOnly: true, eventsTruncated: false,
      events: [{transactionId: 72, transactionType: 'Import', quantity: 102, signedQuantity: 102}],
    };
    vi.mocked(apiClient.get).mockImplementation((url: string) => {
      if (url === '/api/InventoryReconciliation/warehouses')
        return Promise.resolve({ data: [{id: 1, code: 'HCM', name: 'Kho HCM'}] });
      if (url.startsWith('/api/InventoryReconciliation/investigation?')) {
        const params = new URL(url, 'https://qa.invalid').searchParams;
        if (params.get('bucketAfterId') === '100') return Promise.resolve({data: {
          ...evidence, bucketAfterId: 100, nextBucketAfterId: null,
          bucketHasRowsAfterAnchor: true, bucketsTruncated: false,
          buckets: [{inventoryStockId: 101, quantity: 1, reservedQuantity: 0},
                    {inventoryStockId: 102, quantity: 1, reservedQuantity: 0}],
        }});
        return Promise.resolve({data: {
          ...evidence, bucketAfterId: null, nextBucketAfterId: 100,
          bucketsTruncated: true, buckets: pageOneBuckets,
        }});
      }
      return Promise.resolve({data: {items: [{
        productId: 10, productCode: 'SKU-010', productName: 'Sản phẩm test',
        warehouseId: 1, warehouseName: 'Kho HCM', currentQuantity: 102,
        expectedQuantity: 102, difference: 0, status: 'Match',
      }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1}});
    });
    const view = render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button', {name: 'Xem bằng chứng SKU-010 tại Kho HCM'}));
    await view.findByText(/Trang bucket 1/);
    expect((view.getByRole('button', {name: 'Bucket trước'}) as HTMLButtonElement).disabled).toBe(true);
    const firstTable = view.getByRole('table', {name: 'Bucket tồn phục vụ điều tra chênh lệch'});
    expect(firstTable.querySelectorAll('tbody tr')).toHaveLength(100);
    expect(firstTable.textContent).toContain('#100');

    fireEvent.click(view.getByRole('button', {name: 'Bucket sau'}));
    await view.findByText(/Trang bucket 2/);
    const secondTable = view.getByRole('table', {name: 'Bucket tồn phục vụ điều tra chênh lệch'});
    expect(secondTable.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(secondTable.textContent).toContain('#101');
    expect(secondTable.textContent).toContain('#102');
    expect(secondTable.textContent).not.toContain('#100');
    expect((view.getByRole('button', {name: 'Bucket sau'}) as HTMLButtonElement).disabled).toBe(true);
    expect(view.getByText(/Đã xuất hiện bucket mới sau mốc ID #102/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/InventoryReconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&bucketAnchorId=102&bucketAfterId=100'
    );

    fireEvent.click(view.getByRole('button', {name: 'Bucket trước'}));
    await view.findByText(/Trang bucket 1/);
    expect(view.getByRole('table', {name: 'Bucket tồn phục vụ điều tra chênh lệch'})
      .querySelectorAll('tbody tr')).toHaveLength(100);
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/InventoryReconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&bucketAnchorId=102'
    );
  });

  it('discards bucket evidence if warehouse access is revoked on the second page', async () => {
    const all = Array.from({length: 100}, (_, i) => ({
      inventoryStockId: i + 1, quantity: 1, reservedQuantity: 0,
    }));
    vi.mocked(apiClient.get).mockImplementation((url: string) => {
      if (url === '/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data: [{id: 1, code: 'HCM', name: 'Kho HCM'}]});
      if (url.includes('bucketAfterId=100')) return Promise.reject({response: {status: 404}});
      if (url.startsWith('/api/InventoryReconciliation/investigation?'))
        return Promise.resolve({data: {
          warehouseId: 1, warehouseName: 'Kho HCM', productId: 10,
          productCode: 'SKU-010', productName: 'Sản phẩm test',
          eventAnchorId: 72, eventCount: 0, bucketAnchorId: 101, bucketCount: 101,
          currentQuantity: 101, expectedQuantity: 0, difference: 101,
          isReadOnly: true, eventsTruncated: false, events: [],
          bucketsTruncated: true, nextBucketAfterId: 100, buckets: all,
        }});
      return Promise.resolve({data: {items: [{
        productId: 10, productCode: 'SKU-010', productName: 'Sản phẩm test',
        warehouseId: 1, warehouseName: 'Kho HCM', currentQuantity: 101,
        expectedQuantity: 0, difference: 101, status: 'Mismatch',
      }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1}});
    });
    const view = render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button', {name: 'Xem bằng chứng SKU-010 tại Kho HCM'}));
    await view.findByText(/Trang bucket 1/);
    fireEvent.click(view.getByRole('button', {name: 'Bucket sau'}));
    expect(await view.findByText('Không tìm thấy bằng chứng trong kho được cấp quyền.')).toBeTruthy();
    expect(view.queryByRole('table', {name: 'Bucket tồn phục vụ điều tra chênh lệch'})).toBeNull();
  });

  it('switches all-status bucket evidence while preserving the Ledger anchor and rejecting crossed scope', async () => {
    const base = {
      warehouseId:1,warehouseName:'Kho HCM',productId:10,
      productCode:'SKU-010',productName:'Sản phẩm test',
      eventAnchorId:72,eventCount:1,currentQuantity:7,
      expectedQuantity:10,difference:-3,isReadOnly:true,
      eventsTruncated:false,bucketsTruncated:false,
      events:[{transactionId:70,transactionType:'Import',signedQuantity:10}],
    };
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?')){
        const status = new URL(url, 'https://qa.invalid').searchParams.get('bucketStatus') ?? 'Available';
        if(status==='QcHold')return Promise.resolve({data:{
          ...base,bucketStatus:'QcHold',bucketCount:1,bucketAnchorId:23,
          buckets:[{inventoryStockId:23,locationCode:'QC-1',quantity:3,reservedQuantity:0}]
        }});
        if(status==='Quarantine')return Promise.resolve({data:{
          ...base,bucketStatus:'Quarantine',bucketCount:0,bucketAnchorId:0,buckets:[]
        }});
        return Promise.resolve({data:{
          ...base,bucketStatus:'Available',bucketCount:1,bucketAnchorId:22,
          buckets:[{inventoryStockId:22,locationCode:'A-1',quantity:7,reservedQuantity:2}]
        }});
      }
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:7,expectedQuantity:10,
        difference:-3,status:'Mismatch'
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    await view.findByRole('heading',{name:'Bucket tồn Khả dụng hiện tại (1)'});
    const selector=view.getByLabelText('Trạng thái bucket cần xem') as HTMLSelectElement;
    expect(selector.options.length).toBe(8);
    fireEvent.change(selector,{target:{value:'QcHold'}});
    await view.findByRole('heading',{name:'Bucket tồn Chờ kiểm định hiện tại (1)'});
    const qc=view.getByRole('table',{name:'Bucket tồn phục vụ điều tra chênh lệch'});
    expect(qc.textContent).toContain('QC-1');
    expect(qc.textContent).not.toContain('A-1');
    expect(view.getByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'}).textContent).toContain('#70');
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/InventoryReconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&bucketStatus=QcHold'
    );

    fireEvent.change(view.getByLabelText('Trạng thái bucket cần xem'),{target:{value:'Quarantine'}});
    await view.findByRole('heading',{name:'Bucket tồn Cách ly hiện tại (0)'});
    expect(view.getByRole('table',{name:'Bucket tồn phục vụ điều tra chênh lệch'}).textContent)
      .toContain('Không có bucket');
    fireEvent.change(view.getByLabelText('Trạng thái bucket cần xem'),{target:{value:'Available'}});
    await view.findByRole('heading',{name:'Bucket tồn Khả dụng hiện tại (1)'});
    expect(view.getByRole('table',{name:'Bucket tồn phục vụ điều tra chênh lệch'}).textContent)
      .toContain('A-1');
  });

  it('fails closed when bucket status returned by the API differs from requested status',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?'))return Promise.resolve({data:{
        warehouseId:1,productId:10,warehouseName:'Kho HCM',
        productCode:'SKU-010',productName:'Sản phẩm test',
        eventAnchorId:72,eventCount:0,bucketCount:1,bucketAnchorId:22,
        bucketStatus:'Available',isReadOnly:true,eventsTruncated:false,
        bucketsTruncated:false,events:[],buckets:[{inventoryStockId:22,quantity:7}]
      }});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:7,
        expectedQuantity:7,difference:0,status:'Match'
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    await view.findByRole('heading',{name:'Bucket tồn Khả dụng hiện tại (1)'});
    fireEvent.change(view.getByLabelText('Trạng thái bucket cần xem'),{target:{value:'QcHold'}});
    expect(await view.findByText('Không thể tải bằng chứng điều tra. Hãy thử lại.')).toBeTruthy();
    expect(view.queryByRole('table',{name:'Bucket tồn phục vụ điều tra chênh lệch'})).toBeNull();
  });

  it('paginates signed source and destination status-change Ledger independently from bucket status',async()=>{
    const base={
      warehouseId:1,warehouseName:'Kho HCM',productId:10,
      productCode:'SKU-010',productName:'Sản phẩm test',eventAnchorId:72,
      bucketStatus:'Available',bucketCount:1,bucketAnchorId:20,bucketsTruncated:false,
      buckets:[{inventoryStockId:20,quantity:7,reservedQuantity:0}],
      currentQuantity:7,expectedQuantity:10,difference:-3,
      isReadOnly:true,eventsTruncated:false,
    };
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?')){
        const p=new URL(url,'https://qa.invalid').searchParams;
        const status=p.get('eventStatus')??'Available';
        if(status==='QcHold')return Promise.resolve({data:{
          ...base,eventStatus:'QcHold',eventCount:1,
          events:[{transactionId:72,transactionType:'StatusChange',
            inventoryStatus:'QcHold',fromInventoryStatus:'Available',
            toInventoryStatus:'QcHold',signedQuantity:3,quantity:3}]
        }});
        if(status==='Quarantine')return Promise.resolve({data:{
          ...base,eventStatus:'Quarantine',eventCount:0,events:[]
        }});
        return Promise.resolve({data:{
          ...base,eventStatus:'Available',eventCount:2,
          events:[
            {transactionId:72,transactionType:'StatusChange',inventoryStatus:'QcHold',
              fromInventoryStatus:'Available',toInventoryStatus:'QcHold',
              signedQuantity:-3,quantity:3},
            {transactionId:70,transactionType:'Import',inventoryStatus:'Available',
              signedQuantity:10,quantity:10}]
        }});
      }
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:7,
        expectedQuantity:10,difference:-3,status:'Mismatch'
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    await view.findByRole('heading',{name:'Sự kiện Ledger Khả dụng (2)'});
    const original=view.getByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'});
    expect(original.textContent).toContain('-3');
    const select=view.getByLabelText('Trạng thái Ledger cần xem') as HTMLSelectElement;
    expect(select.options.length).toBe(8);
    fireEvent.change(select,{target:{value:'QcHold'}});
    await view.findByRole('heading',{name:'Sự kiện Ledger Chờ kiểm định (1)'});
    expect(view.getByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'}).textContent)
      .toContain('3');
    expect(view.getByRole('heading',{name:'Bucket tồn Khả dụng hiện tại (1)'})).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/InventoryReconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&bucketAnchorId=20&eventStatus=QcHold'
    );
    fireEvent.change(view.getByLabelText('Trạng thái Ledger cần xem'),{target:{value:'Quarantine'}});
    await view.findByRole('heading',{name:'Sự kiện Ledger Cách ly (0)'});
    fireEvent.change(view.getByLabelText('Trạng thái Ledger cần xem'),{target:{value:'Available'}});
    await view.findByRole('heading',{name:'Sự kiện Ledger Khả dụng (2)'});
    expect(view.getByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'}).textContent)
      .toContain('-3');
  });

  it('rejects a Ledger event response that belongs to another status',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?'))return Promise.resolve({data:{
        warehouseId:1,productId:10,warehouseName:'Kho HCM',
        productCode:'SKU-010',productName:'Sản phẩm test',isReadOnly:true,
        eventAnchorId:72,eventStatus:'Available',eventCount:0,
        bucketCount:1,bucketAnchorId:22,bucketsTruncated:false,
        eventsTruncated:false,events:[],buckets:[{inventoryStockId:22,quantity:7}]
      }});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:7,
        expectedQuantity:7,difference:0,status:'Match'
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    await view.findByRole('heading',{name:'Sự kiện Ledger Khả dụng (0)'});
    fireEvent.change(view.getByLabelText('Trạng thái Ledger cần xem'),{target:{value:'QcHold'}});
    expect(await view.findByText('Không thể tải bằng chứng điều tra. Hãy thử lại.')).toBeTruthy();
    expect(view.queryByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'})).toBeNull();
  });

  it('pages anchored Ledger events in both directions without offsets or duplicate IDs',async()=>{
    const evidence={
      warehouseId:1,warehouseName:'Kho HCM',productId:10,
      productCode:'SKU-010',productName:'Sản phẩm test',
      eventAnchorId:72,eventCount:3,bucketCount:1,
      currentQuantity:12,expectedQuantity:10,difference:2,isReadOnly:true,
      eventsTruncated:true,bucketsTruncated:false,
      buckets:[{inventoryStockId:22,quantity:12,reservedQuantity:0}],
      events:[
        {transactionId:72,transactionType:'Import',quantity:5,signedQuantity:5},
        {transactionId:70,transactionType:'Import',quantity:3,signedQuantity:3}
      ],
      nextEventBeforeId:70,ledgerHasEventsAfterAnchor:false
    };
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?')){
        if(url.includes('eventBeforeId=70'))return Promise.resolve({data:{
          ...evidence,eventBeforeId:70,nextEventBeforeId:null,
          ledgerHasEventsAfterAnchor:true,eventsTruncated:false,
          events:[{transactionId:60,transactionType:'Export',quantity:1,signedQuantity:-1}]
        }});
        return Promise.resolve({data:{
          ...evidence,ledgerHasEventsAfterAnchor:url.includes('eventAnchorId=72')
        }});
      }
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:12,expectedQuantity:10,
        difference:2,status:'Mismatch',importQuantity:10,exportQuantity:0,
        transferInQuantity:0,transferOutQuantity:0,
        adjustmentIncreaseQuantity:0,adjustmentDecreaseQuantity:0
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    const table=await view.findByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'});
    expect(table.textContent).toContain('72');
    expect(table.textContent).toContain('70');
    expect(view.getByText(/Trang Ledger 1/)).toBeTruthy();
    expect((view.getByRole('button',{name:'Sự kiện mới hơn'}) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(view.getByRole('button',{name:'Sự kiện cũ hơn'}));
    await view.findByText(/Trang Ledger 2/);
    const nextTable=view.getByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'});
    expect(nextTable.textContent).toContain('60');
    expect(nextTable.textContent).not.toContain('72');
    expect(nextTable.textContent).not.toContain('70');
    expect(view.getByText(/Đã có giao dịch mới hơn mốc #72/)).toBeTruthy();
    expect((view.getByRole('button',{name:'Sự kiện cũ hơn'}) as HTMLButtonElement).disabled).toBe(true);
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/InventoryReconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&eventBeforeId=70'
    );
    fireEvent.click(view.getByRole('button',{name:'Sự kiện mới hơn'}));
    await view.findByText(/Trang Ledger 1/);
    expect(view.getByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'}).textContent).toContain('72');
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/InventoryReconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72'
    );
    fireEvent.click(view.getByRole('button',{name:'Làm mới mốc Ledger'}));
    await waitFor(()=>expect(view.getByText(/Trang Ledger 1/)).toBeTruthy());
    expect(vi.mocked(apiClient.get).mock.calls.filter(([url])=>
      url==='/api/InventoryReconciliation/investigation?warehouseId=1&productId=10&limit=50'
    ).length).toBe(2);
  });

  it('fails closed when an event page no longer has warehouse permission',async()=>{
    const evidence={
      warehouseId:1,warehouseName:'Kho HCM',productId:10,
      productCode:'SKU-010',productName:'Sản phẩm test',eventAnchorId:70,
      eventCount:2,bucketCount:0,currentQuantity:0,expectedQuantity:2,
      difference:-2,isReadOnly:true,eventsTruncated:true,nextEventBeforeId:70,
      buckets:[],events:[{transactionId:70,transactionType:'Import',signedQuantity:2}]
    };
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')return Promise.resolve({data:[]});
      if(url.includes('eventBeforeId=70'))return Promise.reject({response:{status:404}});
      if(url.startsWith('/api/InventoryReconciliation/investigation?'))return Promise.resolve({data:evidence});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:0,expectedQuantity:2,
        difference:-2,status:'Mismatch'
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    await view.findByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'});
    fireEvent.click(view.getByRole('button',{name:'Sự kiện cũ hơn'}));
    expect(await view.findByText('Không tìm thấy bằng chứng trong kho được cấp quyền.')).toBeTruthy();
    expect(view.queryByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'})).toBeNull();
  });

  it('rejects malformed or cross-scope event cursors from server',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')return Promise.resolve({data:[]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?'))return Promise.resolve({data:{
        warehouseId:1,productId:10,isReadOnly:true,eventAnchorId:70,
        eventCount:1,bucketCount:0,eventsTruncated:true,
        buckets:[],events:[{transactionId:70}],nextEventBeforeId:999
      }});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:0,expectedQuantity:0,
        difference:0,status:'Match'
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    expect(await view.findByText('Không thể tải bằng chứng điều tra. Hãy thử lại.')).toBeTruthy();
    expect(view.queryByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'})).toBeNull();
  });


  it('shows all eight statuses and accounts for source and destination of QC status change',async()=>{
    const breakdown=[
      {status:'Available',currentQuantity:7,reservedQuantity:2,bucketCount:1,
        directLedgerNetQuantity:10,statusChangeInQuantity:0,
        statusChangeOutQuantity:3,expectedQuantity:7,difference:0},
      {status:'QcHold',currentQuantity:3,reservedQuantity:0,bucketCount:1,
        directLedgerNetQuantity:0,statusChangeInQuantity:3,
        statusChangeOutQuantity:0,expectedQuantity:3,difference:0},
      ...['Quarantine','Damaged','Rejected','Blocked','Expired','RecallBlocked'].map(status=>({
        status,currentQuantity:0,reservedQuantity:0,bucketCount:0,
        directLedgerNetQuantity:0,statusChangeInQuantity:0,
        statusChangeOutQuantity:0,expectedQuantity:0,difference:0
      }))
    ];
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?'))
        return Promise.resolve({data:{
          warehouseId:1,productId:10,warehouseName:'Kho HCM',
          productCode:'SKU-010',productName:'Sản phẩm test',
          eventAnchorId:12,eventCount:1,bucketCount:1,
          currentQuantity:7,expectedQuantity:10,difference:-3,
          allStatusCurrentQuantity:10,allStatusReservedQuantity:2,
          allStatusExpectedQuantity:10,allStatusDifference:0,
          unclassifiedLedgerEventCount:0,statusBreakdown:breakdown,isReadOnly:true,
          eventsTruncated:false,bucketsTruncated:false,
          events:[{transactionId:11,transactionType:'Import',signedQuantity:10}],
          buckets:[{inventoryStockId:22,quantity:7,reservedQuantity:2}]
        }});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:7,
        expectedQuantity:10,difference:-3,status:'Mismatch',
        importQuantity:10,exportQuantity:0,transferInQuantity:0,
        transferOutQuantity:0,adjustmentIncreaseQuantity:0,
        adjustmentDecreaseQuantity:0
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    const table=await view.findByRole('table',{name:'Đối chiếu từng trạng thái tồn kho theo Ledger'});
    expect(table.querySelectorAll('tbody tr').length).toBe(8);
    expect(table.textContent).toContain('Khả dụng');
    expect(table.textContent).toContain('Chờ kiểm định');
    expect(table.textContent).toContain('Khóa thu hồi');
    const rows=Array.from(table.querySelectorAll('tbody tr'));
    const available=rows.find(row=>row.textContent?.includes('Khả dụng'));
    const hold=rows.find(row=>row.textContent?.includes('Chờ kiểm định'));
    expect(available?.textContent).toContain('-3');
    expect(hold?.textContent).toContain('+3');
    expect(view.getByText(/Đổi trạng thái trừ ở nguồn/i)).toBeTruthy();
    expect(view.getByText(/số lệch chỉ phục vụ điều tra/)).toBeTruthy();
    expect(view.getByText('Tổng tồn mọi trạng thái')).toBeTruthy();
  });

  it('marks historical unclassifiable ledger as unknown instead of a repair amount',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/InventoryReconciliation/warehouses')
        return Promise.resolve({data:[]});
      if(url.startsWith('/api/InventoryReconciliation/investigation?'))
        return Promise.resolve({data:{
          warehouseId:1,productId:10,isReadOnly:true,eventAnchorId:80,
          eventCount:1,bucketCount:0,eventsTruncated:false,bucketsTruncated:false,
          events:[{transactionId:80,transactionType:'TransferAdjustment',signedQuantity:null}],
          buckets:[],allStatusCurrentQuantity:1,allStatusReservedQuantity:0,
          allStatusExpectedQuantity:null,allStatusDifference:null,
          unclassifiedLedgerEventCount:2,availableLedgerExpectedIsPartial:true,
          statusBreakdown:[{status:'QcHold',currentQuantity:1,
            reservedQuantity:0,bucketCount:1,directLedgerNetQuantity:0,
            statusChangeInQuantity:0,statusChangeOutQuantity:0,
            expectedQuantity:null,difference:null}]
        }});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-010',productName:'Sản phẩm test',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:0,
        expectedQuantity:1,difference:-1,status:'Mismatch'
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-010');
    fireEvent.click(view.getByRole('button',{name:'Xem bằng chứng SKU-010 tại Kho HCM'}));
    const warnings=await view.findAllByRole('alert');
    const warningText=warnings.map(x=>x.textContent).join(' ');
    expect(warningText).toContain('Có 2 giao dịch Ledger');
    expect(warningText).toContain('Không thể kết luận');
    expect(warningText).toContain('Tổng Ledger AVAILABLE');
    const table=view.getByRole('table',{name:'Đối chiếu từng trạng thái tồn kho theo Ledger'});
    expect(table.textContent).toContain('Chưa xác định');
    expect(view.getByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'}).textContent)
      .toContain('Chưa xác định');
    expect(view.getByText('Tổng Ledger các trạng thái (tham khảo)')).toBeTruthy();
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
  it('does not count unknown ledger history as a mismatch or a numeric zero', async () => {
    vi.mocked(apiClient.get).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/warehouses')
        ? [{ id: 1, code: 'W1', name: 'Kho thử' }]
        : { items: [{
            productId: 1, productCode: 'QA-UNKNOWN', productName: 'Hàng thử',
            warehouseId: 1, warehouseName: 'Kho thử', currentQuantity: 6,
            expectedQuantity: null, difference: null, status: 'Indeterminate',
            unclassifiedLedgerEventCount: 1, statusChangeOutQuantity: 4,
            importQuantity: 10, exportQuantity: 0, transferInQuantity: 0,
            transferOutQuantity: 0, adjustmentIncreaseQuantity: 0,
            adjustmentDecreaseQuantity: 0,
          }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1 } }));
    const view = render(<InventoryReconciliation />);
    expect(await view.findByText('QA-UNKNOWN')).toBeTruthy();
    expect(view.getAllByText('Chưa xác định').length).toBeGreaterThanOrEqual(3);
    expect(view.getByText('Mismatch trang hiện tại').previousSibling?.textContent).toBe('0');
    expect(view.getByText('Chưa xác định trang hiện tại').previousSibling?.textContent).toBe('1');
    expect(view.getByText('Độ lệch đã xác định').previousSibling?.textContent).toBe('0');
    expect(view.getByText('Đổi trạng thái +0 / -4')).toBeTruthy();
  });

});
