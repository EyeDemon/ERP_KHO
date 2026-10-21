// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PutawayTasks from './PutawayTasks';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const permissionState=vi.hoisted(()=>({granted:new Set<string>()}));
vi.mock('../services/authorization',()=>({usePermission:(code:string)=>permissionState.granted.has(code),currentUserId:()=>7}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({idempotencyHeaders:()=>({'Idempotency-Key':'masked-test-key'}),completeIdempotentAction}));
const summary={id:1,receiptCode:'PN-1',warehouseName:'Kho A',status:'Open',assignedUserId:null,requiredBaseQuantity:10,movedBaseQuantity:0,rowVersion:'AQ==',items:[]};
const detail={...summary,status:'Assigned',items:[{id:2,productCode:'SP1',productName:'Sản phẩm',inventoryStatus:'Available',sourceLocationCode:'RECEIVING',operationUnitCode:'EA',baseUnitCode:'EA',requiredOperationQuantity:10,requiredBaseQuantity:10,movedBaseQuantity:0,remainingBaseQuantity:10}]};
const destinations=[{id:3,code:'A01',name:'Kệ A',locationType:'Storage',isPickable:true}];
const mockReads=(task=detail)=>vi.mocked(apiClient.get).mockImplementation(async url=>({data:url==='/api/putaway-tasks'?[summary]:url.includes('destinations')?destinations:task}) as never);
const grant=(...permissions:string[])=>{permissionState.granted.clear();permissions.forEach(permission=>permissionState.granted.add(permission));};

describe('Cất hàng',()=>{
 afterEach(cleanup);
 beforeEach(()=>{vi.resetAllMocks();grant('putaway.assign','putaway.execute','putaway.cancel');mockReads()});
 it('maps technical states and presentation copy to Vietnamese',async()=>{
   const v=render(<PutawayTasks/>);
   await v.findByText('PN-1');
   expect(v.getByText('Nhập kho')).toBeTruthy();
   expect(v.getByText('Theo dõi nhiệm vụ cất hàng từ vị trí nhận hàng tới vị trí đích, gồm trạng thái, tiến độ và xử lý ngoại lệ.')).toBeTruthy();
   expect(v.getByText('Chưa phân công')).toBeTruthy();
   expect(v.queryByText('Open')).toBeNull();
   expect(v.queryByText('Inbound')).toBeNull();
   fireEvent.click(v.getByText('PN-1'));
   await v.findByText(/Có thể sử dụng/);
   expect(v.queryByText('Available')).toBeNull();
 });
 it('shows assignment only with putaway.assign',async()=>{
   grant('putaway.assign');
   mockReads({...detail,status:'Open'});
   const v=render(<PutawayTasks/>);
   await v.findByText('PN-1');
   fireEvent.click(v.getByText('PN-1'));
   expect(await v.findByText('Nhận nhiệm vụ')).toBeTruthy();
   expect(v.queryByText('Hủy nhiệm vụ')).toBeNull();
   expect(v.queryByText('Bắt đầu cất hàng')).toBeNull();
 });
 it('shows execution controls only with putaway.execute',async()=>{
   grant('putaway.execute');
   mockReads(detail);
   const v=render(<PutawayTasks/>);
   await v.findByText('PN-1');
   fireEvent.click(v.getByText('PN-1'));
   expect(await v.findByText('Bắt đầu cất hàng')).toBeTruthy();
   expect(v.getByLabelText('Vị trí đích SP1')).toBeTruthy();
   expect(v.queryByText('Hủy nhiệm vụ')).toBeNull();
   expect(v.queryByText('Nhận nhiệm vụ')).toBeNull();
 });
 it('shows cancellation only with putaway.cancel',async()=>{
   grant('putaway.cancel');
   mockReads({...detail,status:'Open'});
   const v=render(<PutawayTasks/>);
   await v.findByText('PN-1');
   fireEvent.click(v.getByText('PN-1'));
   expect(await v.findByText('Hủy nhiệm vụ')).toBeTruthy();
   expect(v.queryByText('Nhận nhiệm vụ')).toBeNull();
   expect(v.queryByText('Bắt đầu cất hàng')).toBeNull();
 });
 it('synchronously blocks duplicate movement submits and shows Vietnamese conflict',async()=>{
   let reject!:(e:unknown)=>void;
   vi.mocked(apiClient.post).mockReturnValue(new Promise((_,r)=>{reject=r}) as never);
   const v=render(<PutawayTasks/>);
   await v.findByText('PN-1');
   fireEvent.click(v.getByText('PN-1'));
   await v.findByText('Chọn vị trí đích');
   fireEvent.change(v.getByLabelText('Vị trí đích SP1'),{target:{value:'3'}});
   fireEvent.change(v.getByLabelText('Số lượng cất SP1'),{target:{value:'4'}});
   const button=v.getByText('Xác nhận số lượng');
   fireEvent.click(button);fireEvent.click(button);
   expect(apiClient.post).toHaveBeenCalledTimes(1);
   reject({response:{status:409}});
   await waitFor(()=>expect(v.getByRole('alert').textContent).toContain('Dữ liệu đã thay đổi'));
   expect(v.queryByLabelText('Chi tiết nhiệm vụ cất hàng')).toBeNull();
 });
 it('releases the idempotency key after a successful action',async()=>{
   vi.mocked(apiClient.post).mockResolvedValue({data:detail} as never);
   const v=render(<PutawayTasks/>);
   await v.findByText('PN-1');
   fireEvent.click(v.getByText('PN-1'));
   await v.findByText('Chọn vị trí đích');
   fireEvent.change(v.getByLabelText('Vị trí đích SP1'),{target:{value:'3'}});
   fireEvent.change(v.getByLabelText('Số lượng cất SP1'),{target:{value:'4'}});
   fireEvent.click(v.getByText('Xác nhận số lượng'));
   await waitFor(()=>expect(completeIdempotentAction).toHaveBeenCalledWith('move-1-2'));
 });
 it('renders Lộ trình column in Danh sách nhiệm vụ as two nowrap spans with single arrow → without broken ->, nowrap task button, ui-subcard, and normalized QA_UNIT',async()=>{
   mockReads({...detail,items:[{...detail.items[0],operationUnitCode:'QA_UNIT',baseUnitCode:'QA_UNIT'}]});
   const v=render(<PutawayTasks/>);
   const btn=await v.findByText('PN-1');
   expect((btn as HTMLElement).style.whiteSpace).toBe('nowrap');
   const fromSpan=v.getByText('Bãi tiếp nhận');
   const toSpan=v.getByText('→ BIN-A01-01');
   expect(fromSpan.tagName).toBe('SPAN');
   expect(toSpan.tagName).toBe('SPAN');
   expect((fromSpan as HTMLElement).style.whiteSpace).toBe('nowrap');
   expect((toSpan as HTMLElement).style.whiteSpace).toBe('nowrap');
   expect(v.queryByText('RECEIVING')).toBeNull();
   expect(v.container.textContent).not.toContain('->');
   fireEvent.click(btn);
   const destSelect=await v.findByLabelText('Vị trí đích SP1');
   expect(destSelect.closest('.ui-subcard')).toBeTruthy();
   expect(v.queryByText(/QA_UNIT/)).toBeNull();
   expect(v.getByText(/10 THÙNG/)).toBeTruthy();
 });
 it('renders CancelPendingPutback and PutbackTask labels in Vietnamese and keeps detail form open on 409 INSUFFICIENT_AVAILABLE_STOCK',async()=>{
   const pendingPutback={...detail,id:9,receiptCode:'PN-PUTBACK',status:'CancelPendingPutback',taskType:'PutbackTask'};
   const activePutback={...detail,id:1,receiptCode:'PN-1',status:'InProgress',taskType:'PutbackTask'};
   vi.mocked(apiClient.get).mockImplementation(async url=>({data:url==='/api/putaway-tasks'?[pendingPutback,activePutback]:url.includes('destinations')?destinations:activePutback}) as never);
   vi.mocked(apiClient.post).mockRejectedValueOnce({response:{status:409,data:{code:'INSUFFICIENT_AVAILABLE_STOCK'}}});
   const v=render(<PutawayTasks/>);
   await v.findByText('PN-1');
   expect(v.getAllByText('Chờ cất trả hàng hủy về kệ').length).toBeGreaterThanOrEqual(1);
   expect(v.getAllByText('Cất trả hàng hủy về kệ').length).toBeGreaterThanOrEqual(1);
   fireEvent.click(v.getByText('PN-1'));
   const qtyInput=await v.findByLabelText('Số lượng cất SP1') as HTMLInputElement;
   fireEvent.change(v.getByLabelText('Vị trí đích SP1'),{target:{value:'3'}});
   fireEvent.change(qtyInput,{target:{value:'6'}});
   fireEvent.click(v.getByText('Xác nhận số lượng'));
   expect(await v.findByRole('alert')).toBeTruthy();
   expect(v.getByRole('alert').textContent).toBe('Tồn kho khả dụng tại vị trí chọn không đủ để thực hiện thao tác. Vui lòng điều chỉnh số lượng hoặc chọn vị trí khác.');
   expect((v.getByLabelText('Số lượng cất SP1') as HTMLInputElement).value).toBe('6');
   expect(v.getByText('Kệ đang hoạt động')).toBeTruthy();
   expect(v.getByText('Kệ không bị khóa')).toBeTruthy();
   expect(v.getAllByPlaceholderText('Nhập hoặc quét mã kệ, mã SP, số lô, số sê-ri, mã kiện...').length).toBeGreaterThanOrEqual(1);
   expect(v.queryByPlaceholderText(/Bin, SKU, Lot, Serial, LPN/)).toBeNull();
   expect(v.getByRole('button', { name: 'Chấm điểm Tối ưu Xếp dỡ & Khớp Vị trí Kệ' })).toBeTruthy();
   expect(v.getByTestId('putaway-ledger-pair-panel').textContent).toContain(
     'Chân nguồn -Q (Bãi tiếp nhận) • Chân đích +Q (Kệ lưu trữ BIN-A01-01)',
   );
 });
});

