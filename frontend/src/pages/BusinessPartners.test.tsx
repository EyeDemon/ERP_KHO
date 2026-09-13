// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import BusinessPartners from './BusinessPartners';
import apiClient from '../services/apiClient';
vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));
const get = vi.mocked(apiClient.get); const post = vi.mocked(apiClient.post); const put = vi.mocked(apiClient.put);
const partner = {id:1,code:'BP01',name:'Đối tác',isSupplier:true,isCustomer:true,isActive:true,rowVersion:'AQ=='};
describe('BusinessPartners (mocked API)',()=>{
  afterEach(cleanup);
  beforeEach(()=>{vi.resetAllMocks();localStorage.clear();localStorage.setItem('role','Admin');get.mockResolvedValue({data:{items:[partner],totalPages:1}} as never);post.mockReset();put.mockReset()});
  it('renders both roles and filters through the paged API',async()=>{const v=render(<BusinessPartners/>);expect(await v.findByText('Nhà cung cấp, Khách hàng')).toBeTruthy();fireEvent.change(v.getByLabelText('Lọc vai trò'),{target:{value:'supplier'}});await waitFor(()=>expect(get).toHaveBeenLastCalledWith('/api/business-partners',expect.objectContaining({params:expect.objectContaining({role:'supplier'})}))) });
  it('prevents a double submit while a create is pending',async()=>{let done!:()=>void;post.mockReturnValue(new Promise(r=>{done=()=>r({data:{}} as never)}));const v=render(<BusinessPartners/>);await v.findByText('BP01');fireEvent.click(v.getByText('Thêm đối tác'));fireEvent.change(v.getByLabelText('Mã đối tác'),{target:{value:' new-1 '}});fireEvent.change(v.getByLabelText('Tên đối tác'),{target:{value:'Mới'}});const save=v.getByText('Lưu');fireEvent.click(save);fireEvent.click(save);expect(post).toHaveBeenCalledTimes(1);done();await waitFor(()=>expect(v.queryByText('Đang lưu...')).toBeNull())});
  it('keeps code disabled and sends rowVersion on edit',async()=>{put.mockResolvedValue({} as never);const v=render(<BusinessPartners/>);await v.findByText('BP01');fireEvent.click(v.getByText('Sửa'));expect((v.getByLabelText('Mã đối tác') as HTMLInputElement).disabled).toBe(true);fireEvent.click(v.getByText('Lưu'));await waitFor(()=>expect(put).toHaveBeenCalledWith('/api/business-partners/1',expect.objectContaining({rowVersion:'AQ=='}))) });
});
