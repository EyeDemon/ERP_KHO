// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PackingSessions from './PackingSessions';
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

const line={pickingTaskLineId:41,productId:1,productCode:'SKU-1001',productName:'Cà phê',pickedQuantity:10,packedQuantity:2,remainingQuantity:8};
const hu={id:91,huCode:'HU-001',barcode:'HU-001',warehouseId:1,packingSessionId:77,type:'Carton',status:'InUse',createdAt:'2026-10-06T02:00:00Z',rowVersion:'HU1',contents:[{id:1,pickingTaskLineId:41,productId:1,productCode:'SKU-1001',productName:'Cà phê',quantity:2,packedAt:'2026-10-06T02:01:00Z'}]};
const summary={id:77,sessionCode:'PACK-2026-0077',pickingTaskId:88,pickingTaskCode:'PICK-2026-0088',sourceType:'ExportReceipt',sourceId:5108,sourceCode:'EX-2026-5108',warehouseId:1,warehouseName:'DC Hồ Chí Minh',status:'InProgress',requiredQuantity:10,packedQuantity:2,remainingQuantity:8,handlingUnitCount:1,createdAt:'2026-10-06T02:00:00Z'};
const detail={...summary,rowVersion:'AQ==',lines:[line],handlingUnits:[hu]};
const grant=(...permissions:string[])=>{permissionState.granted.clear();permissions.forEach(p=>permissionState.granted.add(p));};
const reads=(task:Record<string,unknown>=detail)=>vi.mocked(apiClient.get).mockImplementation(async url=>({data:url==='/api/packing-sessions'?[summary]:task}) as never);

describe('Packing workbench',()=>{
  afterEach(cleanup);
  beforeEach(()=>{vi.resetAllMocks();completeIdempotentAction.mockReset();grant('packing.read');reads()});

  it('shows packing evidence read-only without mutation permissions',async()=>{
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');
    fireEvent.click(view.getByText('PACK-2026-0077'));
    expect(await view.findByText(/Foundation hiện có HU create/)).toBeTruthy();
    expect(view.queryByText('Xác nhận Pack')).toBeNull();
    expect(view.queryByText('Tạo HU')).toBeNull();
  });

  it('submits scan-first pack once with idempotency',async()=>{
    grant('packing.read','packing.execute');
    vi.mocked(apiClient.post).mockResolvedValue({data:{...detail,packedQuantity:4,remainingQuantity:6}} as never);
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');
    fireEvent.click(view.getByText('PACK-2026-0077'));
    await view.findByLabelText('HU đích SKU-1001');
    fireEvent.change(view.getByLabelText('HU đích SKU-1001'),{target:{value:'91'}});
    fireEvent.change(view.getByLabelText('Product barcode packing SKU-1001'),{target:{value:'SKU-1001'}});
    fireEvent.change(view.getByLabelText('Packing quantity SKU-1001'),{target:{value:'2'}});
    const button=view.getByText('Xác nhận Pack');
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/packing-sessions/77/pack',{
      handlingUnitId:91,pickingTaskLineId:41,productBarcode:'SKU-1001',quantity:2,rowVersion:'AQ=='
    },{headers:{'Idempotency-Key':'key:packing-pack-77-41'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('packing-pack-77-41');
  });

  it('maps over-pack conflict to actionable Vietnamese copy',async()=>{
    grant('packing.read','packing.execute');
    vi.mocked(apiClient.post).mockRejectedValue({response:{status:409,data:{code:'PACK_QUANTITY_EXCEEDS_PICKED',message:'wrong'}}});
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');
    fireEvent.click(view.getByText('PACK-2026-0077'));
    await view.findByLabelText('HU đích SKU-1001');
    fireEvent.change(view.getByLabelText('HU đích SKU-1001'),{target:{value:'91'}});
    fireEvent.change(view.getByLabelText('Product barcode packing SKU-1001'),{target:{value:'SKU-1001'}});
    fireEvent.change(view.getByLabelText('Packing quantity SKU-1001'),{target:{value:'9'}});
    fireEvent.click(view.getByText('Xác nhận Pack'));
    await waitFor(()=>expect(view.getByRole('alert').textContent).toContain('vượt số lượng đã Picking'));
  });

  it('shows HU create and structure controls only with canonical permissions',async()=>{
    grant('packing.read','handling_unit.create','handling_unit.modify');
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');
    fireEvent.click(view.getByText('PACK-2026-0077'));
    expect(await view.findByText('Tạo HU')).toBeTruthy();
    expect(view.getByText('Hủy HU rỗng')).toBeTruthy();
  });
});
