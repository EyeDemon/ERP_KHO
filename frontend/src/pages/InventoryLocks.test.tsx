// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryLocks from './InventoryLocks';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const permissionState=vi.hoisted(()=>({granted:new Set<string>()}));
vi.mock('../services/authorization',()=>({usePermission:(code:string)=>permissionState.granted.has(code)}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':`key:${key}`}),
  completeIdempotentAction,
}));

const lock={
  id:9,lockType:'QualityHold',status:'Active',warehouseId:1,warehouseName:'DC Hồ Chí Minh',
  locationId:7,locationCode:'A01-R01-B01',productId:10,productCode:'SKU-10',inventoryStatus:'AVAILABLE',
  lotId:100,lotNumber:'LOT-A',reason:'Quality evidence',createdAt:'2026-10-07T10:00:00Z',createdBy:7,rowVersion:'AQ=='
};
const reads=()=>vi.mocked(apiClient.get).mockImplementation(async url=>{
  if(url==='/api/inventory/locks')return {data:[lock]} as never;
  if(url==='/api/Warehouses')return {data:[{id:1,name:'DC Hồ Chí Minh',isActive:true}]} as never;
  if(url==='/api/inventory/statuses')return {data:[{code:'AVAILABLE',name:'Available'}]} as never;
  return {data:[]} as never;
});

describe('InventoryLocks',()=>{
  beforeEach(()=>{vi.resetAllMocks();completeIdempotentAction.mockReset();permissionState.granted.clear();permissionState.granted.add('inventory_lock.read');reads()});
  afterEach(cleanup);

  it('shows active lock read-only without manage permission',async()=>{
    const view=render(<InventoryLocks/>);
    expect(await view.findByText('QualityHold')).toBeTruthy();
    expect(view.getByText(/LOT-A/)).toBeTruthy();
    expect(view.queryByText('Tạo Lock')).toBeNull();
    expect(view.queryByText('Release')).toBeNull();
  });

  it('creates lock once with canonical manage permission and idempotency',async()=>{
    permissionState.granted.add('inventory_lock.manage');
    vi.mocked(apiClient.post).mockResolvedValue({data:lock} as never);
    const view=render(<InventoryLocks/>);
    await view.findByText('QualityHold');
    fireEvent.change(view.getByLabelText('Warehouse lock'),{target:{value:'1'}});
    fireEvent.change(view.getByLabelText('Product ID lock'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Lý do Inventory Lock'),{target:{value:'Cycle count freeze'}});
    const button=view.getByText('Tạo Lock');
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/inventory/locks',expect.objectContaining({
      lockType:'ManualOperationalLock',warehouseId:1,productId:10,reason:'Cycle count freeze'
    }),{headers:{'Idempotency-Key':'key:inventory-lock-create'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('inventory-lock-create');
  });

  it('releases lock with rowVersion',async()=>{
    permissionState.granted.add('inventory_lock.manage');
    vi.mocked(apiClient.post).mockResolvedValue({data:{...lock,status:'Released'}} as never);
    const view=render(<InventoryLocks/>);
    await view.findByText('QualityHold');
    fireEvent.click(view.getByText('Release'));
    fireEvent.change(view.getByLabelText('Lý do release Inventory Lock'),{target:{value:'Investigation cleared'}});
    fireEvent.click(view.getByText('Xác nhận release'));
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledWith('/api/inventory/locks/9/release',{
      reason:'Investigation cleared',rowVersion:'AQ=='
    },{headers:{'Idempotency-Key':'key:inventory-lock-release-9'}}));
  });
});
