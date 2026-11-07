// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryTraceability from './InventoryTraceability';
import apiClient from '../services/apiClient';
import { getTraceabilityWarehouses } from '../services/traceabilityWarehouses';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn()}}));
vi.mock('../services/traceabilityWarehouses',()=>({getTraceabilityWarehouses:vi.fn()}));

const result={
  eventAnchorId:543,
  currentBuckets:[{
    inventoryStockId:51,productId:10,productCode:'SKU-10',productName:'Product 10',warehouseId:1,warehouseName:'DC HCM',
    locationId:7,locationCode:'A01-R01-B01',inventoryStatus:'Available',lotId:100,lotNumber:'LOT-A',
    serialId:null,serialNumber:null,onHandQuantity:10,reservedQuantity:0
  }],
  events:[
    {
      transactionId:41,productId:10,productCode:'SKU-10',productName:'Product 10',warehouseId:1,warehouseName:'DC HCM',
      transactionType:'Move',inventoryStatus:'Available',fromLocationCode:'A01-R01-B01',toLocationCode:'A01-R01-B02',
      lotNumber:'LOT-A',serialNumber:null,quantity:4,referenceType:'InventoryMove',referenceId:7,
      transactionDate:'2026-10-07T10:00:00Z',createdBy:5,createdByName:'Manager',note:'slotting',
      reversalOfTransactionId:null,correctiveTransactionId:42,reversalTransactionId:43,isReversed:true
    },
    {
      transactionId:43,productId:10,productCode:'SKU-10',productName:'Product 10',warehouseId:1,warehouseName:'DC HCM',
      transactionType:'Reversal',inventoryStatus:'Available',fromLocationCode:'A01-R01-B02',toLocationCode:'A01-R01-B01',
      lotNumber:'LOT-A',serialNumber:null,quantity:4,referenceType:'InventoryReversal',referenceId:41,
      transactionDate:'2026-10-07T11:00:00Z',createdBy:5,createdByName:'Manager',note:'corrective',
      reasonCode:'LOCATION_ERROR',
      reversalOfTransactionId:41,correctiveTransactionId:42,reversalTransactionId:43,isReversed:false
    }
  ]
};

const renderTrace=(entries=['/inventory-traceability'])=>render(
  <MemoryRouter initialEntries={entries}>
    <Routes><Route path="/inventory-traceability" element={<InventoryTraceability/>}/></Routes>
  </MemoryRouter>
);

describe('InventoryTraceability',()=>{
  beforeEach(()=>{
    vi.resetAllMocks();
    vi.mocked(getTraceabilityWarehouses).mockResolvedValue([
      {id:1,code:'HCM',name:'Kho TP.HCM'},
      {id:2,code:'HN',name:'Kho Hà Nội'}
    ]);
  });
  afterEach(()=>{
    cleanup();
    window.history.replaceState({}, '', window.location.pathname);
  });


  it('fails closed when a legacy response has no ledger anchor for the next page',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,eventAnchorId:undefined,eventsTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    await view.findByText('Trang sự kiện 1');
    fireEvent.click(view.getByRole('button',{name:'Sự kiện sau'}));
    expect(view.getByRole('alert').textContent).toContain('Thiếu mốc lịch sử');
    expect(apiClient.get).toHaveBeenCalledTimes(1);
  });

  it('keeps the ledger anchor across stock paging but resets it for a new manual search',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,bucketsTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    await view.findByText('Trang 1');
    fireEvent.click(view.getByRole('button',{name:'Trang sau'}));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/v1/inventory/traceability?productId=10&limit=200&bucketOffset=500&eventAnchorId=543'
    ));
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    await waitFor(()=>expect(apiClient.get).toHaveBeenLastCalledWith(
      '/api/v1/inventory/traceability?productId=10&limit=200'
    ));
  });


  it('shows document occurrences across receipt, QC and shipment for the same tracked product lot',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,
      relatedDocuments:[
        {warehouseId:1,warehouseName:'DC HCM',referenceType:'Shipment',referenceId:99,
          eventCount:1,firstTransactionDate:'2026-10-07T12:00:00Z',
          lastTransactionDate:'2026-10-07T12:00:00Z',lastTransactionId:86},
        {warehouseId:1,warehouseName:'DC HCM',referenceType:'GoodsReceipt',referenceId:21,
          eventCount:2,firstTransactionDate:'2026-10-06T08:00:00Z',
          lastTransactionDate:'2026-10-06T09:00:00Z',lastTransactionId:30}
      ],relatedDocumentsTruncated:false
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByRole('table',{name:'Chứng từ liên quan cùng lô hoặc sê-ri'})).toBeTruthy();
    expect(view.getByText('Mã tham chiếu #99')).toBeTruthy();
    expect(view.getByText('Mã tham chiếu #21')).toBeTruthy();
    expect(view.getByText(/chưa chứng minh quan hệ giao nhận/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/v1/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'
    );
  });

  it('warns on the 100-document bound and does not present an unrelated document scope',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,relatedDocuments:[{
        warehouseId:1,warehouseName:'DC HCM',referenceType:'StockTransfer',referenceId:12,
        eventCount:3,firstTransactionDate:'2026-10-07T10:00:00Z',
        lastTransactionDate:'2026-10-07T11:00:00Z',lastTransactionId:41
      }],relatedDocumentsTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Số sê-ri'),{target:{value:'SER-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByText(/Chỉ hiển thị 100 chứng từ mới nhất/)).toBeTruthy();
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'Shipment'}});
    expect(view.queryByRole('table',{name:'Chứng từ liên quan cùng lô hoặc sê-ri'})).toBeNull();
  });

  it('does not synthesize related documents for an untracked warehouse-only view',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    await view.findByRole('table',{name:'Dòng thời gian sổ cái phục vụ truy vết'});
    expect(view.queryByRole('table',{name:'Chứng từ liên quan cùng lô hoặc sê-ri'})).toBeNull();
  });


  it('shows SHIP evidence without implying completed recall or net delivered stock',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,
      shipmentExposures:[{
        shipmentId:42,warehouseId:1,shipmentCode:'SHIP-42',
        shipmentStatus:'ReturnToWarehouse',
        dispatchedAt:'2026-10-08T10:00:00Z',dispatchedQuantity:4,
        ledgerEventCount:2,lastTransactionId:82
      }]
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByRole('table',{name:'Shipment có giao dịch xuất giao cần rà soát'})).toBeTruthy();
    expect(view.getByText('SHIP-42')).toBeTruthy();
    expect(view.getByText('Đang xử lý hoàn về kho')).toBeTruthy();
    expect(view.getByText(/chưa trừ hàng hoàn/)).toBeTruthy();
    expect(view.getByText(/không tự động thu hồi/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/v1/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'
    );
  });

  it('limits shipment recall candidates and invalidates them on reference filter changes',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentExposures:[],shipmentExposuresTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Số sê-ri'),{target:{value:'SER-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByText(/Đã đạt giới hạn 100 Shipment/)).toBeTruthy();
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'Shipment'}});
    expect(view.queryByRole('table',{name:'Shipment có giao dịch xuất giao cần rà soát'})).toBeNull();
    expect(view.queryByText(/Đã đạt giới hạn 100 Shipment/)).toBeNull();
  });

  it('does not show shipment recall candidates for warehouse-only searches',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{...result,shipmentExposures:[]}} as never);
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    await view.findByRole('table',{name:'Dòng thời gian sổ cái phục vụ truy vết'});
    expect(view.queryByRole('table',{name:'Shipment có giao dịch xuất giao cần rà soát'})).toBeNull();
    expect(view.queryByRole('table',{name:'Liên kết Shipment Packing Picking theo lô hoặc sê-ri'})).toBeNull();
  });


  it('shows canonical Shipment to Packing to Picking evidence only for tracked identity',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentPickingEvidence:[{
        shipmentId:42,shipmentCode:'SHIP-42',warehouseId:1,
        packingSessionId:15,packingSessionCode:'PACK-15',
        pickingTaskId:16,pickingTaskCode:'PICK-16',
        pickingTaskLineId:17,allocationId:18,
        sourceLocationCode:'A-01',pickedQuantity:10
      }]
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    const table=await view.findByRole('table',{name:'Liên kết Shipment Packing Picking theo lô hoặc sê-ri'});
    expect(table.textContent).toContain('SHIP-42');
    expect(table.textContent).toContain('PACK-15');
    expect(table.textContent).toContain('PICK-16');
    expect(table.textContent).toContain('A-01');
    expect(table.textContent).toContain('10');
    expect(view.getByText(/chưa chứng minh quan hệ đến phiếu nhập/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/v1/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'
    );
  });

  it('caps canonical pick evidence and hides it when reference scope changes',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentPickingEvidence:[],shipmentPickingEvidenceTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Số sê-ri'),{target:{value:'SER-1'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByText(/Chỉ hiển thị 100 dòng Picking liên kết/)).toBeTruthy();
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'Shipment'}});
    expect(view.queryByRole('table',{name:'Liên kết Shipment Packing Picking theo lô hoặc sê-ri'})).toBeNull();
    expect(view.queryByText(/Chỉ hiển thị 100 dòng Picking liên kết/)).toBeNull();
  });


  it('shows actual HU root-child lineage for a tracked shipment lot without claiming delivery',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentHuEvidence:[{
        shipmentId:42,warehouseId:1,pickingTaskLineId:17,
        rootHandlingUnitId:100,rootHandlingUnitCode:'PALLET-100',
        contentHandlingUnitId:101,contentHandlingUnitCode:'CARTON-101',
        contentHandlingUnitBarcode:'BC-101',parentHandlingUnitId:100,
        hierarchyPath:'PALLET-100 → CARTON-101',packedQuantity:10
      }]
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    const table=await view.findByRole('table',{name:'Chuỗi Handling Unit gốc con đã xuất theo lô hoặc sê-ri'});
    expect(table.textContent).toContain('PALLET-100 → CARTON-101');
    expect(table.textContent).toContain('BC-101');
    expect(table.textContent).toContain('17');
    expect(table.textContent).toContain('10');
    expect(view.getByText(/Số lượng đã đóng không phải số lượng giao thành công/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/v1/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'
    );
  });

  it('warns about truncated HU ancestry and clears it when reference scope changes',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentHuEvidence:[],shipmentHuEvidenceTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Số sê-ri'),{target:{value:'SER-01'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByText(/Dữ liệu kiện HU có giới hạn truy vết/)).toBeTruthy();
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'Shipment'}});
    expect(view.queryByRole('table',{name:'Chuỗi Handling Unit gốc con đã xuất theo lô hoặc sê-ri'})).toBeNull();
    expect(view.queryByText(/Dữ liệu kiện HU có giới hạn truy vết/)).toBeNull();
  });

  it('does not expose HU hierarchy on a warehouse-only query',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentHuEvidence:[{
        shipmentId:42,warehouseId:1,pickingTaskLineId:17,
        rootHandlingUnitId:100,rootHandlingUnitCode:'PALLET-100',
        contentHandlingUnitId:101,contentHandlingUnitCode:'CARTON-101',
        contentHandlingUnitBarcode:'BC-101',hierarchyPath:'PALLET-100 → CARTON-101',
        packedQuantity:10
      }]
    }} as never);
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    await view.findByRole('table',{name:'Dòng thời gian sổ cái phục vụ truy vết'});
    expect(view.queryByRole('table',{name:'Chuỗi Handling Unit gốc con đã xuất theo lô hoặc sê-ri'})).toBeNull();
  });


  it('shows a real exported source document without claiming inbound lot origin',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentExportSources:[{
        shipmentId:42,shipmentCode:'SHIP-42',warehouseId:1,pickingTaskId:16,
        exportReceiptId:30,exportReceiptCode:'PX-30',
        exportReceiptDispatchedAt:'2026-10-09T07:00:00Z',
        exportReceiptProductQuantity:10
      }]
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Mã lô'),{target:{value:'LOT-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    const table=await view.findByRole('table',{name:'Liên kết Shipment Picking với phiếu xuất kho nguồn'});
    expect(table.textContent).toContain('SHIP-42');
    expect(table.textContent).toContain('PX-30');
    expect(table.textContent).toContain('16');
    expect(table.textContent).toContain('10');
    expect(view.getByText(/không phân chia theo lô\/sê-ri/)).toBeTruthy();
    expect(view.getByText(/không phải bằng chứng lô hàng bắt nguồn từ phiếu nhập/)).toBeTruthy();
    expect(apiClient.get).toHaveBeenCalledWith(
      '/api/v1/inventory/traceability?productId=10&lotNumber=LOT-A&limit=200'
    );
  });

  it('alerts for truncated export-source evidence and hides it on reference filter',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentExportSources:[],shipmentExportSourcesTruncated:true
    }} as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.change(view.getByLabelText('Số sê-ri'),{target:{value:'SER-A'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    expect(await view.findByText(/Danh sách chứng từ xuất nguồn chưa đầy đủ/)).toBeTruthy();
    fireEvent.change(view.getByLabelText('Loại tham chiếu'),{target:{value:'Shipment'}});
    expect(view.queryByRole('table',{name:'Liên kết Shipment Picking với phiếu xuất kho nguồn'})).toBeNull();
    expect(view.queryByText(/Danh sách chứng từ xuất nguồn chưa đầy đủ/)).toBeNull();
  });

  it('does not expose export-source documents on a warehouse-only search',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:{
      ...result,shipmentExportSources:[{
        shipmentId:42,shipmentCode:'SHIP-42',warehouseId:1,pickingTaskId:16,
        exportReceiptId:30,exportReceiptCode:'PX-30',
        exportReceiptProductQuantity:10
      }]
    }} as never);
    const view=renderTrace();
    await view.findByRole('option',{name:'HCM — Kho TP.HCM'});
    fireEvent.change(view.getByLabelText('Kho truy vết'),{target:{value:'1'}});
    fireEvent.click(view.getByRole('button',{name:'Truy vết'}));
    await view.findByRole('table',{name:'Dòng thời gian sổ cái phục vụ truy vết'});
    expect(view.queryByRole('table',{name:'Liên kết Shipment Picking với phiếu xuất kho nguồn'})).toBeNull();
  });

  it('can increase server event window to 500 without client-side truncation',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:result} as never);
    const view=renderTrace();
    expect((view.getByLabelText('Giới hạn sự kiện truy vết') as HTMLSelectElement).value).toBe('200');
    fireEvent.change(view.getByLabelText('Giới hạn sự kiện truy vết'),{
      target:{value:'500'}
    });
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith(
      '/api/v1/inventory/traceability?productId=10&limit=500'
    ));
  });

  it('surfaces request errors with a recovery path',async()=>{
    vi.mocked(apiClient.get).mockRejectedValue({response:{status:500,data:{message:'Máy chủ bận.'}}});
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    const alert=await view.findByRole('alert');
    expect(alert.textContent).toContain('Máy chủ bận.');
    expect(alert.textContent).toContain('thử lại');
  });

  it('announces loading state for asynchronous trace queries',async()=>{
    vi.mocked(apiClient.get).mockImplementation(()=>new Promise(()=>{}) as never);
    const view=renderTrace();
    fireEvent.change(view.getByLabelText('ID sản phẩm'),{target:{value:'10'}});
    fireEvent.click(view.getByText('Truy vết'));
    expect(view.getAllByRole('status').some(x=>x.textContent?.includes('Đang truy vết tồn kho'))).toBe(true);
    expect(view.getByText('Đang truy vết...')).toBeTruthy();
  });
});
