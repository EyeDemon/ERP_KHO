// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryBuckets from './InventoryBuckets';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const permissionState=vi.hoisted(()=>({granted:new Set<string>()}));
vi.mock('../services/authorization',()=>({
  usePermission:(code:string)=>permissionState.granted.has(code),
}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':`key:${key}`}),
  completeIdempotentAction,
}));

const statuses=[
  {code:'AVAILABLE',name:'Available',isAvailable:true,isReservable:true,isAllocatable:true,isPickable:true,isShippable:true},
  {code:'QUARANTINE',name:'Quarantine',isAvailable:false,isReservable:false,isAllocatable:false,isPickable:false,isShippable:false},
];
const bucket={
  inventoryStockId:51,productId:10,productCode:'SKU-10',productName:'Sản phẩm 10',
  warehouseId:1,warehouseName:'DC Hồ Chí Minh',locationId:7,locationCode:'A01-R01-B01',
  status:'AVAILABLE',isReservable:true,isAllocatable:true,isPickable:true,isShippable:true,
  lotId:100,lotNumber:'LOT-A',manufactureDate:'2026-01-01T00:00:00Z',expiryDate:'2026-12-31T00:00:00Z',
  serialId:null,serialNumber:null,onHandQuantity:10,reservedQuantity:2,availableQuantity:8,lastUpdated:'2026-10-07T08:00:00Z',
};
const warehouses=[{id:1,name:'DC Hồ Chí Minh',isActive:true}];

const reads=()=>{
  vi.mocked(apiClient.get).mockImplementation(async url=>{
    const value=String(url);
    if(value==='/api/inventory/statuses')return {data:statuses} as never;
    if(value.startsWith('/api/inventory/buckets?'))return {data:[bucket]} as never;
    return {data:[]} as never;
  });
};

describe('InventoryBuckets',()=>{
  beforeEach(()=>{
    vi.resetAllMocks();
    completeIdempotentAction.mockReset();
    permissionState.granted.clear();
    permissionState.granted.add('inventory.read');
    reads();
  });
  afterEach(cleanup);

  it('renders canonical status lot serial dimensions read-only without mutation permission',async()=>{
    const view=render(<InventoryBuckets warehouses={warehouses}/>);
    expect(await view.findByText('LOT-A')).toBeTruthy();
    expect(within(view.getByRole('table',{name:'Inventory bucket'})).getByText('AVAILABLE')).toBeTruthy();
    expect(view.getByText(/R:✓/)).toBeTruthy();
    expect(view.queryByText('Đổi status')).toBeNull();
  });

  it('filters bucket query by warehouse status lot serial and product',async()=>{
    const view=render(<InventoryBuckets warehouses={warehouses}/>);
    await view.findByText('LOT-A');
    fireEvent.change(view.getByLabelText('Kho bucket'),{target:{value:'1'}});
    fireEvent.change(view.getByLabelText('ID sản phẩm bucket'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Inventory status filter'),{target:{value:'AVAILABLE'}});
    fireEvent.change(view.getByLabelText('Lot filter'),{target:{value:'LOT-A'}});
    fireEvent.change(view.getByLabelText('Serial filter'),{target:{value:'SER-1'}});
    fireEvent.click(view.getByText('Lọc bucket'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/buckets?warehouseId=1&productId=10&status=AVAILABLE&lotNumber=LOT-A&serialNumber=SER-1'
    ));
  });

  it('posts status change once with canonical permission and idempotency',async()=>{
    permissionState.granted.add('inventory_status_change.create');
    vi.mocked(apiClient.post).mockResolvedValue({data:{}} as never);
    const view=render(<InventoryBuckets warehouses={warehouses}/>);
    await view.findByText('LOT-A');
    fireEvent.click(view.getByText('Đổi status'));
    expect((view.getByLabelText('Số lượng đổi status') as HTMLInputElement).value).toBe('8');
    fireEvent.change(view.getByLabelText('Status đích'),{target:{value:'QUARANTINE'}});
    fireEvent.change(view.getByLabelText('Lý do đổi status'),{target:{value:'Quality hold evidence'}});
    const submit=view.getByText('Xác nhận status change');
    fireEvent.click(submit);fireEvent.click(submit);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/inventory/status-changes',{
      inventoryStockId:51,quantity:8,toStatus:'QUARANTINE',reason:'Quality hold evidence'
    },{headers:{'Idempotency-Key':'key:inventory-status-change-51'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('inventory-status-change-51');
  });

  it('maps reserved-bucket status rejection to canonical message',async()=>{
    permissionState.granted.add('inventory_status_change.create');
    vi.mocked(apiClient.post).mockRejectedValue({
      response:{status:409,data:{code:'INV_STATUS_CHANGE_NOT_ALLOWED',message:'Không thể đổi status phần tồn đang reserved/allocation.'}}
    });
    const view=render(<InventoryBuckets warehouses={warehouses}/>);
    await view.findByText('LOT-A');
    fireEvent.click(view.getByText('Đổi status'));
    fireEvent.change(view.getByLabelText('Status đích'),{target:{value:'QUARANTINE'}});
    fireEvent.change(view.getByLabelText('Lý do đổi status'),{target:{value:'Quality hold'}});
    fireEvent.click(view.getByText('Xác nhận status change'));
    expect((await view.findByRole('alert')).textContent).toContain('reserved/allocation');
  });
});
