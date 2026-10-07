// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Shipments from './Shipments';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const permissionState=vi.hoisted(()=>({granted:new Set<string>()}));
vi.mock('../services/authorization',()=>({
  usePermission:(code:string)=>permissionState.granted.has(code),
}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':'key:'+key}),
  completeIdempotentAction,
}));

const hu={id:1,handlingUnitId:9911,huCode:'PALLET-01',barcode:'PALLET-01',type:'Pallet',status:'Closed',sequence:1,contentQuantity:40,assignedAt:'2026-10-06T03:00:00Z'};
const summary={id:7701,shipmentCode:'SHIP-2026-7701',packingSessionId:9901,packingSessionCode:'PACK-2026-9901',warehouseId:1,warehouseName:'DC Hồ Chí Minh',sourceType:'Reservation',sourceId:9040,sourceCode:'RSV-PACK-0040',status:'Ready',stagingLocationId:undefined,stagingLocationCode:undefined,handlingUnitCount:1,loadedHandlingUnitCount:0,createdAt:'2026-10-06T03:00:00Z'};
const detail={...summary,rowVersion:'AQ==',handlingUnits:[hu]};
const appointment={id:1043,code:'APT-2026-1043',warehouseId:1,direction:1,status:5,dockId:102,dockCode:'D-02',vehiclePlate:'50H-220.18'};
const grant=(...permissions:string[])=>{permissionState.granted.clear();permissions.forEach(p=>permissionState.granted.add(p));};
const reads=(shipment:Record<string,unknown>=detail)=>vi.mocked(apiClient.get).mockImplementation(async (url)=>{
  if(url==='/api/shipments') return {data:[summary]} as never;
  if(String(url).startsWith('/api/dock-yard/appointments')) return {data:[appointment]} as never;
  return {data:shipment} as never;
});

describe('Shipment staging & loading workbench',()=>{
  afterEach(cleanup);
  beforeEach(()=>{vi.resetAllMocks();completeIdempotentAction.mockReset();grant('shipment.read');reads()});

  it('shows read-only shipment evidence without mutation permissions',async()=>{
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    expect(await view.findByText(/Foundation hiện quản lý Shipment READY/)).toBeTruthy();
    expect(view.queryByText('Đưa vào Staging')).toBeNull();
    expect(view.queryByText('Bắt đầu Loading')).toBeNull();
  });

  it('stages shipment once with idempotency',async()=>{
    grant('shipment.read','shipment.stage');
    vi.mocked(apiClient.post).mockResolvedValue({data:{...detail,status:'Staging',handlingUnits:[{...hu,status:'Staged'}]}} as never);
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    const input=await view.findByLabelText('Shipment staging location');
    fireEvent.change(input,{target:{value:'STG-OUT-01'}});
    const button=view.getByText('Xác nhận Staging');
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/shipments/7701/stage',{stagingLocationCode:'STG-OUT-01',rowVersion:'AQ=='},{headers:{'Idempotency-Key':'key:shipment-stage-7701'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('shipment-stage-7701');
  });

  it('requires both shipment.load and loading.execute before showing loading controls',async()=>{
    reads({...detail,status:'Staging',handlingUnits:[{...hu,status:'Staged'}]});
    grant('shipment.read','shipment.load');
    const first=render(<Shipments/>);
    await first.findByText('SHIP-2026-7701');
    fireEvent.click(first.getByText('SHIP-2026-7701'));
    expect(await first.findByText(/Foundation hiện quản lý Shipment READY/)).toBeTruthy();
    expect(first.queryByText('Bắt đầu Loading')).toBeNull();
    cleanup();

    grant('shipment.read','shipment.load','loading.execute');
    reads({...detail,status:'Staging',handlingUnits:[{...hu,status:'Staged'}]});
    const second=render(<Shipments/>);
    await second.findByText('SHIP-2026-7701');
    fireEvent.click(second.getByText('SHIP-2026-7701'));
    expect(await second.findByLabelText('Outbound appointment loading')).toBeTruthy();
  });

  it('shows dispatch only with shipment.dispatch and submits once with idempotency',async()=>{
    const loaded={...detail,status:'Loaded',loadedHandlingUnitCount:1,sealNumber:'SEAL-01',handlingUnits:[{...hu,status:'Loaded',loadedAt:'2026-10-06T03:30:00Z'}]};
    reads(loaded);
    grant('shipment.read');
    const readOnly=render(<Shipments/>);
    await readOnly.findByText('SHIP-2026-7701');
    fireEvent.click(readOnly.getByText('SHIP-2026-7701'));
    expect(await readOnly.findByText(/Shipment đã LOADED/)).toBeTruthy();
    expect(readOnly.queryByRole('button',{name:'Dispatch Shipment'})).toBeNull();
    cleanup();

    grant('shipment.read','shipment.dispatch');
    reads(loaded);
    vi.mocked(apiClient.post).mockResolvedValue({data:{...loaded,status:'Dispatched',dispatchedAt:'2026-10-06T03:40:00Z',dispatchedBy:7,dispatchedByName:'Shipping Manager',handlingUnits:[{...hu,status:'Shipped',loadedAt:'2026-10-06T03:30:00Z'}]}} as never);
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    const button=await view.findByRole('button',{name:'Dispatch Shipment'});
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith(
      '/api/shipments/7701/dispatch',
      {rowVersion:'AQ=='},
      {headers:{'Idempotency-Key':'key:shipment-dispatch-7701'}}
    );
    expect(completeIdempotentAction).toHaveBeenCalledWith('shipment-dispatch-7701');
    expect(await view.findByText(/Shipment đã DISPATCHED/)).toBeTruthy();
  });

  it('moves a dispatched shipment to in-transit only with shipment.update',async()=>{
    const dispatched={...detail,status:'Dispatched',dispatchedAt:'2026-10-06T03:40:00Z',handlingUnits:[{...hu,status:'Shipped'}]};
    reads(dispatched);
    grant('shipment.read','shipment.update');
    vi.mocked(apiClient.post).mockResolvedValue({data:{...dispatched,status:'InTransit',inTransitAt:'2026-10-06T04:00:00Z',trackingEvents:[]}} as never);
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    const button=await view.findByRole('button',{name:'Chuyển IN_TRANSIT'});
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith(
      '/api/shipments/7701/mark-in-transit',
      {rowVersion:'AQ=='},
      {headers:{'Idempotency-Key':'key:shipment-in-transit-7701'}}
    );
  });

  it('confirms delivery with POD metadata only with shipment.confirm_delivery',async()=>{
    const inTransit={...detail,status:'InTransit',dispatchedAt:'2026-10-06T03:40:00Z',inTransitAt:'2026-10-06T04:00:00Z',trackingEvents:[]};
    reads(inTransit);
    grant('shipment.read','shipment.confirm_delivery');
    vi.mocked(apiClient.post).mockResolvedValue({data:{
      ...inTransit,status:'Delivered',deliveredAt:'2026-10-06T05:00:00Z',
      proofOfDelivery:{deliveredAt:'2026-10-06T05:00:00Z',receiverName:'Nguyễn Văn A',evidenceReference:'pod://proof-1',carrierReference:'CR-1',createdAt:'2026-10-06T05:00:01Z'}
    }} as never);
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    fireEvent.change(await view.findByLabelText('POD receiver name'),{target:{value:'Nguyễn Văn A'}});
    fireEvent.change(view.getByLabelText('POD evidence reference'),{target:{value:'pod://proof-1'}});
    fireEvent.change(view.getByLabelText('POD carrier reference'),{target:{value:'CR-1'}});
    const button=view.getByRole('button',{name:'Xác nhận DELIVERED'});
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith(
      '/api/shipments/7701/delivery-confirm',
      {
        receiverName:'Nguyễn Văn A',
        evidenceReference:'pod://proof-1',
        carrierReference:'CR-1',
        deliveryNote:undefined,
        rowVersion:'AQ=='
      },
      {headers:{'Idempotency-Key':'key:shipment-delivery-confirm-7701'}}
    );
  });

  it('shows delivery failure retry and return controls without inventory wording',async()=>{
    const failed={...detail,status:'DeliveryFailed',dispatchedAt:'2026-10-06T03:40:00Z',deliveryFailedAt:'2026-10-06T05:00:00Z',trackingEvents:[]};
    reads(failed);
    grant('shipment.read','shipment.update');
    vi.mocked(apiClient.post).mockResolvedValue({data:{...failed,status:'InTransit',rowVersion:'Ag=='}} as never);
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    expect(await view.findByText(/không tạo thêm SHIP transaction/)).toBeTruthy();
    const retry=view.getByRole('button',{name:'Retry delivery'});
    fireEvent.click(retry);fireEvent.click(retry);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith(
      '/api/shipments/7701/retry-delivery',
      {note:undefined,rowVersion:'AQ=='},
      {headers:{'Idempotency-Key':'key:shipment-retry-delivery-7701'}}
    );
  });

  it('renders tracking timeline and POD as read-only evidence',async()=>{
    const completed={
      ...detail,status:'Completed',dispatchedAt:'2026-10-06T03:40:00Z',inTransitAt:'2026-10-06T04:00:00Z',
      deliveredAt:'2026-10-06T05:00:00Z',completedAt:'2026-10-06T05:05:00Z',
      proofOfDelivery:{deliveredAt:'2026-10-06T05:00:00Z',receiverName:'Nguyễn Văn A',evidenceReference:'pod://proof-1',createdAt:'2026-10-06T05:00:01Z'},
      trackingEvents:[
        {id:1,eventType:'ShipmentInTransit',fromStatus:'Dispatched',toStatus:'InTransit',occurredAt:'2026-10-06T04:00:00Z',recordedAt:'2026-10-06T04:00:01Z',source:'Internal'},
        {id:2,eventType:'DeliveryConfirmed',fromStatus:'InTransit',toStatus:'Delivered',occurredAt:'2026-10-06T05:00:00Z',recordedAt:'2026-10-06T05:00:01Z',source:'Internal'},
        {id:3,eventType:'ShipmentCompleted',fromStatus:'Delivered',toStatus:'Completed',occurredAt:'2026-10-06T05:05:00Z',recordedAt:'2026-10-06T05:05:01Z',source:'Internal'},
      ],
    };
    reads(completed);
    grant('shipment.read');
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    expect(await view.findByText(/Shipment đã COMPLETED/)).toBeTruthy();
    expect(view.getByRole('table',{name:'Shipment tracking timeline'})).toBeTruthy();
    expect(view.getByText('DeliveryConfirmed')).toBeTruthy();
  });

  it('maps wrong-HU loading conflict to actionable Vietnamese copy',async()=>{
    grant('shipment.read','shipment.load','loading.execute');
    reads({...detail,status:'Loading',dockCode:'D-02',vehiclePlate:'50H-220.18',handlingUnits:[{...hu,status:'Staged'}]});
    vi.mocked(apiClient.post).mockRejectedValue({response:{status:409,data:{code:'SHIPMENT_HU_MISMATCH',message:'wrong'}}});
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    const input=await view.findByLabelText('Shipment HU scan');
    fireEvent.change(input,{target:{value:'OTHER-HU'}});
    fireEvent.click(view.getByText('Xác nhận HU đã load'));
    await waitFor(()=>expect(view.getByRole('alert').textContent).toContain('không thuộc Shipment'));
  });
});
