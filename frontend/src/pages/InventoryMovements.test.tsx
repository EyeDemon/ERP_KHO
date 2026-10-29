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
    expect(view.queryByText('Di chuyển')).toBeNull();
  });

  it('posts internal move once and preserves idempotency key',async()=>{
    permissionState.granted.add('inventory_movement.create');
    vi.mocked(apiClient.post).mockResolvedValue({data:{movementId:1}} as never);
    const view=render(<InventoryMovements/>);
    await view.findByText(/LOT-A/);
    fireEvent.click(view.getByText('Di chuyển'));
    await view.findByLabelText('Vị trí đích internal move');
    fireEvent.change(view.getByLabelText('Vị trí đích internal move'),{target:{value:'8'}});
    fireEvent.change(view.getByLabelText('Số lượng internal move'),{target:{value:'4'}});
    fireEvent.change(view.getByLabelText('Lý do internal move'),{target:{value:'Slotting optimization'}});
    const button=view.getByText('Xác nhận di chuyển');
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
    fireEvent.click(view.getByText('Di chuyển'));
    await view.findByLabelText('Vị trí đích internal move');
    fireEvent.change(view.getByLabelText('Vị trí đích internal move'),{target:{value:'8'}});
    fireEvent.change(view.getByLabelText('Số lượng internal move'),{target:{value:'1'}});
    fireEvent.change(view.getByLabelText('Lý do internal move'),{target:{value:'Move blocked'}});
    fireEvent.click(view.getByText('Xác nhận di chuyển'));
    expect((await view.findByRole('alert')).textContent).toContain('khóa');
  });

  it('executes 1-scan full-LPN pallet move and auto break-bulk replenishment with carton-to-base conversion',async()=>{
    permissionState.granted.add('inventory_movement.create');
    vi.mocked(apiClient.post).mockImplementation(async(url:string)=>{
      if(url==='/api/v1/inventory-movements/move-handling-unit'||url==='/api/v1/inventory/movements/move-handling-unit'){
        return {
          data:{
            handlingUnitId:50,
            handlingUnitCode:'PALLET-LPN-001',
            destinationLocationCode:'BIN-A01-01',
            movedBucketCount:2,
            totalMovedQuantity:14,
            message:'Đã di chuyển nguyên kiện PALLET-LPN-001 (2 dòng tồn kho, tổng 14 đơn vị) sang vị trí BIN-A01-01.',
          }
        } as never;
      }
      if(url==='/api/v1/inventory-control/replenishment/break-bulk'||url==='/api/v1/inventory-movements/auto-break-bulk-replenish'||url==='/api/v1/inventory/movements/auto-break-bulk-replenish'){
        return {
          data:{
            replenishmentId:'BB-REPL-20261028-TEST',
            productCode:'QA-E2E-001',
            productName:'Thùng linh kiện điện tử QA-01',
            sourceLocationCode:'BIN-A01',
            targetLocationCode:'BIN-A01-01',
            sourcePackQty:1,
            conversionFactor:12,
            movedBaseQty:12,
            sourceUomCode:'CASE',
            baseUomCode:'THÙNG',
            baseUomDelta:0,
          }
        } as never;
      }
      return {data:{}} as never;
    });

    const view=render(<InventoryMovements/>);
    await view.findByText(/LOT-A/);

    fireEvent.change(view.getByLabelText('Mã Pallet / LPN / SSCC cần di chuyển'),{target:{value:'PALLET-LPN-001'}});
    fireEvent.change(view.getByLabelText('Vị trí đích di chuyển nguyên Pallet'),{target:{value:'BIN-A01-01'}});
    fireEvent.click(view.getByTestId('btn-move-full-lpn'));

    const lpnResult=await view.findByTestId('full-lpn-move-result');
    expect(lpnResult.textContent).toContain('PALLET-LPN-001');
    expect(lpnResult.textContent).toContain('BIN-A01-01');

    fireEvent.click(view.getByTestId('btn-auto-break-bulk'));
    expect(await view.findByTestId('break-bulk-replenish-notice')).toBeTruthy();
    const bbTable=await view.findByTestId('break-bulk-conversion-table');
    expect(bbTable.textContent).toContain('BIN-A01');
    expect(bbTable.textContent).toContain('BIN-A01-01');
    expect(bbTable.textContent).toContain('1 Thùng = 12 ĐV');
    expect(bbTable.textContent).toContain('+12 ĐV');
    expect(bbTable.textContent).toContain('Khớp 2 bút toán (-1 THÙNG / +12 ĐV • Δ = 0.0000)');
  });
});
