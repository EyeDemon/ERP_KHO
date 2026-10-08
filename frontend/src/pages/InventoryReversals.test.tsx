// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryReversals from './InventoryReversals';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const permissionState=vi.hoisted(()=>({granted:new Set<string>()}));
vi.mock('../services/authorization',()=>({usePermission:(code:string)=>permissionState.granted.has(code)}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':'key:'+key}),
  completeIdempotentAction,
}));

const move={id:41,productId:10,productCode:'SKU-10',productName:'Sản phẩm 10',warehouseId:1,warehouseName:'Kho HCM',
  transactionType:'Move',inventoryStatus:'Available',lotNumber:'LOT-A',quantity:4,transactionDate:'2026-10-07T10:00:00Z',isReversed:false};
const reasons=[
  {code:'LOCATION_ERROR',name:'Sai vị trí lưu kho',transactionType:'Move'},
  {code:'STATUS_ERROR',name:'Sai trạng thái tồn kho',transactionType:'StatusChange'},
  {code:'OPERATION_CORRECTION',name:'Hiệu chỉnh nghiệp vụ sau kiểm tra',transactionType:null},
  {code:'DATA_ENTRY_ERROR',name:'Sai sót nhập liệu',transactionType:null},
];
const statusChange={...move,id:42,transactionType:'StatusChange',fromInventoryStatus:'Available',
  toInventoryStatus:'QcHold',inventoryStatus:'QcHold',quantity:2,transactionDate:'2026-10-07T11:00:00Z'};

const page=(items:typeof move[],pageIndex=1,totalRecords=items.length)=>({
  items,totalRecords,pageIndex,pageSize:20,totalPages:Math.ceil(totalRecords/20)
});
const reads=()=>vi.mocked(apiClient.get).mockImplementation(async url=>{
  if(String(url).includes('reversal-reasons'))return {data:reasons} as never;
  if(String(url).includes('reversal-warehouses'))
    return {data:[{id:1,code:'HCM',name:'Kho Hồ Chí Minh'},{id:2,code:'HN',name:'Kho Hà Nội'}]} as never;
  return {data:page([statusChange,move])} as never;
});

describe('InventoryReversals — backend-authoritative list',()=>{
  beforeEach(()=>{
    vi.resetAllMocks();completeIdempotentAction.mockReset();permissionState.granted.clear();
    permissionState.granted.add('inventory_ledger.read');reads();
  });
  afterEach(cleanup);

  it('reads one server-filtered page and hides mutation without permission',async()=>{
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    expect(await view.findByText('Đổi trạng thái')).toBeTruthy();
    expect(view.getByText('Di chuyển vị trí')).toBeTruthy();
    expect(view.queryByText('Xác nhận đảo giao dịch')).toBeNull();
    expect(apiClient.get).toHaveBeenCalledWith('/api/inventory/reversal-candidates?page=1&pageSize=20');
  });

  it('requires a valid reason code and offers only codes for the selected transaction type',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText('Di chuyển vị trí');
    fireEvent.click(view.getAllByText('Đảo giao dịch')[0]);
    const selector=view.getByLabelText('Mã lý do đảo giao dịch') as HTMLSelectElement;
    expect(selector.value).toBe('');
    expect(view.getByRole('option',{name:/Sai trạng thái tồn kho/})).toBeTruthy();
    expect(view.queryByRole('option',{name:/Sai vị trí lưu kho/})).toBeNull();
    fireEvent.change(view.getByLabelText('Lý do đảo giao dịch tồn kho'),{
      target:{value:'Đã kiểm tra'}
    });
    expect((view.getByText('Xác nhận đảo giao dịch') as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(selector,{target:{value:'STATUS_ERROR'}});
    expect((view.getByText('Xác nhận đảo giao dịch') as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(view.getByText('Hủy'));
    fireEvent.click(view.getAllByText('Đảo giao dịch')[1]);
    expect(view.getByRole('option',{name:/Sai vị trí lưu kho/})).toBeTruthy();
    expect(view.queryByRole('option',{name:/Sai trạng thái tồn kho/})).toBeNull();
  });

  it('fails closed when the reason catalog cannot be loaded',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(String(url).includes('reversal-reasons'))throw new Error('offline');
      if(String(url).includes('reversal-warehouses'))return {data:[]} as never;
      return {data:page([move])} as never;
    });
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText('Di chuyển vị trí');
    expect(await view.findByText(/Không thể tải danh mục mã lý do đảo/)).toBeTruthy();
    fireEvent.click(view.getByText('Đảo giao dịch'));
    fireEvent.change(view.getByLabelText('Lý do đảo giao dịch tồn kho'),{
      target:{value:'Có lý do nhưng không có mã'}
    });
    expect((view.getByText('Xác nhận đảo giao dịch') as HTMLButtonElement).disabled).toBe(true);
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('posts one reversal with a stable idempotency key',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    vi.mocked(apiClient.post).mockResolvedValue({data:{reversalTransactionId:99}} as never);
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText('Đổi trạng thái');
    const buttons=view.getAllByText('Đảo giao dịch');
    fireEvent.click(buttons[0]);
    fireEvent.change(view.getByLabelText('Mã lý do đảo giao dịch'),{target:{value:'OPERATION_CORRECTION'}});
    fireEvent.change(view.getByLabelText('Lý do đảo giao dịch tồn kho'),{target:{value:'Đã kiểm tra theo chứng từ'}});
    const submit=view.getByText('Xác nhận đảo giao dịch');
    fireEvent.click(submit);fireEvent.click(submit);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/inventory/reversals',{
      originalTransactionId:42,reasonCode:'OPERATION_CORRECTION',reason:'Đã kiểm tra theo chứng từ'
    },{headers:{'Idempotency-Key':'key:inventory-reversal-42:OPERATION_CORRECTION:Đã kiểm tra theo chứng từ'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('inventory-reversal-42:OPERATION_CORRECTION:Đã kiểm tra theo chứng từ');
  });

  it('disables old reversed transaction even without the marker in this page',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(String(url).includes('reversal-reasons'))return {data:reasons} as never;
      if(String(url).includes('reversal-warehouses'))return {data:[]} as never;
      return {data:page([{...move,isReversed:true}])} as never;
    });
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    expect(await view.findByText('Đã đảo')).toBeTruthy();
    expect((view.getByText('Đảo giao dịch') as HTMLButtonElement).disabled).toBe(true);
    expect(vi.mocked(apiClient.get).mock.calls.filter(([url])=>String(url).includes('reversal-candidates'))).toHaveLength(1);
  });

  it('reloads authoritative reversal state when another operator reversed first',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    let reversed=false;
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(String(url).includes('reversal-reasons'))return {data:reasons} as never;
      if(String(url).includes('reversal-warehouses'))return {data:[]} as never;
      return {data:page([{...move,isReversed:reversed}])} as never;
    });
    vi.mocked(apiClient.post).mockImplementation(async()=>{
      reversed=true;
      throw {response:{status:409,data:{code:'INV_ALREADY_REVERSED',message:'Giao dịch đã được đảo trước đó.'}}};
    });

    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText('Chưa đảo');
    fireEvent.click(view.getByText('Đảo giao dịch'));
    fireEvent.change(view.getByLabelText('Mã lý do đảo giao dịch'),{target:{value:'OPERATION_CORRECTION'}});
    fireEvent.change(view.getByLabelText('Lý do đảo giao dịch tồn kho'),{
      target:{value:'Xác minh trường hợp thao tác đồng thời'}
    });
    fireEvent.click(view.getByText('Xác nhận đảo giao dịch'));
    expect(await view.findByText('Đã đảo')).toBeTruthy();
    expect(await view.findByRole('alert')).toHaveProperty('textContent','Giao dịch đã được đảo trước đó.');
    expect((view.getByText('Đảo giao dịch') as HTMLButtonElement).disabled).toBe(true);
    expect(vi.mocked(apiClient.get).mock.calls.filter(([url])=>String(url).includes('reversal-candidates'))).toHaveLength(2);
  });

  it('keeps the correction form open when downstream stock prevents reversal',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    vi.mocked(apiClient.post).mockRejectedValue({
      response:{status:409,data:{
        code:'INV_REVERSAL_INSUFFICIENT_STOCK',
        message:'Tồn khả dụng tại vị trí đích không đủ để đảo giao dịch.'
      }}
    });

    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText('Di chuyển vị trí');
    fireEvent.click(view.getAllByText('Đảo giao dịch')[1]);
    fireEvent.change(view.getByLabelText('Mã lý do đảo giao dịch'),{target:{value:'OPERATION_CORRECTION'}});
    fireEvent.change(view.getByLabelText('Lý do đảo giao dịch tồn kho'),{
      target:{value:'Kiểm tra hậu quả dịch chuyển hàng'}
    });
    fireEvent.click(view.getByText('Xác nhận đảo giao dịch'));

    expect(await view.findByRole('alert')).toHaveProperty('textContent',
      'Tồn khả dụng tại vị trí đích không đủ để đảo giao dịch.');
    expect(view.getByText('Xác nhận đảo giao dịch')).toBeTruthy();
    expect(view.queryByText('Đã ghi nhận giao dịch đảo thành công.')).toBeNull();
    expect(apiClient.post).toHaveBeenCalledTimes(1);
  });

  it('opens the immutable reversal chain only when traceability permission is granted',async()=>{
    permissionState.granted.add('inventory_traceability.read');
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(String(url).includes('reversal-reasons'))return {data:reasons} as never;
      if(String(url).includes('reversal-warehouses'))return {data:[]} as never;
      return {data:page([{...move,isReversed:true}])} as never;
    });
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    const link=await view.findByText('Truy vết chuỗi đảo');
    expect((link as HTMLAnchorElement).getAttribute('href'))
      .toBe('/inventory-traceability?referenceType=InventoryReversal&referenceId=41');
    expect(link.closest('a')).toBeTruthy();
  });

  it('navigates within the SPA to the immutable ledger without a full page reload',async()=>{
    permissionState.granted.add('inventory_traceability.read');
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(String(url).includes('reversal-reasons'))return {data:reasons} as never;
      if(String(url).includes('reversal-warehouses'))return {data:[]} as never;
      return {data:page([{...move,isReversed:true}])} as never;
    });
    const view=render(
      <MemoryRouter initialEntries={['/inventory-reversals']}>
        <Routes>
          <Route path="/inventory-reversals" element={<InventoryReversals/>}/>
          <Route path="/inventory-traceability" element={<p>Đã mở truy vết sổ cái</p>}/>
        </Routes>
      </MemoryRouter>
    );
    const link=await view.findByText('Truy vết chuỗi đảo');
    fireEvent.click(link);
    expect(await view.findByText('Đã mở truy vết sổ cái')).toBeTruthy();
  });

  it('filters by an authorized warehouse using the real query parameter',async()=>{
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText('Di chuyển vị trí');
    const filter=await view.findByLabelText('Lọc theo kho');
    fireEvent.change(filter,{target:{value:'2'}});
    await waitFor(()=>expect(apiClient.get)
      .toHaveBeenCalledWith('/api/inventory/reversal-candidates?page=1&pageSize=20&warehouseId=2'));
  });

  it('filters persisted reversal state in SQL before paging',async()=>{
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText('Di chuyển vị trí');
    const select=view.getByLabelText('Lọc theo trạng thái đảo');
    fireEvent.change(select,{target:{value:'pending'}});
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/reversal-candidates?page=1&pageSize=20&isReversed=false'
    ));
    fireEvent.change(select,{target:{value:'reversed'}});
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/reversal-candidates?page=1&pageSize=20&isReversed=true'
    ));
  });

  it('changes the idempotency action when reason changes after a failed request',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    vi.mocked(apiClient.post).mockRejectedValueOnce({
      response:{status:409,data:{code:'INV_REVERSAL_INSUFFICIENT_STOCK',
        message:'Không đủ tồn để đảo giao dịch.'}}
    }).mockResolvedValueOnce({data:{reversalTransactionId:101}} as never);
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText('Di chuyển vị trí');
    fireEvent.click(view.getAllByText('Đảo giao dịch')[1]);
    fireEvent.change(view.getByLabelText('Mã lý do đảo giao dịch'),{target:{value:'OPERATION_CORRECTION'}});
    const input=view.getByLabelText('Lý do đảo giao dịch tồn kho');
    fireEvent.change(input,{target:{value:'Lý do thứ nhất'}});
    fireEvent.click(view.getByText('Xác nhận đảo giao dịch'));
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    await view.findByRole('alert');
    fireEvent.change(input,{target:{value:'Lý do đã sửa'}});
    fireEvent.click(view.getByText('Xác nhận đảo giao dịch'));
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(2));
    expect(vi.mocked(apiClient.post).mock.calls[0][2]?.headers).toEqual({
      'Idempotency-Key':'key:inventory-reversal-41:OPERATION_CORRECTION:Lý do thứ nhất'
    });
    expect(vi.mocked(apiClient.post).mock.calls[1][2]?.headers).toEqual({
      'Idempotency-Key':'key:inventory-reversal-41:OPERATION_CORRECTION:Lý do đã sửa'
    });
  });

  it('looks up an old transaction by exact ID without revealing other records',async()=>{
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText('Di chuyển vị trí');
    fireEvent.change(view.getByLabelText('Tìm theo ID giao dịch'),{target:{value:'41'}});
    fireEvent.click(view.getByText('Tìm giao dịch'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/reversal-candidates?page=1&pageSize=20&transactionId=41'
    ));
  });

  it('translates upper-case API inventory status into Vietnamese',async()=>{
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(String(url).includes('reversal-reasons'))return {data:reasons} as never;
      if(String(url).includes('reversal-warehouses'))return {data:[]} as never;
      return {data:page([{...move,inventoryStatus:'RECALL_BLOCKED'}])} as never;
    });
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    expect(await view.findByText('Khóa thu hồi')).toBeTruthy();
  });

  it('returns to the first filtered server page after reversing the final pending item',async()=>{
    permissionState.granted.add('inventory_reversal.create');
    let reversed=false;
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      const path=String(url);
      if(path.includes('reversal-warehouses'))return {data:[]} as never;
      const second=path.includes('page=2');
      return {data:page(second
        ? (reversed?[]:[{...move,id:21}])
        : [move],second?2:1,reversed?20:21)} as never;
    });
    vi.mocked(apiClient.post).mockImplementation(async()=>{
      reversed=true;
      return {data:{reversalTransactionId:99}} as never;
    });
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText(/Trang 1 \/ 2/);
    fireEvent.click(view.getByText('Trang sau'));
    await view.findByText(/Trang 2 \/ 2/);
    fireEvent.click(view.getByText('Đảo giao dịch'));
    fireEvent.change(view.getByLabelText('Mã lý do đảo giao dịch'),{target:{value:'OPERATION_CORRECTION'}});
    fireEvent.change(view.getByLabelText('Lý do đảo giao dịch tồn kho'),{
      target:{value:'Đã kiểm tra hàng hóa cần đảo'}
    });
    fireEvent.click(view.getByText('Xác nhận đảo giao dịch'));
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(await view.findByText(/Trang 1 \/ 1/)).toBeTruthy();
    expect(view.getByText('Đã ghi nhận giao dịch đảo thành công.')).toBeTruthy();
    expect((view.getByText('Trang sau') as HTMLButtonElement).disabled).toBe(true);
  });

  it('requests later pages from server without truncating older transactions',async()=>{
    vi.mocked(apiClient.get).mockImplementation(async (url)=>{
      if(String(url).includes('reversal-reasons'))return {data:reasons} as never;
      if(String(url).includes('reversal-warehouses'))return {data:[]} as never;
      const second=String(url).includes('page=2');
      return {data:page(second?[{...move,id:21}]:[move],second?2:1,130)} as never;
    });
    const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
    await view.findByText(/Trang 1 \/ 7/);
    fireEvent.click(view.getByText('Trang sau'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith('/api/inventory/reversal-candidates?page=2&pageSize=20'));
    expect(await view.findByText(/Trang 2 \/ 7/)).toBeTruthy();
  });
});
