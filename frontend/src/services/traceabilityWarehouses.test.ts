// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from './apiClient';
import { getTraceabilityWarehouses } from './traceabilityWarehouses';

vi.mock('./apiClient',()=>({default:{get:vi.fn()}}));

describe('Danh sách kho truy vết được cấp quyền',()=>{
  beforeEach(()=>vi.resetAllMocks());

  it('đọc danh sách trực tiếp từ API truy vết, không từ API đảo giao dịch',async()=>{
    const choices=[{id:7,code:'K7',name:'Kho phía Nam'}];
    vi.mocked(apiClient.get).mockResolvedValue({data:choices} as never);
    expect(await getTraceabilityWarehouses()).toEqual(choices);
    expect(apiClient.get).toHaveBeenCalledTimes(1);
    expect(apiClient.get).toHaveBeenCalledWith('/api/inventory/traceability-warehouses');
  });

  it.each([
    null,
    {items:[{id:7,code:'K7',name:'Sai hợp đồng'}]},
    [{id:0,code:'K0',name:'Sai ID'}],
    [{id:7,code:9,name:'Sai mã kho'}],
    [{id:7,code:'',name:'Thiếu mã kho'}],
    [{id:7,code:'K7',name:'   '}],
    [{id:7,code:'K7',name:'Kho chính'},{id:7,code:'K7',name:'Kho lặp'}]
  ])('từ chối payload sai thay vì đưa ra tùy chọn kho giả: %j',async data=>{
    vi.mocked(apiClient.get).mockResolvedValue({data} as never);
    await expect(getTraceabilityWarehouses()).rejects.toThrow('không hợp lệ');
  });
});
