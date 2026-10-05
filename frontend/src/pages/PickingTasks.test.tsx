// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PickingTasks from './PickingTasks';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const permissionState=vi.hoisted(()=>({granted:new Set<string>()}));
vi.mock('../services/authorization',()=>({
  usePermission:(code:string)=>permissionState.granted.has(code),
  currentUserId:()=>7,
}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':`key:${key}`}),
  completeIdempotentAction,
}));

const line={id:31,allocationId:7711,allocationCode:'ALC-7711',productId:1,productCode:'SKU-1001',productName:'Cà phê',sourceLocationId:101,sourceLocationCode:'A01-R02-L03-B04',sourceLocationName:'Bin B04',requestedQuantity:10,pickedQuantity:0,remainingQuantity:10,sequence:1,status:'InProgress'};
const summary={id:88,taskCode:'PICK-2026-0088',sourceType:'ExportReceipt',sourceId:5108,sourceCode:'EX-2026-5108',warehouseId:1,warehouseName:'DC Hồ Chí Minh',pickingType:'STANDARD',status:'InProgress',priority:0,assignedUserId:7,assignedUserName:'Picker',requestedQuantity:10,pickedQuantity:0,remainingQuantity:10,createdAt:'2026-10-06T01:00:00Z'};
const detail={...summary,rowVersion:'AQ==',lines:[line],shortPicks:[]};
const grant=(...permissions:string[])=>{permissionState.granted.clear();permissions.forEach(p=>permissionState.granted.add(p));};
const reads=(task=detail)=>vi.mocked(apiClient.get).mockImplementation(async url=>({data:url==='/api/picking-tasks'?[summary]:task}) as never);

describe('Picking workbench',()=>{
  afterEach(cleanup);
  beforeEach(()=>{vi.resetAllMocks();completeIdempotentAction.mockReset();grant('picking.read');reads()});

  it('shows read-only picking evidence without mutation permissions',async()=>{
    const view=render(<PickingTasks/>);
    await view.findByText('PICK-2026-0088');
    fireEvent.click(view.getByText('PICK-2026-0088'));
    expect(await view.findByText(/Vị trí: A01-R02-L03-B04/)).toBeTruthy();
    expect(view.queryByText('Xác nhận Pick')).toBeNull();
    expect(view.queryByText('Báo Short Pick')).toBeNull();
  });

  it('submits scan-first pick once with idempotency',async()=>{
    grant('picking.read','picking.execute');
    vi.mocked(apiClient.post).mockResolvedValue({data:{...detail,lines:[{...line,pickedQuantity:2,remainingQuantity:8}]}} as never);
    const view=render(<PickingTasks/>);
    await view.findByText('PICK-2026-0088');
    fireEvent.click(view.getByText('PICK-2026-0088'));
    await view.findByLabelText('Location barcode SKU-1001');
    fireEvent.change(view.getByLabelText('Location barcode SKU-1001'),{target:{value:'A01-R02-L03-B04'}});
    fireEvent.change(view.getByLabelText('Product barcode SKU-1001'),{target:{value:'SKU-1001'}});
    fireEvent.change(view.getByLabelText('Pick quantity SKU-1001'),{target:{value:'2'}});
    const button=view.getByText('Xác nhận Pick');
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/picking-tasks/88/pick',{
      taskLineId:31,locationBarcode:'A01-R02-L03-B04',productBarcode:'SKU-1001',quantity:2,rowVersion:'AQ=='
    },{headers:{'Idempotency-Key':'key:picking-pick-88-31'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('picking-pick-88-31');
  });

  it('maps wrong-location conflict to actionable Vietnamese copy',async()=>{
    grant('picking.read','picking.execute');
    vi.mocked(apiClient.post).mockRejectedValue({response:{status:409,data:{code:'PICK_WRONG_LOCATION',message:'wrong'}}});
    const view=render(<PickingTasks/>);
    await view.findByText('PICK-2026-0088');
    fireEvent.click(view.getByText('PICK-2026-0088'));
    await view.findByLabelText('Location barcode SKU-1001');
    fireEvent.change(view.getByLabelText('Location barcode SKU-1001'),{target:{value:'WRONG'}});
    fireEvent.change(view.getByLabelText('Product barcode SKU-1001'),{target:{value:'SKU-1001'}});
    fireEvent.change(view.getByLabelText('Pick quantity SKU-1001'),{target:{value:'1'}});
    fireEvent.click(view.getByText('Xác nhận Pick'));
    await waitFor(()=>expect(view.getByRole('alert').textContent).toContain('Sai vị trí lấy hàng'));
  });

  it('shows short-pick resolution only with short-pick permission',async()=>{
    const short={id:9,pickingTaskLineId:31,expectedQuantity:10,pickedQuantity:4,shortageQuantity:6,reason:'Thiếu vật lý',status:'Open',createdAt:'2026-10-06T01:10:00Z'};
    grant('picking.read','picking.short_pick');
    reads({...detail,status:'ShortPick',shortPicks:[short],lines:[{...line,status:'ShortPick',pickedQuantity:4,remainingQuantity:6}]});
    const view=render(<PickingTasks/>);
    await view.findByText('PICK-2026-0088');
    fireEvent.click(view.getByText('PICK-2026-0088'));
    expect(await view.findByLabelText('Short pick resolution 9')).toBeTruthy();
    expect(view.queryByText('Supervisor override')).toBeNull();
  });
});
