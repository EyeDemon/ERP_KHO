// @vitest-environment jsdom
import { render, fireEvent, waitFor, cleanup } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Approvals from './Approvals';
import apiClient from '../services/apiClient';
import userEvent from '@testing-library/user-event';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const item={documentType:'StockTransfer',documentId:7,documentCode:'TRF-7',pendingState:'Draft',creatorName:'Maker',requestedAtUtc:'2026-09-05T01:00:00Z',warehouseName:'WH01',destinationWarehouseName:'WH02',totalQuantity:5,canApprove:false,canReject:true};
const page=(items= [item])=>({items,totalRecords:items.length,pageIndex:1,pageSize:20,totalPages:items.length?2:0});

describe('Approvals page',()=>{
  afterEach(cleanup);
  beforeEach(()=>{vi.resetAllMocks();vi.mocked(apiClient.get).mockResolvedValue({data:page()});});
  it('renders loading, queue data, capabilities, filters, and pagination',async()=>{
    let resolve!: (value:unknown)=>void;vi.mocked(apiClient.get).mockReturnValueOnce(new Promise(r=>{resolve=r}) as never);
    const view=render(<Approvals/>);expect(view.getByText('Đang tải...')).toBeTruthy();resolve({data:page()});
    await view.findByText('TRF-7');expect(view.queryByTitle('Duyệt')).toBeNull();expect(view.getByTitle('Từ chối')).toBeTruthy();
    fireEvent.change(view.getByLabelText('Tìm mã chứng từ'),{target:{value:'TRF'}});
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalled());fireEvent.click(view.getByText('Sau'));
  });
  it('renders empty and correlation-aware error states',async()=>{
    vi.mocked(apiClient.get).mockResolvedValueOnce({data:page([])});const empty=render(<Approvals/>);await empty.findByText('Không có chứng từ chờ duyệt.');empty.unmount();
    vi.mocked(apiClient.get).mockRejectedValueOnce({response:{status:409,data:{correlationId:'corr-7'}}});const failed=render(<Approvals/>);await failed.findByText(/corr-7/);
  });
  it('loads detail/history and validates and submits reject without duplicate click',async()=>{
    vi.mocked(apiClient.get).mockResolvedValueOnce({data:page()}).mockResolvedValueOnce({data:{summary:item,note:'note',lines:[],history:[{id:1,timestampUtc:'2026-09-05T01:00:00+00:00',actorName:'Checker',displayAction:'Bị từ chối',oldState:'Draft',newState:'Cancelled'}]}});
    vi.mocked(apiClient.post).mockResolvedValue({data:{}});const view=render(<Approvals/>);await view.findByText('TRF-7');
    fireEvent.click(view.getByTitle('Xem chi tiết'));await view.findByText('Bị từ chối');fireEvent.click(view.getByText('Đóng'));
    fireEvent.click(view.getByTitle('Từ chối'));const textarea=view.getByLabelText('Lý do từ chối');expect(document.activeElement).toBe(textarea);
    fireEvent.change(textarea,{target:{value:'ab'}});expect((view.getByText('Xác nhận từ chối') as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(textarea,{target:{value:'Sai số lượng'}});fireEvent.click(view.getByText('Xác nhận từ chối'));fireEvent.click(view.getByText('Xác nhận từ chối'));
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
  });
  it('submits exact server filters and converts local date boundaries to UTC',async()=>{
    const view=render(<Approvals/>);await view.findByText('TRF-7');
    fireEvent.change(view.getByLabelText('Loại chứng từ'),{target:{value:'StockTransfer'}});
    fireEvent.change(view.getByLabelText('Mã kho'),{target:{value:'2'}});
    fireEvent.change(view.getByLabelText('Mã người tạo'),{target:{value:'3'}});
    fireEvent.change(view.getByLabelText('Tìm mã chứng từ'),{target:{value:'TRF'}});
    fireEvent.change(view.getByLabelText('Từ ngày'),{target:{value:'2026-09-05T00:00'}});
    fireEvent.change(view.getByLabelText('Đến ngày'),{target:{value:'2026-09-05T23:59'}});
    await waitFor(()=>expect(apiClient.get).toHaveBeenLastCalledWith('/api/approvals/queue',{params:expect.objectContaining({documentType:'StockTransfer',warehouseId:'2',creatorId:'3',keyword:'TRF',fromUtc:new Date('2026-09-05T00:00').toISOString(),toUtc:new Date('2026-09-05T23:59').toISOString(),pageIndex:1})}));
    await waitFor(()=>expect((view.getByText('Sau') as HTMLButtonElement).disabled).toBe(false));fireEvent.click(view.getByText('Sau'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenLastCalledWith('/api/approvals/queue',{params:expect.objectContaining({pageIndex:2})}));
  });
  it('hides both actions when server denies capabilities and renders the instant in local time',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:page([{...item,canReject:false}])});
    const view=render(<Approvals/>);await view.findByText('TRF-7');
    expect(view.queryByTitle('Duyệt')).toBeNull();expect(view.queryByTitle('Từ chối')).toBeNull();
    expect(view.getByText(new Date(item.requestedAtUtc).toLocaleString())).toBeTruthy();
  });
  it('requires approve confirmation, blocks duplicate submits, and closes on success',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:page([{...item,canApprove:true}])});
    let finish!:()=>void;vi.mocked(apiClient.post).mockReturnValue(new Promise(resolve=>{finish=()=>resolve({data:{}})}) as never);
    const view=render(<Approvals/>);await view.findByText('TRF-7');fireEvent.click(view.getByTitle('Duyệt'));
    expect(apiClient.post).not.toHaveBeenCalled();expect(document.activeElement).toBe(view.getByText('Đóng'));
    fireEvent.click(view.getByText('Xác nhận duyệt'));fireEvent.click(view.getByText('Xác nhận duyệt'));fireEvent.keyDown(document,{key:'Escape'});
    expect(view.getByRole('dialog')).toBeTruthy();expect(apiClient.post).toHaveBeenCalledTimes(1);
    finish();await waitFor(()=>expect(view.queryByRole('dialog')).toBeNull());
    expect(apiClient.post).toHaveBeenCalledWith('/api/stock-transfers/7/approve',null,{headers:expect.objectContaining({'Idempotency-Key':expect.any(String)})});
  });
  it('refreshes on conflict, preserves correlation and dialog, and retains key on retry',async()=>{
    vi.mocked(apiClient.post).mockRejectedValue({response:{status:409,data:{correlationId:'conflict-id'}}});
    const view=render(<Approvals/>);await view.findByText('TRF-7');fireEvent.click(view.getByTitle('Từ chối'));
    fireEvent.change(view.getByLabelText('Lý do từ chối'),{target:{value:'Sai số lượng'}});fireEvent.click(view.getByText('Xác nhận từ chối'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledTimes(2));
    expect(view.getByRole('dialog').textContent).toContain('conflict-id');
    await waitFor(()=>expect((view.getByText('Xác nhận từ chối') as HTMLButtonElement).disabled).toBe(false));
    fireEvent.click(view.getByText('Xác nhận từ chối'));await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(2));
    expect(vi.mocked(apiClient.post).mock.calls[0][2]).toEqual(vi.mocked(apiClient.post).mock.calls[1][2]);
  });
  it('shows detail loading and safely renders distinct history labels and reason text',async()=>{
    let finish!:(value:unknown)=>void;
    vi.mocked(apiClient.get).mockResolvedValueOnce({data:page()}).mockReturnValueOnce(new Promise(r=>{finish=r}) as never);
    const view=render(<Approvals/>);await view.findByText('TRF-7');fireEvent.click(view.getByTitle('Xem chi tiết'));
    expect(view.getByRole('status').textContent).toBe('Đang tải chi tiết...');
    finish({data:{summary:item,lines:[],history:['Bị từ chối','Đã hủy','StockTransfer.Approve.Rejected'].map((displayAction,id)=>({id,displayAction,actorName:'Checker',timestampUtc:'2026-09-05T01:00:00+00:00',reason:id===0?'<img src=x onerror=alert(1)>':undefined}))}});
    await view.findByText('Bị từ chối');expect(view.getByText('Đã hủy')).toBeTruthy();expect(view.getByText('StockTransfer.Approve.Rejected')).toBeTruthy();
    expect(view.getByText('<img src=x onerror=alert(1)>')).toBeTruthy();expect(view.getByRole('dialog').querySelector('img')).toBeNull();
  });
  it('supports reject keyboard focus cycle and restores focus on Escape',async()=>{
    const user=userEvent.setup();const view=render(<Approvals/>);await view.findByText('TRF-7');const trigger=view.getByTitle('Từ chối');await user.click(trigger);
    const input=view.getByLabelText('Lý do từ chối');expect(document.activeElement).toBe(input);
    await user.type(input,'Sai số lượng');await user.tab({shift:true});expect(document.activeElement).toBe(view.getByText('Xác nhận từ chối'));
    await user.tab();expect(document.activeElement).toBe(input);await user.keyboard('{Escape}');expect(view.queryByRole('dialog')).toBeNull();expect(document.activeElement).toBe(trigger);
  });
  it('keeps focus inside the reject dialog after API failure and permits a safe retry',async()=>{
    const user=userEvent.setup();let fail!:(reason:unknown)=>void;
    vi.mocked(apiClient.post).mockReturnValueOnce(new Promise((_,reject)=>{fail=reject}) as never).mockResolvedValueOnce({data:{}});
    const view=render(<Approvals/>);await view.findByText('TRF-7');const trigger=view.getByTitle('Từ chối');await user.click(trigger);
    const input=view.getByLabelText('Lý do từ chối');await user.type(input,'Sai số lượng');const submit=view.getByText('Xác nhận từ chối');
    await user.click(submit);expect((submit as HTMLButtonElement).disabled).toBe(true);fail({response:{status:409,data:{correlationId:'retry-correlation'}}});
    await waitFor(()=>expect(view.getByRole('dialog').textContent).toContain('retry-correlation'));const dialog=view.getByRole('dialog');
    expect(dialog.contains(document.activeElement)).toBe(true);expect(document.activeElement).not.toBe(document.body);expect(document.activeElement).not.toBe(trigger);
    await user.click(input);await user.type(input,' bổ sung');await user.click(view.getByText('Xác nhận từ chối'));
    await waitFor(()=>expect(view.queryByRole('dialog')).toBeNull());expect(apiClient.post).toHaveBeenCalledTimes(2);
    expect(vi.mocked(apiClient.post).mock.calls[0][2]).toEqual(vi.mocked(apiClient.post).mock.calls[1][2]);
  });
});
