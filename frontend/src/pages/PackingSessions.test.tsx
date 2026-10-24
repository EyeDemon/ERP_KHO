// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PackingSessions from './PackingSessions';
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

const line={pickingTaskLineId:41,productId:1,productCode:'SKU-1001',productName:'Cà phê',pickedQuantity:10,packedQuantity:2,remainingQuantity:8};
const hu={id:91,huCode:'HU-001',barcode:'HU-001',warehouseId:1,packingSessionId:77,type:'Carton',status:'InUse',createdAt:'2026-10-06T02:00:00Z',rowVersion:'HU1',contents:[{id:1,pickingTaskLineId:41,productId:1,productCode:'SKU-1001',productName:'Cà phê',quantity:2,packedAt:'2026-10-06T02:01:00Z'}]};
const summary={id:77,sessionCode:'PACK-2026-0077',pickingTaskId:88,pickingTaskCode:'PICK-2026-0088',sourceType:'ExportReceipt',sourceId:5108,sourceCode:'EX-2026-5108',warehouseId:1,warehouseName:'DC Hồ Chí Minh',status:'InProgress',requiredQuantity:10,packedQuantity:2,remainingQuantity:8,handlingUnitCount:1,createdAt:'2026-10-06T02:00:00Z'};
const detail={...summary,rowVersion:'AQ==',lines:[line],handlingUnits:[hu]};
const grant=(...permissions:string[])=>{permissionState.granted.clear();permissions.forEach(p=>permissionState.granted.add(p));};
const reads=(task:Record<string,unknown>=detail)=>vi.mocked(apiClient.get).mockImplementation(async url=>({data:url==='/api/packing-sessions'?[summary]:task}) as never);

describe('Packing workbench',()=>{
  afterEach(cleanup);
  beforeEach(()=>{vi.resetAllMocks();completeIdempotentAction.mockReset();grant('packing.read');reads()});

  it('shows packing evidence read-only without mutation permissions',async()=>{
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');
    fireEvent.click(view.getByText('PACK-2026-0077'));
    expect(await view.findByText(/Quản lý tạo kiện đóng gói/)).toBeTruthy();
    expect(view.queryByText('Xác nhận đóng gói')).toBeNull();
    expect(view.queryByText('Tạo kiện')).toBeNull();
  });

  it('submits scan-first pack once with idempotency',async()=>{
    grant('packing.read','packing.execute');
    vi.mocked(apiClient.post).mockResolvedValue({data:{...detail,packedQuantity:4,remainingQuantity:6}} as never);
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');
    fireEvent.click(view.getByText('PACK-2026-0077'));
    await view.findByLabelText('HU đích SKU-1001');
    fireEvent.change(view.getByLabelText('HU đích SKU-1001'),{target:{value:'91'}});
    fireEvent.change(view.getByLabelText('Product barcode packing SKU-1001'),{target:{value:'SKU-1001'}});
    fireEvent.change(view.getByLabelText('Packing quantity SKU-1001'),{target:{value:'2'}});
    const button=view.getByText('Xác nhận đóng gói');
    fireEvent.click(button);fireEvent.click(button);
    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/packing-sessions/77/pack',{
      handlingUnitId:91,pickingTaskLineId:41,productBarcode:'SKU-1001',quantity:2,rowVersion:'AQ=='
    },{headers:{'Idempotency-Key':'key:packing-pack-77-41'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('packing-pack-77-41');
  });

  it('maps over-pack conflict to actionable Vietnamese copy',async()=>{
    grant('packing.read','packing.execute');
    vi.mocked(apiClient.post).mockRejectedValue({response:{status:409,data:{code:'PACK_QUANTITY_EXCEEDS_PICKED',message:'wrong'}}});
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');
    fireEvent.click(view.getByText('PACK-2026-0077'));
    await view.findByLabelText('HU đích SKU-1001');
    fireEvent.change(view.getByLabelText('HU đích SKU-1001'),{target:{value:'91'}});
    fireEvent.change(view.getByLabelText('Product barcode packing SKU-1001'),{target:{value:'SKU-1001'}});
    fireEvent.change(view.getByLabelText('Packing quantity SKU-1001'),{target:{value:'9'}});
    fireEvent.click(view.getByText('Xác nhận đóng gói'));
    await waitFor(()=>expect(view.getByRole('alert').textContent).toContain('vượt số lượng đã Picking'));
  });

  it('shows HU create and structure controls only with canonical permissions',async()=>{
    grant('packing.read','handling_unit.create','handling_unit.modify');
    reads({
      ...detail,
      packedQuantity:0,
      remainingQuantity:10,
      lines:[{...line,packedQuantity:0,remainingQuantity:10}],
      handlingUnits:[{...hu,status:'Open',contents:[]}],
    });
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');
    fireEvent.click(view.getByText('PACK-2026-0077'));
    expect(await view.findByText('Tạo kiện')).toBeTruthy();
    expect(view.getByText('Hủy HU rỗng')).toBeTruthy();
  });

  it('renders mobile 2-line nowrap route cells with →, right padding >= 8px, and vertical label stacking',async()=>{
    grant('packing.read','packing.execute');
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');
    const sessionCodeBtn=view.getByText('PACK-2026-0077');
    expect(sessionCodeBtn.style.whiteSpace).toBe('nowrap');
    expect(view.getByText('1 kiện')).toBeTruthy();
    const routeSourceSpan=view.getByText('PICK-2026-0088');
    const routeTargetSpan=view.getByText('→ EX-2026-5108');
    expect(routeSourceSpan.tagName).toBe('SPAN');
    expect(routeSourceSpan.style.whiteSpace).toBe('nowrap');
    expect(routeTargetSpan.tagName).toBe('SPAN');
    expect(routeTargetSpan.style.whiteSpace).toBe('nowrap');
    const progressHeader=view.getByRole('columnheader',{name:'Tiến độ'});
    expect(progressHeader.style.paddingRight).toBe('10px');
    fireEvent.click(sessionCodeBtn);
    const huSelect=await view.findByLabelText('HU đích SKU-1001');
    expect(huSelect.closest('label')?.style.flexDirection).toBe('column');
    expect(huSelect.closest('.ui-subcard')).toBeTruthy();
    expect(view.getByTestId('unified-packing-station-card').style.gridTemplateColumns).toBe('repeat(2, minmax(0, 1fr))');
    expect(view.getByText('Mã QR vận đơn')).toBeTruthy();
    expect(view.queryByText('QR GS1')).toBeNull();
  });

  it('records packing video evidence with SHA-256 hash and searches video evidence by AWB code',async()=>{
    vi.mocked(apiClient.post).mockResolvedValueOnce({
      data:{
        id:301,
        sessionId:77,
        sessionCode:'PACK-2026-0077',
        awbCode:'SPXVN058291042',
        durationSeconds:48,
        fileSizeMb:14.6,
        sha256Hash:'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
        cameraStation:'CAM-PACK-01',
      }
    } as never);
    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');

    fireEvent.click(view.getByRole('button',{name:'Ghi nhận Video Đóng gói & Ký số SHA-256'}));
    expect(await view.findByText(/Đã lưu bằng chứng video đóng gói cho mã vận đơn SPXVN058291042/)).toBeTruthy();
    expect(apiClient.post).toHaveBeenCalledWith(
      '/api/v1/packing-sessions/video-evidence',
      expect.objectContaining({
        sessionId:77,
        awbCode:'SPXVN058291042',
        cameraStation:'CAM-PACK-01',
      }),
    );
    expect(view.getByRole('table',{name:'Danh sách bằng chứng video đóng gói'})).toBeTruthy();

    fireEvent.change(view.getByLabelText('Tra cứu video theo mã vận đơn AWB'),{target:{value:'SPXVN058291042'}});
    fireEvent.click(view.getByRole('button',{name:'Tra cứu Video theo Mã vận đơn (AWB)'}));
    await waitFor(()=>expect(apiClient.get).toHaveBeenCalledWith('/api/v1/packing-sessions/video-evidence',{params:{awbCode:'SPXVN058291042'}}));
  });

  it('calculates 3D cartonization & DIM weight and executes DEKIT_DISASSEMBLE kitting mode',async()=>{
    vi.mocked(apiClient.post).mockImplementation(async(url:string)=>{
      if(url==='/api/v1/packing-sessions/77/recommend-carton'){
        return {
          data:{
            sessionId:77,
            recommendedCartonCode:'CARTON-M',
            recommendedCartonName:'Thùng Tiêu chuẩn M (30x25x20cm)',
            recommendedDimensionsCm:'30x25x20',
            totalItemWeightKg:1.2,
            dimWeightKg:3.0,
            billableWeightKg:3.0,
            dimDivisor:5000,
            isDimWeightCharged:true,
            dimWeightWarning:'CẢNH BÁO CƯỚC THỂ TÍCH (DIM): Cước vận chuyển bị tính theo Trọng lượng Quy đổi 3,00 kg (> Trọng lượng thực 1,20 kg).',
          }
        } as never;
      }
      if(url==='/api/packing-sessions/execute-kitting-vas'){
        return {
          data:{
            vasExecutionCode:'VAS-DEKIT-2026-001',
            status:'DEKIT_DISASSEMBLED',
            statusLabel:'Đã xả bộ KIT trả tồn thành phần về kệ',
            vasServiceType:'DEKIT_DISASSEMBLE',
            activePackingSessionsCount:1,
            handlingUnitsCount:1,
          }
        } as never;
      }
      return {data:{}} as never;
    });

    const view=render(<PackingSessions/>);
    await view.findByText('PACK-2026-0077');

    fireEvent.click(view.getByTestId('btn-recommend-carton-3d'));
    expect(await view.findByTestId('dim-weight-warning-badge')).toBeTruthy();
    expect(view.getByTestId('unified-carton-suggestion').textContent).toContain('Thùng Tiêu chuẩn M (30x25x20cm)');
    expect(view.getByTestId('unified-carton-suggestion').textContent).toContain('Cảnh báo Cước Quy Đổi');

    const modeSelect=view.getByTestId('select-kitting-vas-mode');
    fireEvent.change(modeSelect,{target:{value:'DEKIT_DISASSEMBLE'}});
    fireEvent.click(view.getByRole('button',{name:'Thực thi Đóng bộ / Tách bộ & Dịch vụ Gia tăng'}));

    await waitFor(()=>{
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/packing-sessions/execute-kitting-vas',
        expect.objectContaining({
          vasMode:'DEKIT_DISASSEMBLE',
          vasServiceType:'DEKIT_DISASSEMBLE',
        }),
        expect.anything(),
      );
    });
    expect(await view.findByText(/VAS-DEKIT-2026-001/)).toBeTruthy();
    expect(view.getByText(/Đã xả bộ KIT trả tồn thành phần về kệ/)).toBeTruthy();
  });
});
