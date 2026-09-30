// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PutawayTasks from './PutawayTasks';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
vi.mock('../services/authorization',()=>({usePermission:()=>true,currentUserId:()=>7}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({idempotencyHeaders:()=>({'Idempotency-Key':'masked-test-key'}),completeIdempotentAction}));
const summary={id:1,receiptCode:'PN-1',warehouseName:'Kho A',status:'Open',assignedUserId:null,requiredBaseQuantity:10,movedBaseQuantity:0,rowVersion:'AQ==',items:[]};
const detail={...summary,status:'Assigned',items:[{id:2,productCode:'SP1',productName:'Sản phẩm',inventoryStatus:'Available',sourceLocationCode:'RECEIVING',operationUnitCode:'EA',baseUnitCode:'EA',requiredOperationQuantity:10,requiredBaseQuantity:10,movedBaseQuantity:0,remainingBaseQuantity:10}]};

describe('Cất hàng',()=>{
 afterEach(cleanup);
 beforeEach(()=>{vi.resetAllMocks();vi.mocked(apiClient.get).mockImplementation(async url=>({data:url==='/api/putaway-tasks'?[summary]:url.includes('destinations')?[{id:3,code:'A01',name:'Kệ A',locationType:'Storage',isPickable:true}]:detail}) as never)});
 it('maps technical states to Vietnamese and never renders raw enums',async()=>{const v=render(<PutawayTasks/>);await v.findByText('PN-1');expect(v.getByText('Chưa phân công')).toBeTruthy();expect(v.queryByText('Open')).toBeNull();fireEvent.click(v.getByText('PN-1'));await v.findByText(/Có thể sử dụng/);expect(v.queryByText('Available')).toBeNull()});
 it('synchronously blocks duplicate movement submits and shows Vietnamese conflict',async()=>{let reject!:(e:unknown)=>void;vi.mocked(apiClient.post).mockReturnValue(new Promise((_,r)=>{reject=r}) as never);const v=render(<PutawayTasks/>);await v.findByText('PN-1');fireEvent.click(v.getByText('PN-1'));await v.findByText('Chọn vị trí đích');fireEvent.change(v.getByLabelText('Vị trí đích SP1'),{target:{value:'3'}});fireEvent.change(v.getByLabelText('Số lượng cất SP1'),{target:{value:'4'}});const button=v.getByText('Xác nhận số lượng');fireEvent.click(button);fireEvent.click(button);expect(apiClient.post).toHaveBeenCalledTimes(1);reject({response:{status:409}});await waitFor(()=>expect(v.getByRole('alert').textContent).toContain('Dữ liệu đã thay đổi'));expect(v.queryByLabelText('Chi tiết nhiệm vụ cất hàng')).toBeNull()});
 it('releases the idempotency key after a successful action',async()=>{vi.mocked(apiClient.post).mockResolvedValue({data:detail} as never);const v=render(<PutawayTasks/>);await v.findByText('PN-1');fireEvent.click(v.getByText('PN-1'));await v.findByText('Chọn vị trí đích');fireEvent.change(v.getByLabelText('Vị trí đích SP1'),{target:{value:'3'}});fireEvent.change(v.getByLabelText('Số lượng cất SP1'),{target:{value:'4'}});fireEvent.click(v.getByText('Xác nhận số lượng'));await waitFor(()=>expect(completeIdempotentAction).toHaveBeenCalledWith('move-1-2'))});
});
