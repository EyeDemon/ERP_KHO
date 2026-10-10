// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryTraceability from './InventoryTraceability';
import apiClient from '../services/apiClient';
import { getTraceabilityWarehouses } from '../services/traceabilityWarehouses';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn()}}));
vi.mock('../services/traceabilityWarehouses',()=>({getTraceabilityWarehouses:vi.fn()}));

const show=()=>render(<MemoryRouter initialEntries={['/inventory-traceability']}>
  <Routes><Route path="/inventory-traceability" element={<InventoryTraceability/>}/></Routes>
</MemoryRouter>);

describe('Receipt evidence in inventory traceability',()=>{
  beforeEach(()=>{
    vi.resetAllMocks();
    vi.mocked(getTraceabilityWarehouses).mockResolvedValue([{id:1,code:'HCM',name:'Kho TP.HCM'}]);
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      eventAnchorId:42,currentBuckets:[],events:[],
      receiptExposures:[{receiptId:21,warehouseId:1,receiptCode:'PN-21',
        postedQuantity:4,lastPostedAt:'2026-10-09T08:00:00Z',
        ledgerEventCount:2,lastTransactionId:40}]
    }} as never);
  });
  afterEach(()=>cleanup());

  it('shows actual posted receipt evidence without asserting per-lot QC lineage',async()=>{
    const view=show();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByRole('table',{name:'Phiếu nhập đã ghi sổ theo lô hoặc sê-ri'})).toBeTruthy();
    expect(view.getByText('PN-21')).toBeTruthy();
    expect(view.getByText(/không chứng minh QC riêng từng lô/)).toBeTruthy();
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'));
  });

  it('warns on capped results and hides receipt view for explicit references',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      eventAnchorId:42,currentBuckets:[],events:[],
      receiptExposures:[],receiptExposuresTruncated:true
    }} as never);
    const view=show();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Số sê-ri'),{target:{value:'SER-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByText(/Đã đạt giới hạn 100 phiếu nhập/)).toBeTruthy();
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'ImportReceipt'}});
    expect(view.queryByRole('table',{name:'Phiếu nhập đã ghi sổ theo lô hoặc sê-ri'})).toBeNull();
  });
});
