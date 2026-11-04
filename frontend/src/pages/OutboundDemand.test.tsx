// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import OutboundDemand from './OutboundDemand';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const permissionState=vi.hoisted(()=>({granted:new Set<string>()}));
vi.mock('../services/authorization',()=>({
  usePermission:(code:string)=>permissionState.granted.has(code),
}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':`key:${key}`}),
  completeIdempotentAction,
}));

const line={
  id:101,externalLineId:'1',productId:1,productCode:'SKU-1001',productName:'Cà phê',uomCode:'GOI',
  orderedQuantity:10,reservedQuantity:6,allocatedQuantity:6,pickedQuantity:0,shippedQuantity:0,
  backorderQuantity:4,cancelledQuantity:0,openQuantity:10,
};
const summary={
  id:88,orderCode:'SO-0088',externalOrderId:'EXT-88',customerId:2,customerCode:'CUS-02',customerName:'Minh An',
  warehouseId:1,warehouseName:'DC Hồ Chí Minh',priority:10,status:'Draft',orderedQuantity:10,reservedQuantity:0,
  allocatedQuantity:0,pickedQuantity:0,shippedQuantity:0,backorderQuantity:0,cancelledQuantity:0,openQuantity:10,
  createdAt:'2026-10-07T01:00:00Z',
};
const detail={...summary,rowVersion:'AQ==',lines:[line]};
const backorder={
  id:9,backorderCode:'BO-0009',salesOrderId:88,orderCode:'SO-0088',externalOrderId:'EXT-88',salesOrderLineId:101,
  warehouseId:1,warehouseName:'DC Hồ Chí Minh',productId:1,productCode:'SKU-1001',productName:'Cà phê',
  orderedQuantity:10,quantity:4,recoveredQuantity:0,cancelledQuantity:0,remainingQuantity:4,status:'Open',
  createdAt:'2026-10-07T01:05:00Z',rowVersion:'Ag==',
};

const grant=(...codes:string[])=>{
  permissionState.granted.clear();
  codes.forEach(code=>permissionState.granted.add(code));
};

const reads=()=>vi.mocked(apiClient.get).mockImplementation(async url=>{
  if(url==='/api/v1/sales-orders')return {data:[summary]} as never;
  if(url==='/api/v1/backorders')return {data:[backorder]} as never;
  if(url==='/api/v1/sales-orders/88')return {data:detail} as never;
  if(url==='/api/v1/business-partners')return {data:{items:[{id:2,code:'CUS-02',name:'Minh An',isCustomer:true,isActive:true}]}} as never;
  if(url==='/api/v1/warehouses')return {data:[{id:1,code:'WH-01',name:'DC Hồ Chí Minh',isActive:true}]} as never;
  if(url==='/api/v1/products')return {data:[{id:1,code:'SKU-1001',name:'Cà phê',isActive:true}]} as never;
  return {data:[]} as never;
});

describe('Outbound demand and backorder workbench',()=>{
  afterEach(cleanup);
  beforeEach(()=>{
    vi.resetAllMocks();
    completeIdempotentAction.mockReset();
    grant('sales_order.read','backorder.read');
    reads();
  });

  it('shows canonical demand metrics read-only without mutation permissions',async()=>{
    const view=render(<OutboundDemand/>);
    expect(await view.findByText('SO-0088')).toBeTruthy();
    expect(await view.findByText(/BO-0009\s+—\s+SKU-1001/)).toBeTruthy();
    expect(view.getByRole('tablist',{name:'Chọn phân khu quản lý đơn bán hàng'})).toBeTruthy();
    expect(view.getByRole('tab',{name:'Tất cả phân khu'})).toBeTruthy();
    expect(view.getByRole('tab',{name:'1. Danh sách đơn bán (1)'})).toBeTruthy();
    expect(view.getAllByText(/Trang 1\/1/).length).toBeGreaterThan(0);
    const codeBtn=view.getByRole('button',{name:'SO-0088'}) as HTMLButtonElement;
    expect(codeBtn.getAttribute('title')).toBe('Mã đơn đầy đủ: SO-0088 (Bấm Sao chép để lấy mã gốc)');
    expect((view.getByRole('columnheader',{name:'Đặt hàng'}) as HTMLElement).style.whiteSpace).toBe('nowrap');
    expect(view.queryByText('Release demand')).toBeNull();
    expect(view.queryByText('Reallocate')).toBeNull();
    expect(view.queryByText('Tạo Đơn bán hàng')).toBeNull();
    cleanup();
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(url==='/api/v1/sales-orders')return {data:[{...summary,orderCode:'SO-20261029-999-4C0'}]} as never;
      if(url==='/api/v1/backorders')return {data:[]} as never;
      return {data:[]} as never;
    });
    const emptyView=render(<OutboundDemand/>);
    expect(await emptyView.findByText('Không có đơn nợ hàng nào đang mở')).toBeTruthy();
    expect(emptyView.getByRole('button',{name:'SO-20261029...4C0'})).toBeTruthy();
  });

  it('releases sales order once with idempotency',async()=>{
    grant('sales_order.read','backorder.read','sales_order.release');
    vi.mocked(apiClient.post).mockResolvedValue({data:{...detail,status:'Released',reservedQuantity:6,allocatedQuantity:6,backorderQuantity:4}} as never);
    const view=render(<OutboundDemand/>);
    await view.findByText('SO-0088');
    fireEvent.click(view.getByText('SO-0088'));
    const button=await view.findByText('Release demand');
    fireEvent.click(button);
    fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/v1/sales-orders/88/release',{
      rowVersion:'AQ=='
    },{headers:{'Idempotency-Key':'key:sales-order-release-88'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('sales-order-release-88');
  });

  it('maps execution-started backorder recovery conflict',async()=>{
    grant('sales_order.read','backorder.read','backorder.manage');
    vi.mocked(apiClient.post).mockRejectedValue({
      response:{status:409,data:{code:'BACKORDER_EXECUTION_STARTED',message:'blocked'}}
    });
    const view=render(<OutboundDemand/>);
    await view.findByText(/BO-0009\s+—\s+SKU-1001/);
    fireEvent.change(view.getByLabelText('Recover quantity BO-0009'),{target:{value:'4'}});
    fireEvent.click(view.getByText('Reallocate'));
    await waitFor(()=>expect(view.getByRole('alert').textContent).toContain('Picking đã bắt đầu'));
  });

  it('only exposes create form when master-data read permissions are also present',async()=>{
    grant('sales_order.read','backorder.read','sales_order.create');
    const first=render(<OutboundDemand/>);
    await first.findByText('SO-0088');
    expect(first.queryByText('Tạo Đơn bán hàng')).toBeNull();
    cleanup();

    grant('sales_order.read','backorder.read','sales_order.create','partner.read','product.read','warehouse.read');
    reads();
    const second=render(<OutboundDemand/>);
    const create=await second.findByText('Tạo Đơn bán hàng');
    fireEvent.click(create);
    expect(await second.findByLabelText('External Order ID')).toBeTruthy();
    expect(second.getByText('Mã đơn kênh bán')).toBeTruthy();
    expect(second.getByText('Mã dòng kênh bán')).toBeTruthy();
    expect(second.getByRole('button',{name:'Lưu đơn bán hàng'})).toBeTruthy();
    expect(second.getByLabelText('Khách hàng Sales Order')).toBeTruthy();
    expect(second.getByLabelText('SKU Sales Order 1')).toBeTruthy();
  });

  it('imports marketplace Excel orders via FormData with real File, downloads template, and normalizes 3-tier VN address',async()=>{
    vi.mocked(apiClient.post).mockImplementation(async url=>{
      if(url==='/api/v1/sales-orders/import-marketplace-excel'){
        return {data:{importBatchId:'IMP-MKT-99',importedOrders:8,channel:'Shopee',status:'Đã đồng bộ 8 đơn Shopee'}} as never;
      }
      if(url==='/api/v1/shipments/normalize-vn-address'){
        return {data:{provinceName:'TP. Hồ Chí Minh',districtName:'Quận 1',wardName:'Phường Bến Nghé',wardCode:'26734',normalizedFullAddress:'Số 12 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh'}} as never;
      }
      return {data:{}} as never;
    });
    const view=render(<OutboundDemand/>);
    await view.findByText('SO-0088');

    expect(view.getByText('Tỷ lệ đáp ứng (đơn đã xuất giao)')).toBeTruthy();
    expect(view.getByText('Chọn tệp Excel (.xlsx)')).toBeTruthy();
    expect(view.getByRole('button',{name:'Phân tích định tuyến & đề xuất tách đơn liên kho'})).toBeTruthy();
    const fileInput=view.getByLabelText('Tệp Excel đơn hàng sàn') as HTMLInputElement;
    const excelFile=new File(['xlsx-bytes'],'orders.xlsx',{
      type:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    fireEvent.change(fileInput,{target:{files:[excelFile]}});

    fireEvent.click(view.getByRole('button',{name:'Nhập Excel Đơn hàng Sàn'}));
    expect(await view.findByText(/IMP-MKT-99/)).toBeTruthy();
    const importCall=vi.mocked(apiClient.post).mock.calls.find(c=>c[0]==='/api/v1/sales-orders/import-marketplace-excel');
    expect(importCall).toBeTruthy();
    const sentFormData=importCall?.[1] as FormData;
    expect(sentFormData).toBeInstanceOf(FormData);
    expect(sentFormData.get('file')).toBeInstanceOf(File);
    expect((sentFormData.get('file') as File).name).toBe('orders.xlsx');
    expect(sentFormData.get('channel')).toBe('Shopee');

    fireEvent.click(view.getByRole('button',{name:'Tải file mẫu .xlsx'}));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith('/api/v1/import-templates/marketplace-orders',{responseType:'blob'}));

    fireEvent.click(view.getByRole('button',{name:'Chuẩn hóa Địa chỉ 3 Cấp VN'}));
    expect(await view.findByText(/Phường Bến Nghé \(Mã: 26734\)/)).toBeTruthy();
    expect(apiClient.post).toHaveBeenCalledWith('/api/v1/shipments/normalize-vn-address',expect.objectContaining({rawAddress:expect.any(String)}));
  });

  it('Wave 60 [W60-03]: initializes Smart DOM order splitting with SO-QA-001 / QA-E2E-001 / WH01 and toggles MULTI_WAREHOUSE_SPLIT across WH01 & WH02',async()=>{
    const view=render(<OutboundDemand/>);
    await view.findByText('SO-0088');

    const domCard=view.getByTestId('smart-dom-order-splitting-card');
    expect(domCard.textContent).toContain('SO-QA-001');
    expect(domCard.textContent).toContain('QA-E2E-001');
    expect(domCard.textContent).toContain('Kho Trung tâm (WH01)');
    expect(domCard.textContent).not.toContain('SO-2026-DOM-01');
    expect(domCard.textContent).not.toContain('DC Bình Dương');

    const modeSelect=view.getByTestId('select-dom-fulfillment-mode');
    fireEvent.change(modeSelect,{target:{value:'MULTI_WAREHOUSE_SPLIT'}});
    expect(domCard.textContent).toContain('SO-QA-001-SUB1');
    expect(domCard.textContent).toContain('SO-QA-001-SUB2');
    expect(domCard.textContent).toContain('Kho Trung tâm (WH01)');
    expect(domCard.textContent).toContain('WH02 — Kho Chi Nhánh Hà Nội');
  });

  it('Wave 15: preserves immutable SO-202610-001 order code verbatim and disables batch recovery when backorder list is empty',async()=>{
    vi.mocked(apiClient.get).mockImplementation(async url=>{
      if(url==='/api/v1/sales-orders')return {data:[{...summary,id:101,orderCode:'SO-202610-001',warehouseName:'Kho Trung tâm WH01',createdAt:'2026-10-15T08:00:00Z'}]} as never;
      if(url==='/api/v1/backorders')return {data:[]} as never;
      if(url==='/api/v1/business-partners')return {data:{items:[{id:2,code:'CUS-02',name:'Minh An',isCustomer:true,isActive:true}]}} as never;
      if(url==='/api/v1/warehouses')return {data:[{id:1,code:'WH01',name:'Kho Trung tâm',isActive:true}]} as never;
      if(url==='/api/v1/products')return {data:[{id:1,code:'SKU-1001',name:'Cà phê',isActive:true}]} as never;
      return {data:[]} as never;
    });

    const soView=render(<MemoryRouter initialEntries={['/sales-orders']}><OutboundDemand/></MemoryRouter>);
    expect(await soView.findByText('SO-202610-001')).toBeTruthy();
    expect(soView.container.textContent).not.toContain('SO-202611-001');
    expect(soView.container.textContent).toContain('Kho Trung tâm (WH01)');
    const codeSortHeader=soView.getByRole('columnheader',{name:/Mã đơn bán/}) as HTMLElement;
    const customerSortHeader=soView.getByRole('columnheader',{name:/Khách hàng \/ Kênh bán/}) as HTMLElement;
    expect(codeSortHeader.style.paddingRight).toBe('14px');
    expect(codeSortHeader.style.whiteSpace).toBe('nowrap');
    expect(customerSortHeader.style.paddingRight).toBe('14px');
    expect(customerSortHeader.style.whiteSpace).toBe('nowrap');
    cleanup();

    const boView=render(<MemoryRouter initialEntries={['/backorders']}><OutboundDemand/></MemoryRouter>);
    const reasonEl=await boView.findByTestId('backorder-empty-disabled-reason');
    expect(reasonEl.textContent).toContain('Không có đơn thiếu hàng cần xử lý');
    expect((boView.getByTestId('btn-trigger-backorder-allocation') as HTMLButtonElement).disabled).toBe(true);
  });
});
