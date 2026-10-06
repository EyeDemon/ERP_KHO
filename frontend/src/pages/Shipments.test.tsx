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
const loaded={...detail,status:'Loaded',loadedHandlingUnitCount:1,dockCode:'D-02',vehiclePlate:'50H-220.18',sealNumber:'SEAL-01',loadedAt:'2026-10-06T03:30:00Z',handlingUnits:[{...hu,status:'Loaded',loadedAt:'2026-10-06T03:25:00Z'}]};
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

  it('hides dispatch without shipment.dispatch permission',async()=>{
    reads(loaded);
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    expect(await view.findByText(/Cần quyền shipment.dispatch/)).toBeTruthy();
    expect(view.queryByText('Xác nhận Dispatch')).toBeNull();
  });

  it('dispatches a loaded shipment once with idempotency',async()=>{
    grant('shipment.read','shipment.dispatch');
    reads(loaded);
    vi.mocked(apiClient.post).mockResolvedValue({data:{...loaded,status:'Dispatched',dispatchedAt:'2026-10-06T03:40:00Z',dispatchedBy:7,handlingUnits:[{...hu,status:'Shipped'}]}} as never);
    const view=render(<Shipments/>);
    await view.findByText('SHIP-2026-7701');
    fireEvent.click(view.getByText('SHIP-2026-7701'));
    const button=await view.findByText('Xác nhận Dispatch');
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith(
      '/api/shipments/7701/dispatch',
      {rowVersion:'AQ=='},
      {headers:{'Idempotency-Key':'key:shipment-dispatch-7701'}}
    );
    expect(completeIdempotentAction).toHaveBeenCalledWith('shipment-dispatch-7701');
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
