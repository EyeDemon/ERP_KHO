// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import InventoryReversals from './InventoryReversals';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
vi.mock('../services/authorization',()=>({usePermission:(permission:string)=>permission==='inventory_ledger.read'}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':key}),
  completeIdempotentAction:vi.fn(),
}));

beforeEach(()=>{
  vi.resetAllMocks();
  vi.mocked(apiClient.get).mockImplementation(async url=>{
    if(String(url).includes('reversal-warehouses'))
      return {data:[{id:1,code:'HCM',name:'Kho Hồ Chí Minh'}]} as never;
    return {data:{
      items:[{
        id:41,productId:10,productCode:'SKU-10',productName:'Sản phẩm 10',
        warehouseId:1,warehouseName:'Kho HCM',transactionType:'Move',
        inventoryStatus:'Available',quantity:4,transactionDate:'2026-10-07T10:00:00Z',
        isReversed:false
      }],
      totalRecords:1,pageIndex:1,pageSize:20,totalPages:1
    }} as never;
  });
});
afterEach(cleanup);

it('sends trimmed SKU prefix with server-side reversal filter after prior load completes',async()=>{
  const view=render(<MemoryRouter><InventoryReversals/></MemoryRouter>);
  await view.findByText('Di chuyển vị trí');
  fireEvent.change(view.getByLabelText('Tìm theo mã sản phẩm'),{target:{value:' SKU-10 '}});
  fireEvent.change(view.getByLabelText('Lọc theo trạng thái đảo'),{target:{value:'pending'}});
  await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
    '/api/inventory/reversal-candidates?page=1&pageSize=20&isReversed=false'
  ));
  await waitFor(()=>expect((view.getByText('Tìm giao dịch') as HTMLButtonElement).disabled).toBe(false));
  fireEvent.click(view.getByText('Tìm giao dịch'));
  await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
    '/api/inventory/reversal-candidates?page=1&pageSize=20&isReversed=false&productCode=SKU-10'
  ));
});
