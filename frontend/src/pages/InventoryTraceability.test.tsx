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

  it('requires at least identity or reference before calling API',()=>{
    const view=render(<InventoryTraceability/>);
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getByRole('alert').textContent).toContain('ít nhất');
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('requires reference type and id together',()=>{
    const view=render(<InventoryTraceability/>);
    fireEvent.change(view.getByLabelText('Reference Type traceability'),{target:{value:'Shipment'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getByRole('alert').textContent).toContain('cùng nhau');
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('renders current buckets and reversal-aware immutable timeline',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=render(<InventoryTraceability/>);
    fireEvent.change(view.getByLabelText('Product ID traceability'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Lot traceability'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith('/api/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'));
    expect(await view.findByText('A01-R01-B01')).toBeTruthy();
    expect(view.getByText('Reversal')).toBeTruthy();
    expect(view.getByText(/đã reversal/)).toBeTruthy();
    expect(view.getByText(/đảo #41/)).toBeTruthy();
    expect(view.getAllByText(/Corrective #42/).length).toBeGreaterThan(0);
    expect(view.getAllByText(/Marker #43/).length).toBeGreaterThan(0);
    expect(view.getByText(/Original #41/)).toBeTruthy();
  });
});
