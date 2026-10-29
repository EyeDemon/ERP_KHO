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
const detail={...summary,rowVersion:'AQ==',lines:[line],shortPicks:[] as Array<Record<string, unknown>>};
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

  it('renders mobile 2-line nowrap route cells with →, right padding >= 8px, vertical label stacking, ui-subcard, and 4-col wave table',async()=>{
    grant('picking.read','picking.execute');
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(url==='/api/picking-tasks') return {data:[summary]} as never;
      if(url==='/api/picking-tasks/wave-summary') return {data:[{waveCode:'WAVE-W1-P0-STANDARD',warehouseId:1,warehouseName:'DC Hồ Chí Minh',pickingType:'STANDARD',priority:0,taskCount:1,openTaskCount:1,totalRequestedQuantity:10,totalPickedQuantity:0,totalRemainingQuantity:10,taskCodes:['PICK-2026-0088']}]} as never;
      return {data:detail} as never;
    });
    const view=render(<PickingTasks/>);
    const taskBtn=await view.findByRole('button',{name:'PICK-2026-0088'});
    expect(taskBtn.style.whiteSpace).toBe('nowrap');
    expect(view.getByText('WAVE-QA-001')).toBeTruthy();
    expect(view.getByText('1 phiếu (1 mở)')).toBeTruthy();
    const routeSourceSpan=view.getByText('EX-2026-5108');
    const routeTargetSpan=view.getByText('→ BIN-PICK');
    expect(routeSourceSpan.tagName).toBe('SPAN');
    expect(routeSourceSpan.style.whiteSpace).toBe('nowrap');
    expect(routeTargetSpan.tagName).toBe('SPAN');
    expect(routeTargetSpan.style.whiteSpace).toBe('nowrap');
    const progressHeaders=view.getAllByRole('columnheader',{name:'Tiến độ'});
    expect(progressHeaders.length).toBe(2);
    expect(progressHeaders[1].style.paddingRight).toBe('10px');
    fireEvent.click(taskBtn);
    const locInput=await view.findByLabelText('Location barcode SKU-1001');
    expect(locInput.closest('label')?.style.flexDirection).toBe('column');
    expect(locInput.closest('.ui-subcard')).toBeTruthy();
    expect(view.getByRole('button',{name:'Tối ưu Lộ trình Đi bộ Nhặt hàng Chữ S (Dãy → Kệ → Tầng → Ô)'})).toBeTruthy();
    expect(view.queryByText(/WCS\/WES|RFID\/IoT|Voice\/Pick-to-Light/)).toBeNull();
  });

  it('loads 8 Put-to-Wall sortation slots (SLOT-01..SLOT-08) and validates barcode scan into slot',async()=>{
    grant('picking.read','picking.execute');
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(url==='/api/picking-tasks') return {data:[summary]} as never;
      if(url==='/api/v1/picking-tasks/88/put-to-wall-slots'){
        return {
          data:{
            taskId:88,
            taskCode:'PICK-2026-0088',
            wallCode:'PTW-WALL-01',
            totalSlots:8,
            activeSlotsCount:1,
            slots:[
              {slotCode:'SLOT-01',slotIndex:1,status:'SORTING',orderCode:'EX-2026-5108',skuCode:'SKU-1001',productName:'Cà phê',requiredQty:10,pickedQty:2,remainingQty:8},
              {slotCode:'SLOT-02',slotIndex:2,status:'EMPTY',orderCode:null,skuCode:null,productName:null,requiredQty:0,pickedQty:0,remainingQty:0},
            ]
          }
        } as never;
      }
      return {data:detail} as never;
    });

    vi.mocked(apiClient.post).mockImplementation(async(url:string)=>{
      if(url==='/api/v1/picking-tasks/88/put-to-wall-sort'){
        return {
          data:{
            taskId:88,
            matched:true,
            sortedSlotCode:'SLOT-01',
            skuCode:'SKU-1001',
            sortedQuantity:1,
            slotSortedTotal:3,
            slotRequiredQty:10,
            slotRemainingQty:7,
            slotStatus:'SORTING_IN_PROGRESS',
            vietnameseMessage:'Đã chia đúng 1 sản phẩm SKU-1001 vào ô SLOT-01 (3/10).',
          }
        } as never;
      }
      return {data:{}} as never;
    });

    const view=render(<PickingTasks/>);
    await view.findByText('PICK-2026-0088');

    expect(await view.findByTestId('put-to-wall-grid')).toBeTruthy();
    expect(view.getByTestId('put-to-wall-SLOT-01')).toBeTruthy();

    fireEvent.change(view.getByLabelText('Quét mã SKU chia vào ô Put-to-Wall'),{target:{value:'SKU-1001'}});
    fireEvent.change(view.getByLabelText('Chọn ô chia hàng Put-to-Wall'),{target:{value:'SLOT-01'}});
    fireEvent.click(view.getByTestId('btn-confirm-put-to-wall-sort'));

    const notice=await view.findByTestId('put-to-wall-sort-notice');
    expect(notice.textContent).toContain('SLOT-01');
    expect(notice.textContent).toContain('SKU-1001');
    expect(notice.textContent).toContain('Tường Chia Chọn Đa Đơn');
  });
});
