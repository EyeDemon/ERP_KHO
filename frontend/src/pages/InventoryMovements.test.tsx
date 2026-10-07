// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryMovements from './InventoryMovements';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const permissionState=vi.hoisted(()=>({granted:new Set<string>()}));
vi.mock('../services/authorization',()=>({usePermission:(code:string)=>permissionState.granted.has(code)}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':`key:${key}`}),
  completeIdempotentAction,
}));

const bucket={
  inventoryStockId:51,productId:10,productCode:'SKU-10',productName:'Sản phẩm 10',
  warehouseId:1,warehouseName:'DC Hồ Chí Minh',locationId:7,locationCode:'A01-R01-B01',
  status:'AVAILABLE',lotId:100,lotNumber:'LOT-A',onHandQuantity:10,reservedQuantity:2,availableQuantity:8
};
const reads=()=>vi.mocked(apiClient.get).mockImplementation(async url=>{
  const value=String(url);
  if(value==='/api/inventory/buckets?')return {data:[bucket]} as never;
  if(value==='/api/putaway-tasks/locations?warehouseId=1')return {data:[
    {id:7,warehouseId:1,code:'A01-R01-B01',name:'Source',isActive:true,isBlocked:false,isSystemManaged:false},
    {id:8,warehouseId:1,code:'A01-R01-B02',name:'Destination',isActive:true,isBlocked:false,isSystemManaged:false}
  ]} as never;
  return {data:[]} as never;
});

describe('InventoryMovements',()=>{
  beforeEach(()=>{vi.resetAllMocks();completeIdempotentAction.mockReset();permissionState.granted.clear();permissionState.granted.add('inventory.read');reads()});
  afterEach(cleanup);

  it('shows buckets read-only without movement permission',async()=>{
    const view=render(<InventoryMovements/>);
    expect(await view.findByText(/LOT-A/)).toBeTruthy();
    expect(view.queryByText('Move')).toBeNull();
  });

  it('posts internal move once and preserves idempotency key',async()=>{
    permissionState.granted.add('inventory_movement.create');
    vi.mocked(apiClient.post).mockResolvedValue({data:{movementId:1}} as never);
    const view=render(<InventoryMovements/>);
    await view.findByText(/LOT-A/);
    fireEvent.click(view.getByText('Move'));
    await view.findByLabelText('Vị trí đích internal move');
    fireEvent.change(view.getByLabelText('Vị trí đích internal move'),{target:{value:'8'}});
    fireEvent.change(view.getByLabelText('Số lượng internal move'),{target:{value:'4'}});
    fireEvent.change(view.getByLabelText('Lý do internal move'),{target:{value:'Slotting optimization'}});
    const button=view.getByText('Xác nhận Move');
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/inventory/movements',{
      inventoryStockId:51,destinationLocationId:8,quantity:4,reason:'Slotting optimization'
    },{headers:{'Idempotency-Key':'key:inventory-move-51'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('inventory-move-51');
  });

  it('maps lock conflict to actionable message',async()=>{
    permissionState.granted.add('inventory_movement.create');
    vi.mocked(apiClient.post).mockRejectedValue({response:{status:409,data:{code:'INV_STOCK_LOCKED',message:'Inventory bucket đang bị khóa bởi Inventory Lock.'}}});
    const view=render(<InventoryMovements/>);
    await view.findByText(/LOT-A/);
    fireEvent.click(view.getByText('Move'));
    await view.findByLabelText('Vị trí đích internal move');
    fireEvent.change(view.getByLabelText('Vị trí đích internal move'),{target:{value:'8'}});
    fireEvent.change(view.getByLabelText('Số lượng internal move'),{target:{value:'1'}});
    fireEvent.change(view.getByLabelText('Lý do internal move'),{target:{value:'Move blocked'}});
    fireEvent.click(view.getByText('Xác nhận Move'));
    expect((await view.findByRole('alert')).textContent).toContain('khóa');
  });
});
