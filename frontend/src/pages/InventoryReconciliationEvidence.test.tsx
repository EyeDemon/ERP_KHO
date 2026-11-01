// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryReconciliation from './InventoryReconciliation';
import apiClient from '../services/apiClient';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn() },
}));

vi.mock('../services/runtimeMode', () => ({
  isReadOnlyDemoRuntime: vi.fn(() => false),
}));

describe('InventoryReconciliation', () => {
  beforeEach(() => {
    vi.mocked(isReadOnlyDemoRuntime).mockReturnValue(false);
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
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
      if (url === '/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({ data: [{id: 1, code: 'HCM', name: 'Kho HCM'}] });
      if (url.startsWith('/api/v1/inventory-reconciliation/investigation?')) {
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
      '/api/v1/inventory-reconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&bucketAnchorId=102&bucketAfterId=100'
    );

    fireEvent.click(view.getByRole('button', {name: 'Bucket trước'}));
    await view.findByText(/Trang bucket 1/);
    expect(view.getByRole('table', {name: 'Bucket tồn phục vụ điều tra chênh lệch'})
      .querySelectorAll('tbody tr')).toHaveLength(100);
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/v1/inventory-reconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&bucketAnchorId=102'
    );
  });

  it('discards bucket evidence if warehouse access is revoked on the second page', async () => {
    const all = Array.from({length: 100}, (_, i) => ({
      inventoryStockId: i + 1, quantity: 1, reservedQuantity: 0,
    }));
    vi.mocked(apiClient.get).mockImplementation((url: string) => {
      if (url === '/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data: [{id: 1, code: 'HCM', name: 'Kho HCM'}]});
      if (url.includes('bucketAfterId=100')) return Promise.reject({response: {status: 404}});
      if (url.startsWith('/api/v1/inventory-reconciliation/investigation?'))
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
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?')){
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
      '/api/v1/inventory-reconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&bucketStatus=QcHold'
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
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?'))return Promise.resolve({data:{
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
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?')){
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
      '/api/v1/inventory-reconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&bucketAnchorId=20&eventStatus=QcHold'
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
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?'))return Promise.resolve({data:{
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
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?')){
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
      '/api/v1/inventory-reconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72&eventBeforeId=70'
    );
    fireEvent.click(view.getByRole('button',{name:'Sự kiện mới hơn'}));
    await view.findByText(/Trang Ledger 1/);
    expect(view.getByRole('table',{name:'Sự kiện Ledger phục vụ điều tra chênh lệch'}).textContent).toContain('72');
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/v1/inventory-reconciliation/investigation?warehouseId=1&productId=10&limit=50&eventAnchorId=72'
    );
    fireEvent.click(view.getByRole('button',{name:'Làm mới mốc Ledger'}));
    await waitFor(()=>expect(view.getByText(/Trang Ledger 1/)).toBeTruthy());
    expect(vi.mocked(apiClient.get).mock.calls.filter(([url])=>
      url==='/api/v1/inventory-reconciliation/investigation?warehouseId=1&productId=10&limit=50'
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
      if(url==='/api/v1/inventory-reconciliation/warehouses')return Promise.resolve({data:[]});
      if(url.includes('eventBeforeId=70'))return Promise.reject({response:{status:404}});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?'))return Promise.resolve({data:evidence});
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
      if(url==='/api/v1/inventory-reconciliation/warehouses')return Promise.resolve({data:[]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?'))return Promise.resolve({data:{
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


});
