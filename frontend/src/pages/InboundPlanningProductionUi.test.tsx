// @vitest-environment jsdom
import { cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import PurchaseOrders from './PurchaseOrders';
import Asns from './Asns';

vi.mock('../services/apiClient',()=>({
  default:{get:vi.fn(),post:vi.fn(),put:vi.fn(),delete:vi.fn()},
}));

const get=vi.mocked(apiClient.get);

const po={
  id:41,externalPoId:'ERP-PO-4521',code:'PO-2026-4521',supplierCode:'SUP-001',supplierName:'Nông Sản Cao Nguyên',
  warehouseId:1,warehouseName:'DC Hồ Chí Minh',status:'Open',orderDate:'2026-10-03T00:00:00Z',expectedDate:'2026-10-06T00:00:00Z',baseOrderedQuantity:1200,
};

const asn={
  id:51,code:'ASN-2026-1051',purchaseOrderId:41,purchaseOrderCode:'PO-2026-4521',supplierCode:'SUP-001',supplierName:'Nông Sản Cao Nguyên',
  warehouseId:1,warehouseName:'DC Hồ Chí Minh',status:'InTransit',expectedArrivalAtUtc:'2026-10-05T08:00:00Z',baseExpectedQuantity:1200,
};

describe('Inbound planning production UI',()=>{
  beforeEach(()=>{
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role','Viewer');
  });

  afterEach(cleanup);

  it('renders Purchase Order as read-only with no master-data overfetch',async()=>{
    localStorage.setItem('permissions','["purchase_order.read"]');
    get.mockImplementation(async(url)=>{
      if(url==='/api/purchase-orders')return {data:[po]} as never;
      return {data:[]} as never;
    });

    const view=render(<PurchaseOrders/>);

    expect(await view.findByText('PO-2026-4521')).toBeTruthy();
    expect(view.getByRole('table',{name:'Danh sách đơn mua'})).toBeTruthy();
    expect(view.getByText('Đang mở')).toBeTruthy();
    expect(view.queryByText('Tạo đơn mua nháp')).toBeNull();
    expect(view.queryByRole('button',{name:'Mở đơn mua'})).toBeNull();
    expect(get).toHaveBeenCalledWith('/api/purchase-orders',{params:{status:undefined}});
    expect(get).not.toHaveBeenCalledWith('/api/warehouses');
    expect(get).not.toHaveBeenCalledWith('/api/products');
    expect(get).not.toHaveBeenCalledWith('/api/business-partners',expect.anything());
  });

  it('renders ASN as read-only and does not expose workflow actions',async()=>{
    localStorage.setItem('permissions','["asn.read"]');
    get.mockImplementation(async(url)=>{
      if(url==='/api/asns')return {data:[asn]} as never;
      return {data:[]} as never;
    });

    const view=render(<Asns/>);

    expect(await view.findByText('ASN-2026-1051')).toBeTruthy();
    expect(view.getByRole('table',{name:'Danh sách ASN'})).toBeTruthy();
    expect(view.getByText('Đang vận chuyển')).toBeTruthy();
    expect(view.queryByText('Tạo ASN nháp')).toBeNull();
    expect(view.queryByRole('button',{name:'Ghi nhận đến kho'})).toBeNull();
    expect(get).toHaveBeenCalledWith('/api/asns',{params:{status:undefined}});
    expect(get).not.toHaveBeenCalledWith('/api/purchase-orders');
    expect(get).not.toHaveBeenCalledWith('/api/warehouses');
    expect(get).not.toHaveBeenCalledWith('/api/products');
  });
});
