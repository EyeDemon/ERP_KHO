// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryReversals from './InventoryReversals';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const permissionState=vi.hoisted(()=>({granted:new Set<string>()}));
vi.mock('../services/authorization',()=>({usePermission:(code:string)=>permissionState.granted.has(code)}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':'key:'+key}),
  completeIdempotentAction,
}));

const move={id:41,productId:10,productCode:'SKU-10',productName:'Product 10',warehouseId:1,warehouseName:'DC HCM',
  transactionType:'Move',inventoryStatus:'Available',lotNumber:'LOT-A',quantity:4,transactionDate:'2026-10-07T10:00:00Z'};
const statusChange={id:42,productId:10,productCode:'SKU-10',productName:'Product 10',warehouseId:1,warehouseName:'DC HCM',
  transactionType:'StatusChange',inventoryStatus:'QcHold',fromInventoryStatus:'Available',toInventoryStatus:'QcHold',quantity:2,transactionDate:'2026-10-07T11:00:00Z'};
const reads=()=>vi.mocked(apiClient.get).mockImplementation(async url=>{
  const value=String(url);
  if(value.includes('transactionType=Move'))return {data:{items:[move]}} as never;
  if(value.includes('transactionType=StatusChange'))return {data:{items:[statusChange]}} as never;
  if(value.includes('transactionType=Reversal'))return {data:{items:[]}} as never;
  return {data:{items:[]}} as never;
});

describe('InventoryReversals',()=>{
  beforeEach(()=>{vi.resetAllMocks();completeIdempotentAction.mockReset();permissionState.granted.clear();permissionState.granted.add('inventory_ledger.read');reads()});
  afterEach(cleanup);

  it('shows supported transactions read-only without reversal permission',async()=>{
    const view=render(<InventoryReversals/>);
    expect(await view.findByText('Đổi trạng thái')).toBeTruthy();
    expect(view.getByText('Di chuyển vị trí')).toBeTruthy();
    expect(view.queryByText('Đảo giao dịch')).toBeNull();
  });

  it('posts reversal once with idempotency key',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    vi.mocked(apiClient.post).mockResolvedValue({data:{reversalTransactionId:99}} as never);
    const view=render(<InventoryReversals/>);
    await view.findByText('Đổi trạng thái');
    const buttons=view.getAllByText('Đảo giao dịch');
    fireEvent.click(buttons[0]);
    fireEvent.change(view.getByLabelText('Lý do đảo giao dịch tồn kho'),{target:{value:'Hiệu chỉnh theo xác nhận của người vận hành'}});
    const submit=view.getByText('Xác nhận đảo giao dịch');
    fireEvent.click(submit);fireEvent.click(submit);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/inventory/reversals',{
      originalTransactionId:42,reason:'Hiệu chỉnh theo xác nhận của người vận hành'
    },{headers:{'Idempotency-Key':'key:inventory-reversal-42'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('inventory-reversal-42');
  });

  it('disables reversal when marker already references original transaction',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      const value=String(url);
      if(value.includes('transactionType=Move'))return {data:{items:[move]}} as never;
      if(value.includes('transactionType=StatusChange'))return {data:{items:[]}} as never;
      if(value.includes('transactionType=Reversal'))return {data:{items:[{...move,id:90,transactionType:'Reversal',referenceType:'InventoryReversal',referenceId:41}]}} as never;
      return {data:{items:[]}} as never;
    });
    const view=render(<InventoryReversals/>);
    expect(await view.findByText('Đã đảo')).toBeTruthy();
    expect((view.getByText('Đảo giao dịch') as HTMLButtonElement).disabled).toBe(true);
  });
});
