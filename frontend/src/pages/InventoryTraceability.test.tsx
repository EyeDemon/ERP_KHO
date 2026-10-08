// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryTraceability from './InventoryTraceability';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn()}}));

const result={
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
  beforeEach(()=>vi.resetAllMocks());
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
        <Routes><Route path="/inventory-traceability" element={<InventoryTraceability/>}/></Routes>
      </MemoryRouter>
    );
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?referenceType=InventoryReversal&referenceId=41&limit=200'
    ));
    fireEvent.click(view.getByText('Mở chuỗi đảo khác'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?referenceType=InventoryReversal&referenceId=84&limit=200'
    ));
    expect((view.getByLabelText('ID tham chiếu') as HTMLInputElement).value).toBe('84');
    expect(await view.findByText('#84 • đã đảo')).toBeTruthy();
    // Flush the delayed first response before asserting: otherwise the
    // assertion can pass prematurely without exercising the race.
    await act(async()=>{resolveFirst({data:result});});
    expect(view.queryByText('#41 • đã đảo')).toBeNull();
    expect((view.getByLabelText('ID tham chiếu') as HTMLInputElement).value).toBe('84');
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

  it('renders visible labels, current buckets and reversal-aware immutable timeline',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=renderTrace();
    expect(view.getByLabelText('ID kho (tùy chọn)')).toBeTruthy();
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
    expect(view.getByRole('status').textContent).toContain('Đã tải 1 nhóm tồn kho hiện tại và 2 sự kiện sổ cái');
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
    expect(view.getByRole('status').textContent).toContain('Đang truy vết tồn kho');
    expect(view.getByText('Đang truy vết...')).toBeTruthy();
  });
});
