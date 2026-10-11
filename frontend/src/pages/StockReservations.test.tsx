// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StrictMode } from 'react';
import apiClient from '../services/apiClient';
import { setCurrentPermissions } from '../services/authorization';
import StockReservations from './StockReservations';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
const get=vi.mocked(apiClient.get),post=vi.mocked(apiClient.post);
const reservation={id:3,reservationCode:'GH-3',productCode:'SP',productName:'Sản phẩm',warehouseName:'Kho',quantity:10,remainingQuantity:10,consumedQuantity:0,releasedQuantity:0,status:'Active',sourceType:'Manual',rowVersion:'AAAAAAAAAAE=',baseUomNameSnapshot:'Cái',expiresAt:'2026-10-09T12:00:00Z'};
const page={items:[reservation],totalRecords:1,pageIndex:1,pageSize:20};
const read=['reservation.read'];
beforeEach(()=>{vi.resetAllMocks();localStorage.clear();localStorage.setItem('role','Admin');localStorage.setItem('permissions',JSON.stringify(read));get.mockImplementation(async url=>({data:url==='/api/stock-reservations'?page:url==='/api/stock-reservations/3'?reservation:[]}) as never);});
afterEach(()=>{cleanup();vi.restoreAllMocks();});

describe('quản lý giữ hàng Manual theo permission',()=>{
  it('loading preserves table cell semantics and announces Vietnamese status',()=>{
    get.mockReturnValue(new Promise(()=>{}));
    const view=render(<StockReservations/>);
    const cell=view.getByRole('cell',{name:'Đang tải giữ hàng...'});
    expect(within(cell).getByRole('status').textContent).toBe('Đang tải giữ hàng...');
  });
  it('reader loads without optional masters and never inherits Admin role mutation rights',async()=>{
    const view=render(<StockReservations/>);await view.findByText('GH-3');expect(get.mock.calls.map(x=>x[0])).toEqual(['/api/stock-reservations']);
    expect(view.queryByRole('button',{name:'Tạo giữ hàng'})).toBeNull();expect(view.queryByRole('button',{name:'Xử lý hết hạn'})).toBeNull();expect(view.queryByRole('button',{name:'Giải phóng GH-3'})).toBeNull();
    expect(view.getByRole('cell',{name:'Đang giữ'})).toBeTruthy();expect(view.getByText('Thủ công')).toBeTruthy();expect(document.title).toBe('Giữ hàng — ERP KHO');
  });
  it('ExportReceipt hold stays read-only even with release grant and a technical token',async()=>{
    localStorage.setItem('permissions',JSON.stringify([...read,'reservation.release']));get.mockResolvedValue({data:{...page,items:[{...reservation,sourceType:'ExportReceipt',sourceCode:'PX-1'}]}} as never);
    const view=render(<StockReservations/>);await view.findByText('GH-3');expect(view.queryByRole('button',{name:'Giải phóng GH-3'})).toBeNull();expect(view.getByText('Phiếu xuất / PX-1')).toBeTruthy();
  });
  it('double-submit sends one release request with snapshot token and safe 409 keeps user input',async()=>{
    localStorage.setItem('permissions',JSON.stringify([...read,'reservation.release']));let reject!:(value:unknown)=>void;post.mockReturnValue(new Promise((_,fail)=>{reject=fail}));
    const view=render(<StockReservations/>);await view.findByText('GH-3');fireEvent.click(view.getByRole('button',{name:'Giải phóng GH-3'}));await view.findByRole('dialog');
    fireEvent.change(view.getByLabelText('Số lượng'),{target:{value:'2'}});fireEvent.change(view.getByLabelText('Lý do'),{target:{value:'Không còn nhu cầu'}});
    const button=view.getByRole('button',{name:'Xác nhận giải phóng'});fireEvent.click(button);fireEvent.click(button);expect(post).toHaveBeenCalledTimes(1);expect((button as HTMLButtonElement).disabled).toBe(true);
    expect(post.mock.calls[0][1]).toEqual({quantity:2,reason:'Không còn nhu cầu',rowVersion:reservation.rowVersion});expect(post.mock.calls[0][2]?.headers?.['Idempotency-Key']).toBeTruthy();
    await act(async()=>reject({isAxiosError:true,response:{status:409,data:{message:'SQL PRIVATE'}}}));
    expect(view.getByRole('alert').textContent).toBe('Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.');expect(view.queryByText('SQL PRIVATE')).toBeNull();expect((view.getByLabelText('Lý do') as HTMLTextAreaElement).value).toBe('Không còn nhu cầu');expect(post).toHaveBeenCalledTimes(1);
  });
  it('create uses Base quantity and independent master reads',async()=>{
    localStorage.setItem('permissions',JSON.stringify([...read,'reservation.create','product.read','warehouse.read']));
    get.mockImplementation(async url=>({data:url==='/api/stock-reservations'?page:url==='/api/products'?[{id:2,code:'SP2',name:'Sản phẩm 2',unitName:'Cái',unitDecimalPlaces:4,isActive:true}]:[{id:1,name:'Kho 1'}]}) as never);
    post.mockResolvedValue({data:{}} as never);const view=render(<StockReservations/>);await view.findByText('GH-3');fireEvent.click(view.getByText('Tạo giữ hàng'));await view.findByRole('dialog');
    fireEvent.change(view.getByLabelText('Kho'),{target:{value:'1'}});fireEvent.change(view.getByLabelText('Sản phẩm'),{target:{value:'2'}});fireEvent.change(view.getByLabelText('Số lượng'),{target:{value:'0.0001'}});fireEvent.click(view.getByText('Xác nhận tạo'));
    await waitFor(()=>expect(post).toHaveBeenCalledTimes(1));expect(post.mock.calls[0][1]).toEqual({warehouseId:1,productId:2,quantity:0.0001});
  });
  it('production StrictMode dialog remains interactive outside inert page content',async()=>{
    localStorage.setItem('permissions',JSON.stringify([...read,'reservation.create','product.read','warehouse.read']));
    const view=render(<StrictMode><StockReservations/></StrictMode>);await view.findByText('GH-3');fireEvent.click(view.getByText('Tạo giữ hàng'));
    const dialog=await view.findByRole('dialog');expect((dialog.parentElement as HTMLElement).inert).not.toBe(true);
    expect(view.getByRole('combobox',{name:'Kho'})).toBeTruthy();expect(view.getByRole('combobox',{name:'Sản phẩm'})).toBeTruthy();
    expect(view.getByText('Kho',{selector:'label'}).textContent).toBe('Kho');
  });
  it('reconciliation is read-only and translates technical issue types',async()=>{
    get.mockImplementation(async url=>({data:url==='/api/stock-reservations'?page:[{issue:'LedgerMismatch',warehouseId:1,productId:2,ledgerReserved:10,stockReserved:8}]}) as never);
    const view=render(<StockReservations/>);await view.findByText('GH-3');fireEvent.click(view.getByText('Đối chiếu giữ hàng'));await view.findByRole('dialog');expect(view.getByText(/Lượng giữ hàng không khớp/)).toBeTruthy();expect(view.queryByText('LedgerMismatch')).toBeNull();expect(post).not.toHaveBeenCalled();
  });
  for(const kind of ['list','detail'] as const)it('mounted revoke/regrant discards late '+kind+' response',async()=>{
    let release!:(value:never)=>void;const late=new Promise(resolve=>{release=resolve});if(kind==='list')get.mockImplementationOnce(()=>late as never);
    const view=render(<StockReservations/>);const heading=view.getByRole('heading',{name:'Giữ hàng'});
    if(kind==='detail'){await view.findByText('GH-3');get.mockImplementationOnce(()=>late as never);fireEvent.click(view.getByRole('button',{name:'Chi tiết GH-3'}));}
    await act(async()=>setCurrentPermissions([]));expect(heading.isConnected).toBe(true);await act(async()=>setCurrentPermissions(read));await view.findByText('GH-3');
    await act(async()=>release({data:kind==='list'?{...page,items:[{...reservation,reservationCode:'OLD_SECRET'}]}:{...reservation,reservationCode:'OLD_SECRET'}} as never));
    expect(view.queryByText('OLD_SECRET')).toBeNull();expect(view.queryByRole('dialog')).toBeNull();
  });
});
