// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
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

describe('InventoryTraceability',()=>{
  beforeEach(()=>vi.resetAllMocks());
  afterEach(cleanup);

  it('requires at least identity or reference and focuses the primary identity field',()=>{
    const view=render(<InventoryTraceability/>);
    const productId=view.getByLabelText('Product ID');
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getByRole('alert').textContent).toContain('ít nhất');
    expect(document.activeElement).toBe(productId);
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('requires reference type and id together and exposes inline field state',()=>{
    const view=render(<InventoryTraceability/>);
    const referenceType=view.getByLabelText('Reference Type');
    const referenceId=view.getByLabelText('Reference ID');
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
    const view=render(<InventoryTraceability/>);
    expect(view.getByLabelText('Warehouse ID (tùy chọn)')).toBeTruthy();
    expect(view.getByLabelText('Product ID')).toBeTruthy();
    expect(view.getByLabelText('Lot number')).toBeTruthy();
    expect(view.getByLabelText('Serial number')).toBeTruthy();
    expect(view.getByLabelText('Reference Type')).toBeTruthy();
    expect(view.getByLabelText('Reference ID')).toBeTruthy();
    fireEvent.change(view.getByLabelText('Product ID'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Lot number'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith('/api/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'));
    expect(await view.findByText('A01-R01-B01')).toBeTruthy();
    expect(view.getByText('Reversal')).toBeTruthy();
    expect(view.getByText(/đã reversal/)).toBeTruthy();
    expect(view.getByText(/đảo #41/)).toBeTruthy();
    expect(view.getAllByText(/Corrective #42/).length).toBeGreaterThan(0);
    expect(view.getAllByText(/Marker #43/).length).toBeGreaterThan(0);
    expect(view.getByText(/Original #41/)).toBeTruthy();
    expect(view.getByRole('status').textContent).toContain('Đã tải 1 bucket hiện tại và 2 ledger event');
  });

  it('surfaces request errors with a recovery path',async()=>{
    vi.mocked(apiClient.get).mockRejectedValue({response:{status:500,data:{message:'Máy chủ bận.'}}});
    const view=render(<InventoryTraceability/>);
    fireEvent.change(view.getByLabelText('Product ID'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    const alert=await view.findByRole('alert');
    expect(alert.textContent).toContain('Máy chủ bận.');
    expect(alert.textContent).toContain('thử lại');
  });

  it('announces loading state for asynchronous trace queries',async()=>{
    vi.mocked(apiClient.get).mockImplementation(()=>new Promise(()=>{}) as never);
    const view=render(<InventoryTraceability/>);
    fireEvent.change(view.getByLabelText('Product ID'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getByRole('status').textContent).toContain('Đang truy vết inventory');
    expect(view.getByText('Đang truy vết...')).toBeTruthy();
  });
});
