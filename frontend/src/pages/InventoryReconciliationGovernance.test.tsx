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


  it('shows detailed status mismatch when offsetting QC and Quarantine differences sum to zero', async () => {
    const details = ['Available', 'QcHold', 'Quarantine', 'Damaged', 'Rejected', 'Blocked',
      'Expired', 'RecallBlocked'].map(status => {
      const currentQuantity = status === 'Available' ? 10 : status === 'QcHold' ? 3 :
        status === 'Quarantine' ? 1 : 0;
      const expectedQuantity = status === 'Available' ? 10 :
        status === 'QcHold' || status === 'Quarantine' ? 2 : 0;
      return { status, currentQuantity, expectedQuantity,
        difference: currentQuantity - expectedQuantity, reservedQuantity: 0,
        bucketCount: 0, directLedgerNetQuantity: expectedQuantity,
        statusChangeInQuantity: 0, statusChangeOutQuantity: 0 };
    });
    vi.mocked(apiClient.get).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/warehouses')
        ? [{id: 1, code: 'W1', name: 'Kho thử'}]
        : url.includes('/investigation?')
          ? {warehouseId: 1, productId: 10, warehouseName: 'Kho thử',
            productCode: 'SKU-OFFSET', productName: 'Kiểm thử bù trừ',
            eventAnchorId: 10, eventCount: 0, bucketCount: 0, events: [], buckets: [],
            eventsTruncated: false, bucketsTruncated: false, isReadOnly: true,
            currentQuantity: 10, expectedQuantity: 10, difference: 0,
            allStatusCurrentQuantity: 14, allStatusExpectedQuantity: 14,
            allStatusDifference: 0, allStatusStatus: 'Mismatch', statusBreakdown: details}
          : {items: [{warehouseId: 1, productId: 10, warehouseName: 'Kho thử',
            productCode: 'SKU-OFFSET', productName: 'Kiểm thử bù trừ',
            currentQuantity: 10, expectedQuantity: 10, difference: 0, status: 'Match',
            allStatusCurrentQuantity: 14, allStatusExpectedQuantity: 14,
            allStatusDifference: 0, allStatusStatus: 'Mismatch',
            importQuantity: 10, exportQuantity: 0, transferInQuantity: 0,
            transferOutQuantity: 0, adjustmentIncreaseQuantity: 0,
            adjustmentDecreaseQuantity: 0
          }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1} }));
    const view = render(<InventoryReconciliation />);
    await view.findByText('SKU-OFFSET');
    fireEvent.click(view.getByRole('button', {name: 'Xem bằng chứng SKU-OFFSET tại Kho thử'}));
    expect(await view.findByText(/Lệch dù tổng bằng 0/)).toBeTruthy();
    const badges = view.getAllByText('Lệch theo trạng thái');
    expect(badges).toHaveLength(2);
    const table = view.getByRole('table', {name:'Đối chiếu từng trạng thái tồn kho theo Ledger'});
    expect(table.textContent).toContain('Chờ kiểm định');
    expect(table.textContent).toContain('Cách ly');
  });

  it('rejects a false Match verdict in the detailed eight-status evidence', async () => {
    const details = ['Available', 'QcHold', 'Quarantine', 'Damaged', 'Rejected', 'Blocked',
      'Expired', 'RecallBlocked'].map(status => ({
      status, currentQuantity: status === 'QcHold' ? 1 : 0, expectedQuantity: 0,
      difference: status === 'QcHold' ? 1 : 0, reservedQuantity: 0, bucketCount: 0,
      directLedgerNetQuantity: 0, statusChangeInQuantity: 0, statusChangeOutQuantity: 0
    }));
    vi.mocked(apiClient.get).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/warehouses')
        ? [{ id: 1, code: 'W1', name: 'Kho thử' }]
        : url.includes('/investigation?')
          ? {warehouseId: 1, productId: 10, warehouseName: 'Kho thử',
            productCode: 'SKU-FAKE', productName: 'Kiểm thử',
            eventAnchorId: 10, eventCount: 0, bucketCount: 0,
            events: [], buckets: [], isReadOnly: true,
            currentQuantity: 0, expectedQuantity: 0, difference: 0,
            allStatusStatus: 'Match', allStatusCurrentQuantity: 1,
            allStatusExpectedQuantity: 0, allStatusDifference: 1,
            statusBreakdown: details}
          : {items:[{warehouseId: 1, productId: 10, warehouseName: 'Kho thử',
            productCode: 'SKU-FAKE', productName: 'Kiểm thử',
            currentQuantity: 0, expectedQuantity: 0, difference: 0, status: 'Match'}],
            totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1} }));
    const view = render(<InventoryReconciliation />);
    await view.findByText('SKU-FAKE');
    fireEvent.click(view.getByRole('button', {name:'Xem bằng chứng SKU-FAKE tại Kho thử'}));
    expect(await view.findByText('Không thể tải bằng chứng điều tra. Hãy thử lại.')).toBeTruthy();
    expect(view.queryByRole('table',{name:'Đối chiếu từng trạng thái tồn kho theo Ledger'})).toBeNull();
  });


  it('explains missing Ledger for a QC-only bucket instead of showing a synthetic correction amount', async () => {
    const breakdown = Object.keys({
      Available: true, QcHold: true, Quarantine: true, Damaged: true,
      Rejected: true, Blocked: true, Expired: true, RecallBlocked: true,
    }).map(status => ({
      status, currentQuantity: status === 'QcHold' ? 5 : 0,
      expectedQuantity: null, difference: null, reservedQuantity: 0,
      bucketCount: status === 'QcHold' ? 1 : 0,
      directLedgerNetQuantity: 0, statusChangeInQuantity: 0, statusChangeOutQuantity: 0,
    }));
    vi.mocked(apiClient.get).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/warehouses')
        ? [{ id: 1, code: 'W1', name: 'Kho thử' }]
        : url.includes('/investigation?')
          ? { warehouseId: 1, productId: 10, warehouseName: 'Kho thử',
            productCode: 'SKU-QC-NO-LEDGER', productName: 'Tồn QC cũ',
            eventAnchorId: 0, eventCount: 0, bucketCount: 0, events: [], buckets: [],
            eventsTruncated: false, bucketsTruncated: false, isReadOnly: true,
            currentQuantity: 0, expectedQuantity: 0, difference: 0,
            availableLedgerExpectedIsPartial: true,
            historyInsufficientForNonAvailableStock: true,
            allStatusCurrentQuantity: 5, allStatusReservedQuantity: 0,
            allStatusExpectedQuantity: null, allStatusDifference: null,
            allStatusStatus: 'Indeterminate', unclassifiedLedgerEventCount: 0,
            statusBreakdown: breakdown }
          : { items: [{ warehouseId: 1, productId: 10, warehouseName: 'Kho thử',
            productCode: 'SKU-QC-NO-LEDGER', productName: 'Tồn QC cũ',
            currentQuantity: 0, expectedQuantity: null, difference: null, status: 'Indeterminate',
            allStatusCurrentQuantity: 5, allStatusExpectedQuantity: null,
            allStatusDifference: null, allStatusStatus: 'Indeterminate',
            unclassifiedLedgerEventCount: 0, importQuantity: 0, exportQuantity: 0,
            transferInQuantity: 0, transferOutQuantity: 0,
            adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0,
          }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1 } }));
    const view = render(<InventoryReconciliation />);
    await view.findByText('SKU-QC-NO-LEDGER');
    fireEvent.click(view.getByRole('button', {
      name: 'Xem bằng chứng SKU-QC-NO-LEDGER tại Kho thử',
    }));
    expect(await view.findByText(/không tìm thấy lịch sử Ledger đến mốc/)).toBeTruthy();
    expect(view.getByText('Tổng Ledger các trạng thái (tham khảo)').previousSibling?.textContent)
      .toBe('Chưa xác định');
    const breakdownTable = view.getByRole('table', {
      name: 'Đối chiếu từng trạng thái tồn kho theo Ledger',
    });
    expect(breakdownTable.textContent).toContain('Chưa xác định');
  });

  it('refuses missing-Ledger reason paired with an invented completed reconciliation', async () => {
    const statuses = ['Available', 'QcHold', 'Quarantine', 'Damaged', 'Rejected',
      'Blocked', 'Expired', 'RecallBlocked'];
    vi.mocked(apiClient.get).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/warehouses')
        ? [{ id: 1, code: 'W1', name: 'Kho thử' }]
        : url.includes('/investigation?')
          ? { warehouseId: 1, productId: 10, eventAnchorId: 0,
            eventCount: 0, bucketCount: 0, events: [], buckets: [],
            eventsTruncated: false, bucketsTruncated: false, isReadOnly: true,
            historyInsufficientForNonAvailableStock: true,
            availableLedgerExpectedIsPartial: false, unclassifiedLedgerEventCount: 0,
            allStatusExpectedQuantity: 0, allStatusDifference: 0, allStatusStatus: 'Match',
            statusBreakdown: statuses.map(status => ({
              status, currentQuantity: 0, expectedQuantity: 0, difference: 0,
              reservedQuantity: 0, bucketCount: 0, directLedgerNetQuantity: 0,
              statusChangeInQuantity: 0, statusChangeOutQuantity: 0,
            })),
          }
          : { items: [{ warehouseId: 1, productId: 10, warehouseName: 'Kho thử',
            productCode: 'SKU-UNSAFE', productName: 'Không hợp lệ',
            currentQuantity: 0, expectedQuantity: 0, difference: 0, status: 'Match',
            importQuantity: 0, exportQuantity: 0, transferInQuantity: 0,
            transferOutQuantity: 0, adjustmentIncreaseQuantity: 0,
            adjustmentDecreaseQuantity: 0,
          }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1 } }));
    const view = render(<InventoryReconciliation />);
    await view.findByText('SKU-UNSAFE');
    fireEvent.click(view.getByRole('button', { name: 'Xem bằng chứng SKU-UNSAFE tại Kho thử' }));
    expect(await view.findByText('Không thể tải bằng chứng điều tra. Hãy thử lại.')).toBeTruthy();
    expect(view.queryByRole('table', {
      name: 'Đối chiếu từng trạng thái tồn kho theo Ledger',
    })).toBeNull();
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
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'HCM',name:'Kho HCM'}]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?'))
        return Promise.resolve({data:{
          warehouseId:1,productId:10,warehouseName:'Kho HCM',
          productCode:'SKU-010',productName:'Sản phẩm test',
          eventAnchorId:12,eventCount:1,bucketCount:1,
          currentQuantity:7,expectedQuantity:7,difference:0,
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
    expect(view.getByText('Khả dụng · Ledger đến mốc').previousSibling?.textContent).toBe('7');
    expect(view.getByText('Khả dụng · Chênh lệch tham khảo').previousSibling?.textContent).toBe('0');
  });


  it('rejects an incomplete Ledger investigation that incorrectly claims AVAILABLE is complete', async () => {
    vi.mocked(apiClient.get).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/warehouses')
        ? [{ id: 1, code: 'W1', name: 'Kho thử' }]
        : url.includes('/investigation?')
          ? { warehouseId: 1, productId: 10, eventAnchorId: 20,
            isReadOnly: true, eventCount: 1, bucketCount: 0,
            eventsTruncated: false, bucketsTruncated: false,
            events: [], buckets: [],
            currentQuantity: 12, expectedQuantity: 12, difference: 0,
            availableLedgerExpectedIsPartial: false,
            unclassifiedLedgerEventCount: 1,
            allStatusStatus: 'Indeterminate',
            allStatusCurrentQuantity: 12,
            allStatusExpectedQuantity: null, allStatusDifference: null,
            statusBreakdown: [
              'Available', 'QcHold', 'Quarantine', 'Damaged', 'Rejected',
              'Blocked', 'Expired', 'RecallBlocked',
            ].map(status => ({
              status, currentQuantity: status === 'Available' ? 12 : 0,
              reservedQuantity: 0, bucketCount: 0, directLedgerNetQuantity: 0,
              statusChangeInQuantity: 0, statusChangeOutQuantity: 0,
              expectedQuantity: null, difference: null,
            })),
          }
          : { items: [{
            warehouseId: 1, warehouseName: 'Kho thử',
            productId: 10, productCode: 'SKU-PARTIAL', productName: 'Hàng thử',
            currentQuantity: 12, expectedQuantity: null, difference: null,
            status: 'Indeterminate', unclassifiedLedgerEventCount: 1,
          }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1 } }));
    const view = render(<InventoryReconciliation />);
    await view.findByText('SKU-PARTIAL');
    fireEvent.click(view.getByRole('button', {
      name: 'Xem bằng chứng SKU-PARTIAL tại Kho thử',
    }));
    expect(await view.findByText('Không thể tải bằng chứng điều tra. Hãy thử lại.')).toBeTruthy();
    expect(view.queryByRole('table', {
      name: 'Đối chiếu từng trạng thái tồn kho theo Ledger',
    })).toBeNull();
  });

  it('marks historical unclassifiable ledger as unknown instead of a repair amount',async()=>{
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?'))
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

  it('does not call real investigation API on read-only demo',async()=>{
    vi.mocked(isReadOnlyDemoRuntime).mockReturnValue(true);
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'W1',name:'Kho HCM'}]});
      if(url==='/api/v1/inventory-reconciliation/outbox-messages')
        return Promise.resolve({data:[]});
      return Promise.resolve({data:{items:[{
        productId:10,productCode:'SKU-1001',productName:'Sản phẩm demo',
        warehouseId:1,warehouseName:'Kho HCM',currentQuantity:95,expectedQuantity:100,
        difference:-5,status:'Mismatch',importQuantity:100,exportQuantity:0,
        transferInQuantity:0,transferOutQuantity:0,
        adjustmentIncreaseQuantity:0,adjustmentDecreaseQuantity:0
      }],totalRecords:1,pageIndex:1,pageSize:20,totalPages:1}});
    });
    const view=render(<InventoryReconciliation />);
    await view.findByText('SKU-1001');
    const buttons=view.getAllByRole('button',{name:/Xem bằng chứng/});
    expect(buttons.length).toBeGreaterThan(0);
    expect(buttons.every(b=>(b as HTMLButtonElement).disabled)).toBe(true);
    fireEvent.click(buttons[0]);
    expect(vi.mocked(apiClient.get).mock.calls.some(([u])=>String(u).includes('/investigation'))).toBe(false);
  });

  it('clears stale evidence when filters change while investigation is in flight',async()=>{
    let resolveInvestigation: ((value:unknown)=>void)|undefined;
    vi.mocked(apiClient.get).mockImplementation((url:string)=>{
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'W1',name:'Kho HCM'}]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?'))
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
      if(url==='/api/v1/inventory-reconciliation/warehouses')
        return Promise.resolve({data:[{id:1,code:'W1',name:'Kho HCM'}]});
      if(url.startsWith('/api/v1/inventory-reconciliation/investigation?'))
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
      if (url === '/api/v1/inventory-reconciliation/warehouses') {
        return Promise.resolve({ data: [{ id: 1, code: 'HCM', name: 'Kho HCM' }] });
      }
      return Promise.resolve({ data: { items: [], totalRecords: 0, pageIndex: 1, pageSize: 20, totalPages: 0 } });
    });

    const view = render(<InventoryReconciliation />);
    await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.some(([url]) => String(url).startsWith('/api/v1/inventory-reconciliation?'))).toBe(true));

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
    expect(view.getByText('Lệch số dư trang hiện tại').previousSibling?.textContent).toBe('0');
    expect(view.getByText('Chưa xác định trang hiện tại').previousSibling?.textContent).toBe('1');
    expect(view.getByText('Độ lệch đã xác định').previousSibling?.textContent).toBe('0');
    expect(view.getByText('Đổi trạng thái +0 / -4')).toBeTruthy();
  });

  it('renders Integration Outbox monitoring card with all 4 standard event types', async () => {
    vi.mocked(apiClient.get).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/warehouses')
        ? [{ id: 1, code: 'W1', name: 'Kho thử' }]
        : { items: [], totalRecords: 0, pageIndex: 1, pageSize: 20, totalPages: 0 } }));
    const view = render(<InventoryReconciliation />);
    expect(await view.findByText('Giám sát Hàng đợi Bản tin Tích hợp Bất đồng bộ')).toBeTruthy();
    expect(view.getByText('Kết chuyển Bút toán Sổ cái Chung')).toBeTruthy();
    expect(view.getByText('Điều phối Vận đơn & Xuất bến')).toBeTruthy();
    expect(view.getByText('Hạch toán Hóa đơn Công nợ')).toBeTruthy();
    expect(view.getByText('Chốt Số dư Sổ cái Kho Cuối kỳ')).toBeTruthy();
  });

});
