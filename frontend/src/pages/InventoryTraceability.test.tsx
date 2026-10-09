// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryTraceability from './InventoryTraceability';
import apiClient from '../services/apiClient';
import { getTraceabilityWarehouses } from '../services/traceabilityWarehouses';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn()}}));
vi.mock('../services/traceabilityWarehouses',()=>({getTraceabilityWarehouses:vi.fn()}));

const result={
  eventAnchorId:543,
  currentBuckets:[{
    inventoryStockId:51,productId:10,productCode:'SKU-10',productName:'Product 10',warehouseId:1,warehouseName:'DC HCM',
    locationId:7,locationCode:'A01-R01-B01',inventoryStatus:'Available',lotId:100,lotNumber:'LOT-A',
    serialId:null,serialNumber:null,onHandQuantity:10,reservedQuantity:0
  }],
  events:[
    {
      transactionId:41,productId:10,productCode:'SKU-10',productName:'Product 10',warehouseId:1,warehouseName:'DC HCM',
      transactionType:'Move',inventoryStatus:'Available',fromLocationCode:'A01-R01-B01',toLocationCode:'A01-R01-B02',
      lotNumber:'LOT-A',serialNumber:null,quantity:4,referenceType:'InventoryMove',referenceId:7,
      transactionDate:'2026-10-07T10:00:00Z',createdBy:5,createdByName:'Manager',note:'slotting',
      reversalOfTransactionId:null,correctiveTransactionId:42,reversalTransactionId:43,isReversed:true
    },
    {
      transactionId:43,productId:10,productCode:'SKU-10',productName:'Product 10',warehouseId:1,warehouseName:'DC HCM',
      transactionType:'Reversal',inventoryStatus:'Available',fromLocationCode:'A01-R01-B02',toLocationCode:'A01-R01-B01',
      lotNumber:'LOT-A',serialNumber:null,quantity:4,referenceType:'InventoryReversal',referenceId:41,
      transactionDate:'2026-10-07T11:00:00Z',createdBy:5,createdByName:'Manager',note:'corrective',
      reasonCode:'LOCATION_ERROR',
      reversalOfTransactionId:41,correctiveTransactionId:42,reversalTransactionId:43,isReversed:false
    }
  ]
};

const renderTrace=(entries=['/inventory-traceability'])=>render(
  <MemoryRouter initialEntries={entries}>
    <Routes><Route path="/inventory-traceability" element={<InventoryTraceability/>}/></Routes>
  </MemoryRouter>
);

describe('InventoryTraceability',()=>{
  beforeEach(()=>{
    vi.resetAllMocks();
    vi.mocked(getTraceabilityWarehouses).mockResolvedValue([
      {id:1,code:'HCM',name:'Kho TP.HCM'},
      {id:2,code:'HN',name:'Kho Hà Nội'}
    ]);
  });
  afterEach(()=>{
    cleanup();
    window.history.replaceState({}, '', window.location.pathname);
  });

  it('loads an authorized immutable reversal chain directly from its deep link',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=renderTrace(['/inventory-traceability?referenceType=InventoryReversal&referenceId=41']);

    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?referenceType=InventoryReversal&referenceId=41&limit=200'
    ));
    expect((view.getByLabelText('Loại tham chiếu') as HTMLInputElement).value).toBe('InventoryReversal');
    expect((view.getByLabelText('ID tham chiếu') as HTMLInputElement).value).toBe('41');
    expect(await view.findByText('Đảo giao dịch')).toBeTruthy();
  });

  it('tracks router deep-link changes and ignores a stale earlier ledger response',async()=>{
    let resolveFirst:(value:unknown)=>void=()=>{};
    const first=new Promise(resolve=>{resolveFirst=resolve});
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(String(url).includes('referenceId=41'))return await first as never;
      return {data:{...result,events:[{...result.events[0],transactionId:84,referenceId:84}]}} as never;
    });
    const view=render(
      <MemoryRouter initialEntries={['/inventory-traceability?referenceType=InventoryReversal&referenceId=41']}>
        <Link to="/inventory-traceability?referenceType=InventoryReversal&referenceId=84">Mở chuỗi đảo khác</Link>
        <Link to="/inventory-traceability">Về trang truy vết trống</Link>
        <Routes><Route path="/inventory-traceability" element={<InventoryTraceability/>}/></Routes>
      </MemoryRouter>
    );
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?referenceType=InventoryReversal&referenceId=41&limit=200'
    ));
    fireEvent.change(view.getByLabelText('Giới hạn sự kiện truy vết'),{
      target:{value:'500'}
    });
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Mở chuỗi đảo khác'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?referenceType=InventoryReversal&referenceId=84&limit=200'
    ));
    expect((view.getByLabelText('ID tham chiếu') as HTMLInputElement).value).toBe('84');
    expect((view.getByLabelText('ID sản phẩm') as HTMLInputElement).value).toBe('');
    expect((view.getByLabelText('Giới hạn sự kiện truy vết') as HTMLSelectElement).value).toBe('200');
    expect(await view.findByText('#84 • đã đảo')).toBeTruthy();
    // Flush the delayed first response before asserting: otherwise the
    // assertion can pass prematurely without exercising the race.
    await act(async()=>{resolveFirst({data:result});});
    expect(view.queryByText('#41 • đã đảo')).toBeNull();
    expect((view.getByLabelText('ID tham chiếu') as HTMLInputElement).value).toBe('84');
    fireEvent.click(view.getByText('Về trang truy vết trống'));
    await waitFor(()=>{
      expect((view.getByLabelText('ID tham chiếu') as HTMLInputElement).value).toBe('');
      expect(view.queryByText('#84 • đã đảo')).toBeNull();
    });
  });

  it('does not let a pending manual search overwrite a newer reversal deep link',async()=>{
    let resolveManual:(value:unknown)=>void=()=>{};
    const manual=new Promise(resolve=>{resolveManual=resolve});
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(String(url).includes('productId=10'))return await manual as never;
      return {data:{...result,events:[{...result.events[0],transactionId:84,referenceId:84}]}} as never;
    });
    const view=render(
      <MemoryRouter initialEntries={['/inventory-traceability']}>
        <Link to="/inventory-traceability?referenceType=InventoryReversal&referenceId=84">Mở chuỗi mới</Link>
        <Routes><Route path="/inventory-traceability" element={<InventoryTraceability/>}/></Routes>
      </MemoryRouter>
    );
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?productId=10&limit=200'
    ));
    fireEvent.click(view.getByText('Mở chuỗi mới'));
    expect(await view.findByText('#84 • đã đảo')).toBeTruthy();
    await act(async()=>{resolveManual({data:result});});
    expect(view.queryByText('#41 • đã đảo')).toBeNull();
    expect((view.getByLabelText('ID tham chiếu') as HTMLInputElement).value).toBe('84');
    expect(view.getByRole('status').textContent).toContain('Đã tải');
  });

  it('renders legacy uppercase or underscored inventory status in Vietnamese',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      currentBuckets:[{...result.currentBuckets[0],inventoryStatus:'RECALL_BLOCKED'}],
      events:[{...result.events[0],inventoryStatus:'QC_HOLD'}]
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(await view.findByText('Khóa thu hồi')).toBeTruthy();
    expect(view.getByText('Chờ kiểm tra chất lượng')).toBeTruthy();
  });

  it('loads only the warehouse choices returned by the scoped API and uses their real IDs',async()=>{
    const view=renderTrace();
    const select=view.getByLabelText('Kho truy vết') as HTMLSelectElement;
    expect(select.disabled).toBe(true);
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    await waitFor(()=>expect(select.disabled).toBe(false));
    expect(select.options).toHaveLength(3);
    expect([...select.options].map(option=>option.value)).toEqual(['','1','2']);
    expect(view.queryByRole('option',{name:/Kho không được phân quyền/})).toBeNull();
    expect(getTraceabilityWarehouses).toHaveBeenCalledTimes(1);
  });

  it('does not invent unrestricted warehouse choices when the lookup fails and can retry',async()=>{
    vi.mocked(getTraceabilityWarehouses)
      .mockRejectedValueOnce(new Error('403'))
      .mockResolvedValueOnce([{id:2,code:'HN',name:'Kho Hà Nội'}]);
    const view=renderTrace();
    const alert=await view.findByRole('alert');
    expect(alert.textContent).toContain('Không thể tải danh sách kho');
    const select=view.getByLabelText('Kho truy vết') as HTMLSelectElement;
    expect(select.disabled).toBe(true);
    expect(select.options).toHaveLength(1);
    fireEvent.click(view.getByRole('button',{name:'Tải lại danh sách kho'}));
    await view.findByRole('option',{name:'HN — Kho Hà Nội'});
    await waitFor(()=>expect(select.disabled).toBe(false));
    expect([...select.options].map(option=>option.value)).toEqual(['','2']);
    expect(getTraceabilityWarehouses).toHaveBeenCalledTimes(2);
  });

  it('handles zero assigned warehouses without inserting sample stock or identifiers',async()=>{
    vi.mocked(getTraceabilityWarehouses).mockResolvedValue([]);
    const view=renderTrace();
    expect(await view.findByText(/Chưa có kho được cấp quyền truy vết/)).toBeTruthy();
    const select=view.getByLabelText('Kho truy vết') as HTMLSelectElement;
    expect(select.disabled).toBe(true);
    expect(select.options).toHaveLength(1);
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('allows an authorized warehouse-only overview without requiring a product or document',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?warehouseId=1&limit=200'
    ));
    expect(await view.findByText('A01-R01-B01')).toBeTruthy();
    expect(view.getByText(/Chọn một kho để xem toàn bộ tồn hiện tại/)).toBeTruthy();
  });

  it('requires at least identity or reference and focuses the primary identity field',()=>{
    const view=renderTrace();
    const productId=view.getByLabelText('ID sản phẩm');
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getByRole('alert').textContent).toContain('ít nhất');
    expect(document.activeElement).toBe(productId);
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('requires reference type and id together and exposes inline field state',()=>{
    const view=renderTrace();
    const referenceType=view.getByLabelText('Loại tham chiếu');
    const referenceId=view.getByLabelText('ID tham chiếu');
    fireEvent.change(referenceType,{target:{value:'Shipment'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getByRole('alert').textContent).toContain('cùng nhau');
    expect(referenceType.getAttribute('aria-invalid')).toBe('true');
    expect(referenceId.getAttribute('aria-invalid')).toBe('true');
    expect(referenceType.getAttribute('aria-describedby')).toContain('traceability-reference-help');
    expect(referenceType.getAttribute('aria-describedby')).toContain('traceability-validation-error');
    expect(document.activeElement).toBe(referenceId);
    expect(apiClient.get).not.toHaveBeenCalled();

    fireEvent.change(referenceId,{target:{value:'99'}});
    expect(view.queryByRole('alert')).toBeNull();
    expect(referenceType.getAttribute('aria-invalid')).toBeNull();
    expect(referenceId.getAttribute('aria-invalid')).toBeNull();
  });

  it('keeps document identity when combining warehouse and product filters',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'StockTransfer'}});
    fireEvent.change(view.getByLabelText('ID tham chiếu'),{target:{value:'42'}});
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?warehouseId=1&productId=10&referenceType=StockTransfer&referenceId=42&limit=200'
    ));
    expect(view.getByText(/Kết hợp chứng từ với bộ lọc khác chỉ lấy nhóm tồn liên quan/)).toBeTruthy();
  });

  it('rejects malformed or unsafe product and reference IDs before requesting SQL data',async()=>{
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    const product=view.getByLabelText('ID sản phẩm') as HTMLInputElement;
    const reference=view.getByLabelText('ID tham chiếu') as HTMLInputElement;
    fireEvent.change(product,{target:{value:'1.5'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getByRole('alert').textContent).toContain('ID sản phẩm phải là số nguyên dương');
    expect(document.activeElement).toBe(product);
    expect(apiClient.get).not.toHaveBeenCalled();

    fireEvent.change(product,{target:{value:''}});
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'StockTransfer'}});
    fireEvent.change(reference,{target:{value:'9007199254740992'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getByRole('alert').textContent).toContain('ID tham chiếu phải là số nguyên dương');
    expect(document.activeElement).toBe(reference);
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('renders visible labels, current buckets and reversal-aware immutable timeline',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=renderTrace();
    expect(view.getByLabelText('Kho truy vết')).toBeTruthy();
    expect(view.getByLabelText('ID sản phẩm')).toBeTruthy();
    expect(view.getByLabelText('Mã lô')).toBeTruthy();
    expect(view.getByLabelText('Số sê-ri')).toBeTruthy();
    expect(view.getByLabelText('Loại tham chiếu')).toBeTruthy();
    expect(view.getByLabelText('ID tham chiếu')).toBeTruthy();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith('/api/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'));
    expect(await view.findByText('A01-R01-B01')).toBeTruthy();
    expect(view.getByText('Đảo giao dịch')).toBeTruthy();
    expect(view.getAllByText(/đã đảo/).length).toBeGreaterThan(0);
    expect(view.getByText(/đảo #41/)).toBeTruthy();
    expect(view.getAllByText(/Hiệu chỉnh #42/).length).toBeGreaterThan(0);
    expect(view.getAllByText(/Dấu đảo #43/).length).toBeGreaterThan(0);
    expect(view.getByText(/Gốc #41/)).toBeTruthy();
    expect(view.getByText(/Mã lý do: LOCATION_ERROR/)).toBeTruthy();
    expect(view.getByRole('status').textContent).toContain('Đã tải 1 nhóm tồn kho hiện tại và 2 sự kiện sổ cái');
  });

  it('warns when SQL limits event history and current inventory buckets',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result, eventsTruncated:true, bucketsTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(await view.findByText(/Chỉ hiển thị 500 nhóm tồn của trang hiện tại/)).toBeTruthy();
    expect(view.getByText(/Lịch sử còn dữ liệu cũ hơn/)).toBeTruthy();
  });

  it('paginates warehouse stock into a second server window and back without repeating buckets',async()=>{
    const second={...result,currentBuckets:[{
      ...result.currentBuckets[0],inventoryStockId:52,locationCode:'Z99'
    }],bucketsTruncated:false};
    vi.mocked(apiClient.get).mockImplementation(async url=>
      String(url).includes('bucketOffset=500')
        ? {data:second} as never
        : {data:{...result,bucketsTruncated:true}} as never
    );
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(await view.findByText('A01-R01-B01')).toBeTruthy();
    expect(view.getByText('Trang 1')).toBeTruthy();
    expect((view.getByRole('button',{name:'Trang trước'}) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(view.getByRole('button',{name:'Trang sau'}));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?warehouseId=1&limit=200&bucketOffset=500&eventAnchorId=543'
    ));
    expect(await view.findByText('Z99')).toBeTruthy();
    expect(view.queryByText('A01-R01-B01')).toBeNull();
    expect(view.getByText('Trang 2')).toBeTruthy();
    expect((view.getByRole('button',{name:'Trang sau'}) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(view.getByRole('button',{name:'Trang trước'}));
    expect(await view.findByText('A01-R01-B01')).toBeTruthy();
    expect(view.getByText('Trang 1')).toBeTruthy();
  });

  it('invalidates a pending next-page response when filter inputs change',async()=>{
    let resolvePage:(value:unknown)=>void=()=>{};
    const pending=new Promise(resolve=>{resolvePage=resolve});
    vi.mocked(apiClient.get).mockImplementation(async url=>
      String(url).includes('bucketOffset=500')
        ? await pending as never
        : {data:{...result,bucketsTruncated:true}} as never
    );
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(await view.findByText('A01-R01-B01')).toBeTruthy();
    fireEvent.click(view.getByRole('button',{name:'Trang sau'}));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?warehouseId=1&limit=200&bucketOffset=500&eventAnchorId=543'
    ));
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'88'}});
    await act(async()=>resolvePage({data:{...result,currentBuckets:[
      {...result.currentBuckets[0],inventoryStockId:88,locationCode:'STALE'}
    ]}}));
    expect(view.queryByText('STALE')).toBeNull();
    expect(view.queryByRole('navigation',{name:'Phân trang nhóm tồn kho'})).toBeNull();
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?warehouseId=1&productId=88&limit=200'
    ));
    expect(await view.findByText('A01-R01-B01')).toBeTruthy();
    expect(view.getByText('Trang 1')).toBeTruthy();
  });

  it('paginates older ledger history while retaining stock filters and resets after a new search',async()=>{
    const older={...result,events:[{...result.events[0],transactionId:91,transactionDate:'2026-09-01T00:00:00Z'}],eventsTruncated:false};
    vi.mocked(apiClient.get).mockImplementation(async url=>
      String(url).includes('eventOffset=200')
        ? {data:older} as never
        : {data:{...result,eventsTruncated:true}} as never
    );
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    await view.findByText('Trang sự kiện 1');
    expect((view.getByRole('button',{name:'Sự kiện trước'}) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(view.getByRole('button',{name:'Sự kiện sau'}));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?productId=10&limit=200&eventOffset=200&eventAnchorId=543'
    ));
    expect(await view.findByText('Trang sự kiện 2')).toBeTruthy();
    expect((view.getByRole('button',{name:'Sự kiện sau'}) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(view.getByRole('button',{name:'Sự kiện trước'}));
    await view.findByText('Trang sự kiện 1');
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'11'}});
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?productId=11&limit=200'
    ));
  });


  it('fails closed when a legacy response has no ledger anchor for the next page',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,eventAnchorId:undefined,eventsTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    await view.findByText('Trang sự kiện 1');
    fireEvent.click(view.getByRole('button',{name:'Sự kiện sau'}));
    expect(view.getByRole('alert').textContent).toContain('Thiếu mốc lịch sử');
    expect(apiClient.get).toHaveBeenCalledTimes(1);
  });

  it('keeps the ledger anchor across stock paging but resets it for a new manual search',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,bucketsTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    await view.findByText('Trang 1');
    fireEvent.click(view.getByRole('button',{name:'Trang sau'}));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?productId=10&limit=200&bucketOffset=500&eventAnchorId=543'
    ));
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    await waitFor(()=>expect(apiClient.get).toHaveBeenLastCalledWith(
      '/api/inventory/traceability?productId=10&limit=200'
    ));
  });


  it('shows document occurrences across receipt, QC and shipment for the same tracked product lot',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,
      relatedDocuments:[
        {warehouseId:1,warehouseName:'DC HCM',referenceType:'Shipment',referenceId:99,
          eventCount:1,firstTransactionDate:'2026-10-07T12:00:00Z',
          lastTransactionDate:'2026-10-07T12:00:00Z',lastTransactionId:86},
        {warehouseId:1,warehouseName:'DC HCM',referenceType:'GoodsReceipt',referenceId:21,
          eventCount:2,firstTransactionDate:'2026-10-06T08:00:00Z',
          lastTransactionDate:'2026-10-06T09:00:00Z',lastTransactionId:30}
      ],relatedDocumentsTruncated:false
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByRole('table',{name:'Chứng từ liên quan cùng lô hoặc sê-ri'})).toBeTruthy();
    expect(view.getByText('Shipment #99')).toBeTruthy();
    expect(view.getByText('GoodsReceipt #21')).toBeTruthy();
    expect(view.getByText(/chưa chứng minh quan hệ giao nhận/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'
    );
  });

  it('warns on the 100-document bound and does not present an unrelated document scope',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,relatedDocuments:[{
        warehouseId:1,warehouseName:'DC HCM',referenceType:'StockTransfer',referenceId:12,
        eventCount:3,firstTransactionDate:'2026-10-07T10:00:00Z',
        lastTransactionDate:'2026-10-07T11:00:00Z',lastTransactionId:41
      }],relatedDocumentsTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Số sê-ri'),{target:{value:'SER-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByText(/Chỉ hiển thị 100 chứng từ mới nhất/)).toBeTruthy();
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'Shipment'}});
    expect(view.queryByRole('table',{name:'Chứng từ liên quan cùng lô hoặc sê-ri'})).toBeNull();
  });

  it('does not synthesize related documents for an untracked warehouse-only view',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    await view.findByRole('table',{name:'Dòng thời gian sổ cái phục vụ truy vết'});
    expect(view.queryByRole('table',{name:'Chứng từ liên quan cùng lô hoặc sê-ri'})).toBeNull();
  });


  it('shows SHIP evidence without implying completed recall or net delivered stock',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,
      shipmentExposures:[{
        shipmentId:42,warehouseId:1,shipmentCode:'SHIP-42',
        shipmentStatus:'ReturnToWarehouse',
        dispatchedAt:'2026-10-08T10:00:00Z',dispatchedQuantity:4,
        ledgerEventCount:2,lastTransactionId:82
      }]
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByRole('table',{name:'Shipment có giao dịch xuất giao cần rà soát'})).toBeTruthy();
    expect(view.getByText('SHIP-42')).toBeTruthy();
    expect(view.getByText('Đang xử lý hoàn về kho')).toBeTruthy();
    expect(view.getByText(/chưa trừ hàng hoàn/)).toBeTruthy();
    expect(view.getByText(/không tự động thu hồi/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'
    );
  });

  it('limits shipment recall candidates and invalidates them on reference filter changes',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentExposures:[],shipmentExposuresTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Số sê-ri'),{target:{value:'SER-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByText(/Đã đạt giới hạn 100 Shipment/)).toBeTruthy();
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'Shipment'}});
    expect(view.queryByRole('table',{name:'Shipment có giao dịch xuất giao cần rà soát'})).toBeNull();
    expect(view.queryByText(/Đã đạt giới hạn 100 Shipment/)).toBeNull();
  });

  it('does not show shipment recall candidates for warehouse-only searches',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{...result,shipmentExposures:[]}} as never);
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    await view.findByRole('table',{name:'Dòng thời gian sổ cái phục vụ truy vết'});
    expect(view.queryByRole('table',{name:'Shipment có giao dịch xuất giao cần rà soát'})).toBeNull();
    expect(view.queryByRole('table',{name:'Liên kết Shipment Packing Picking theo lô hoặc sê-ri'})).toBeNull();
  });


  it('shows canonical Shipment to Packing to Picking evidence only for tracked identity',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentPickingEvidence:[{
        shipmentId:42,shipmentCode:'SHIP-42',warehouseId:1,
        packingSessionId:15,packingSessionCode:'PACK-15',
        pickingTaskId:16,pickingTaskCode:'PICK-16',
        pickingTaskLineId:17,allocationId:18,
        sourceLocationCode:'A-01',pickedQuantity:10
      }]
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    const table=await view.findByRole('table',{name:'Liên kết Shipment Packing Picking theo lô hoặc sê-ri'});
    expect(table.textContent).toContain('SHIP-42');
    expect(table.textContent).toContain('PACK-15');
    expect(table.textContent).toContain('PICK-16');
    expect(table.textContent).toContain('A-01');
    expect(table.textContent).toContain('10');
    expect(view.getByText(/chưa chứng minh quan hệ đến phiếu nhập/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'
    );
  });

  it('caps canonical pick evidence and hides it when reference scope changes',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentPickingEvidence:[],shipmentPickingEvidenceTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Số sê-ri'),{target:{value:'SER-1'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByText(/Chỉ hiển thị 100 dòng Picking liên kết/)).toBeTruthy();
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'Shipment'}});
    expect(view.queryByRole('table',{name:'Liên kết Shipment Packing Picking theo lô hoặc sê-ri'})).toBeNull();
    expect(view.queryByText(/Chỉ hiển thị 100 dòng Picking liên kết/)).toBeNull();
  });

  it('can increase server event window to 500 without client-side truncation',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=renderTrace();
    expect((view.getByLabelText('Giới hạn sự kiện truy vết') as HTMLSelectElement).value).toBe('200');
    fireEvent.change(view.getByLabelText('Giới hạn sự kiện truy vết'),{
      target:{value:'500'}
    });
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?productId=10&limit=500'
    ));
  });

  it('surfaces request errors with a recovery path',async()=>{
    vi.mocked(apiClient.get).mockRejectedValue({response:{status:500,data:{message:'Máy chủ bận.'}}});
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    const alert=await view.findByRole('alert');
    expect(alert.textContent).toContain('Máy chủ bận.');
    expect(alert.textContent).toContain('thử lại');
  });

  it('announces loading state for asynchronous trace queries',async()=>{
    vi.mocked(apiClient.get).mockImplementation(()=>new Promise(()=>{}) as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getAllByRole('status').some(x=>x.textContent?.includes('Đang truy vết tồn kho'))).toBe(true);
    expect(view.getByText('Đang truy vết...')).toBeTruthy();
  });
});
