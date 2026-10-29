// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import StockAllocations from './StockAllocations';
vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn(),put:vi.fn(),delete:vi.fn()}}));
vi.mock('../services/idempotency',()=>({idempotencyHeaders:(a:string)=>({'Idempotency-Key':`key:${a}`}),completeIdempotentAction:vi.fn()}));
const get=vi.mocked(apiClient.get),post=vi.mocked(apiClient.post);
const allocation={id:7711,allocationCode:'ALC-2026-7711',reservationId:9031,reservationCode:'RSV-2026-9031',sourceType:'ExportReceipt',sourceCode:'EX-2026-5108',warehouseId:1,warehouseName:'DC Hồ Chí Minh',productId:1,productCode:'SKU-1001',productName:'Cà phê Arabica 500g',locationId:101,locationCode:'A01-R02-L03-B04',locationName:'Bin B04',inventoryStatus:'Available',quantity:80,status:'Active',strategy:'LocationOrder',selectionReason:'Tự động theo thứ tự vị trí ổn định.',allocatedAt:'2026-10-05T08:47:00Z'};
const reservation={reservationId:9031,reservationCode:'RSV-2026-9031',sourceType:'ExportReceipt',sourceCode:'EX-2026-5108',warehouseId:1,warehouseName:'DC Hồ Chí Minh',productId:1,productCode:'SKU-1001',productName:'Cà phê Arabica 500g',reservedQuantity:120,allocatedQuantity:80,allocatableQuantity:40,expiresAt:'2026-10-06T08:45:00Z'};
const candidate={locationId:102,locationCode:'A01-R02-L03-B05',locationName:'Bin B05',reservedQuantity:40,allocatedQuantity:0,allocatableQuantity:40,rank:1,reason:'Vị trí pickable.'};
describe('StockAllocations production UI',()=>{
  beforeEach(()=>{
    vi.resetAllMocks();localStorage.clear();
    get.mockImplementation((url)=>{
      if(url==='/api/inventory/allocations/reservations')return Promise.resolve({data:[reservation]} as never);
      if(url==='/api/inventory/allocations/candidates')return Promise.resolve({data:[candidate]} as never);
      return Promise.resolve({data:{items:[allocation],totalRecords:1,pageIndex:1,pageSize:20}} as never);
    });
    post.mockResolvedValue({data:[]} as never);
  });
  afterEach(()=>{cleanup();vi.restoreAllMocks()});
  it('renders read-only evidence with only allocation.read',async()=>{
    localStorage.setItem('permissions',JSON.stringify(['allocation.read']));
    const view=render(<StockAllocations/>);
    expect(await view.findByText('ALC-2026-7711')).toBeTruthy();
    expect(view.getByRole('table',{name:'Danh sách Allocation'})).toBeTruthy();
    expect(view.queryByRole('button',{name:/Tự động/})).toBeNull();
    expect(view.queryByRole('button',{name:/Giải phóng/})).toBeNull();
  });
  it('creates automatic Allocation once with idempotency',async()=>{
    localStorage.setItem('permissions',JSON.stringify(['allocation.read','allocation.create']));
    const view=render(<StockAllocations/>);await view.findByText('ALC-2026-7711');
    const button=await view.findByRole('button',{name:/Tự động/});
    await waitFor(()=>expect((button as HTMLButtonElement).disabled).toBe(false));
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(post).toHaveBeenCalledTimes(1));
    expect(post).toHaveBeenCalledWith('/api/inventory/allocations/auto',{reservationId:9031,quantity:40,locationId:null},{headers:{'Idempotency-Key':'key:allocation:auto:9031:40:auto'}});
  });
  it('releases an active Allocation with a reason',async()=>{
    localStorage.setItem('permissions',JSON.stringify(['allocation.read','allocation.release']));
    const view=render(<StockAllocations/>);await view.findByText('ALC-2026-7711');
    fireEvent.click(view.getByRole('button',{name:/Giải phóng/}));
    await view.findByRole('dialog');
    fireEvent.change(view.getByLabelText('Lý do thao tác Allocation'),{target:{value:'Đổi kế hoạch xuất'}});
    fireEvent.click(view.getByRole('button',{name:'Xác nhận'}));
    await waitFor(()=>expect(post).toHaveBeenCalledTimes(1));
    expect(post).toHaveBeenCalledWith('/api/inventory/allocations/7711/release',{reason:'Đổi kế hoạch xuất'},{headers:{'Idempotency-Key':'key:allocation:release:7711:release'}});
  });
});
