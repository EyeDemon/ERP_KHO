// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ReceiptInventoryIdentityEditor from './ReceiptInventoryIdentityEditor';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient',()=>({default:{get:vi.fn(),post:vi.fn()}}));
const {completeIdempotentAction}=vi.hoisted(()=>({completeIdempotentAction:vi.fn()}));
vi.mock('../services/idempotency',()=>({
  idempotencyHeaders:(key:string)=>({'Idempotency-Key':`key:${key}`}),
  completeIdempotentAction,
}));

const lotLine={
  id:11,productId:1,productCode:'SKU-LOT',productName:'Lot product',
  trackingType:'Lot' as const,expiryControl:true,shelfLifeDays:365,
  baseAcceptedQuantity:10,baseDamagedQuantity:0,baseRejectedQuantity:0,baseUnitCode:'EA'
};
const serialLine={
  id:12,productId:2,productCode:'SKU-SERIAL',productName:'Serial product',
  trackingType:'Serial' as const,expiryControl:false,shelfLifeDays:null,
  baseAcceptedQuantity:2,baseDamagedQuantity:0,baseRejectedQuantity:0,baseUnitCode:'EA'
};

describe('ReceiptInventoryIdentityEditor',()=>{
  beforeEach(()=>{
    vi.resetAllMocks();
    completeIdempotentAction.mockReset();
  });
  afterEach(cleanup);

  it('loads persisted identity read-only after receipt is ready to post',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:[{
      id:101,lineId:11,productId:1,targetStatus:'AVAILABLE',baseQuantity:10,
      lotNumber:'LOT-001',manufactureDate:'2026-09-01T00:00:00Z',expiryDate:'2027-09-01T00:00:00Z',serialNumber:null
    }]} as never);

    const view=render(<ReceiptInventoryIdentityEditor receiptId={88} status="ReadyToPost" lines={[lotLine]} canUpdate={true}/>);
    expect(await view.findByDisplayValue('LOT-001')).toBeTruthy();
    expect(view.getByText(/Identity đang ở chế độ chỉ đọc/)).toBeTruthy();
    expect(view.queryByText('Lưu Lot / Serial / Expiry')).toBeNull();
  });

  it('saves lot expiry identity idempotently while receipt is Received',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:[]} as never);
    vi.mocked(apiClient.post).mockImplementation(async (_url,body)=>({data:(body as {lines:unknown[]}).lines} as never));

    const view=render(<ReceiptInventoryIdentityEditor receiptId={88} status="Received" lines={[lotLine]} canUpdate={true}/>);
    await view.findByText('Chưa khai báo identity.');
    fireEvent.click(view.getByText('Thêm lot'));
    fireEvent.change(view.getByLabelText('Identity lot SKU-LOT 0'),{target:{value:'LOT-NEW'}});
    fireEvent.change(view.getByLabelText('Identity manufacture SKU-LOT 0'),{target:{value:'2026-09-01'}});
    fireEvent.change(view.getByLabelText('Identity expiry SKU-LOT 0'),{target:{value:'2027-09-01'}});
    const save=view.getByText('Lưu Lot / Serial / Expiry');
    fireEvent.click(save);fireEvent.click(save);

    await waitFor(()=>expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(apiClient.post).toHaveBeenCalledWith('/api/importreceipts/88/inventory-identities',{
      lines:[{
        lineId:11,targetStatus:'AVAILABLE',baseQuantity:10,lotNumber:'LOT-NEW',
        manufactureDate:'2026-09-01',expiryDate:'2027-09-01',serialNumber:null
      }]
    },{headers:{'Idempotency-Key':'key:receipt-inventory-identity-88'}});
    expect(completeIdempotentAction).toHaveBeenCalledWith('receipt-inventory-identity-88');
  });

  it('locks serial quantity to one per identity row',async()=>{
    vi.mocked(apiClient.get).mockResolvedValue({data:[]} as never);
    const view=render(<ReceiptInventoryIdentityEditor receiptId={89} status="Received" lines={[serialLine]} canUpdate={true}/>);
    await view.findByText('Chưa khai báo identity.');
    fireEvent.click(view.getByText('Thêm serial'));
    const quantity=view.getByLabelText('Identity quantity SKU-SERIAL 0') as HTMLInputElement;
    expect(quantity.value).toBe('1');
    expect(quantity.disabled).toBe(true);
    expect(view.getByLabelText('Identity serial SKU-SERIAL 0')).toBeTruthy();
  });
});
