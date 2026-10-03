// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import { setCurrentPermissions } from '../services/authorization';
import ImportReceipts from './ImportReceipts';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));
const get = vi.mocked(apiClient.get);
const receipt = { id: 8, code: 'QA_RECEIPT', status: 'ReadyToPost', createdBy: 2, details: [] };
describe('quyền độc lập trên phiếu nhập', () => {
  beforeEach(() => {
    vi.resetAllMocks(); localStorage.clear(); localStorage.setItem('userId', '1');
    setCurrentPermissions(['receipt.read']);
    get.mockImplementation(async url => ({ data: url === '/api/importreceipts' ? [receipt] : receipt }) as never);
  });
  afterEach(cleanup);

  it('người chỉ thực hiện ghi được từng phần nhưng không gửi command hoàn tất kiểm tra', async () => {
    setCurrentPermissions(['receipt.read', 'quality_inspection.execute']);
    const qc = { ...receipt, status: 'QcPending', details: [1, 2].map(id => ({
      id, productId: id, productCode: `SP${id}`, productName: `Sản phẩm ${id}`, requiresQc: true,
      qcState: 'QcPending', expectedQuantity: 5, receivedQuantity: 5, acceptedQuantity: 0,
      damagedQuantity: 0, rejectedQuantity: 0, conversionFactor: 1, operationUnitCode: 'EA', baseUnitCode: 'EA',
    })) };
    get.mockImplementation(async url => ({ data: url === '/api/importreceipts' ? [qc] : qc }) as never);
    vi.mocked(apiClient.post).mockResolvedValue({ data: {} });
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    fireEvent.click(view.getByText('Chi tiết'));
    fireEvent.change(await view.findByLabelText('Kiểm tra chất lượng: chấp nhận SP1'), { target: { value: '5' } });
    fireEvent.change(view.getByLabelText('Kiểm tra chất lượng: chấp nhận SP2'), { target: { value: '5' } });
    fireEvent.click(view.getByText('Ghi nhận kết quả kiểm tra chất lượng'));
    expect(await view.findByText(/Bạn không có quyền hoàn tất kiểm tra chất lượng/)).toBeTruthy();
    expect(apiClient.post).not.toHaveBeenCalled();
    fireEvent.click(view.getByLabelText('Ghi kết quả kiểm tra SP2'));
    fireEvent.click(view.getByText('Ghi nhận kết quả kiểm tra chất lượng'));
    await waitFor(() => expect(apiClient.post).toHaveBeenCalledTimes(1));
    expect(vi.mocked(apiClient.post).mock.calls[0][1]).toEqual({ lines: [expect.objectContaining({ lineId: 1, acceptedQuantity: 5 })] });
  });

  it('duyệt phiếu đã kiểm tra cần cả quyền hoàn tất phiếu và duyệt kết quả, không cần thực hiện', async () => {
    const qc = { ...receipt, status: 'QcCompleted' };
    get.mockImplementation(async url => ({ data: url === '/api/importreceipts' ? [qc] : qc }) as never);
    setCurrentPermissions(['receipt.read', 'receipt.complete']);
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    expect(view.queryByText('Duyệt để ghi nhận tồn kho')).toBeNull();
    act(() => setCurrentPermissions(['receipt.read', 'receipt.complete', 'quality_disposition.approve']));
    expect(await view.findByText('Duyệt để ghi nhận tồn kho')).toBeTruthy();
    act(() => setCurrentPermissions(['receipt.read', 'quality_disposition.approve']));
    expect(view.queryByText('Duyệt để ghi nhận tồn kho')).toBeNull();
  });

  it('duyệt phiếu không kiểm tra không cần quyền kiểm tra chất lượng', async () => {
    const noQc = { ...receipt, status: 'Received' };
    get.mockImplementation(async url => ({ data: url === '/api/importreceipts' ? [noQc] : noQc }) as never);
    setCurrentPermissions(['receipt.read', 'receipt.complete']);
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    expect(view.getByText('Duyệt để ghi nhận tồn kho')).toBeTruthy();
  });

  it('receipt-only reader xem chi tiết mà không gọi dependency không có quyền', async () => {
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    fireEvent.click(view.getByText('Chi tiết'));
    expect(await view.findByText(/Chi Tiết Phiếu Nhập:/)).toBeTruthy();
    expect(get.mock.calls.every(([url]) => url.startsWith('/api/importreceipts') && !url.includes('discrepanc'))).toBe(true);
    expect(view.queryByText('Lưu Phiếu Nháp')).toBeNull();
    expect(view.queryByText('Ghi nhận tồn kho')).toBeNull();
  });

  it('optional dependency failure không xóa core receipt', async () => {
    setCurrentPermissions(['receipt.read', 'receiving_discrepancy.read', 'reason_code.read']);
    get.mockImplementation(async url => {
      if (url.includes('discrepanc')) throw { response: { status: 403 } };
      return { data: url === '/api/importreceipts' ? [receipt] : receipt } as never;
    });
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    fireEvent.click(view.getByText('Chi tiết'));
    expect(await view.findByText(/Chi Tiết Phiếu Nhập:/)).toBeTruthy();
  });

  it('receipt.complete không cấp quyền ghi tồn và receipt.post không cấp quyền tạo phiếu', async () => {
    setCurrentPermissions(['receipt.read', 'receipt.complete']);
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    expect(view.queryByText('Ghi nhận tồn kho')).toBeNull();
    act(() => setCurrentPermissions(['receipt.read', 'receipt.post']));
    expect(await view.findByText('Ghi nhận tồn kho')).toBeTruthy();
    expect(view.queryByText('Lưu Phiếu Nháp')).toBeNull();
  });

  it('response chi tiết đến sau revoke không khôi phục dữ liệu đã xóa', async () => {
    let finish!: (value: unknown) => void;
    get.mockImplementation(url => url === '/api/importreceipts' ? Promise.resolve({ data: [receipt] }) as never
      : new Promise(resolve => { finish = resolve; }) as never);
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    fireEvent.click(view.getByText('Chi tiết'));
    act(() => setCurrentPermissions([]));
    await act(async () => finish({ data: receipt }));
    await waitFor(() => expect(view.queryByText('QA_RECEIPT')).toBeNull());
    expect(view.queryByText(/Chi Tiết Phiếu Nhập:/)).toBeNull();
  });

  it('response danh sách cũ không ghi đè dữ liệu mới sau revoke rồi regrant', async () => {
    let finish!: (value: unknown) => void;
    get.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }) as never);
    const view = render(<ImportReceipts />);
    await waitFor(() => expect(get).toHaveBeenCalledWith('/api/importreceipts'));
    act(() => setCurrentPermissions([]));
    act(() => setCurrentPermissions(['receipt.read']));
    await view.findByText('QA_RECEIPT');
    await act(async () => finish({ data: [{ ...receipt, code: 'STALE_PRIVATE_RECEIPT' }] }));
    expect(view.queryByText('STALE_PRIVATE_RECEIPT')).toBeNull();
    expect(view.getByText('QA_RECEIPT')).toBeTruthy();
  });

  it('bản in đến muộn không mở lại sau revoke rồi regrant', async () => {
    let finish!: (value: unknown) => void;
    get.mockImplementation(url => url === '/api/importreceipts' ? Promise.resolve({ data: [receipt] }) as never
      : new Promise(resolve => { finish = resolve; }) as never);
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    fireEvent.click(view.getByText('Xem bản in'));
    act(() => setCurrentPermissions([]));
    act(() => setCurrentPermissions(['receipt.read']));
    await view.findByText('QA_RECEIPT');
    await act(async () => finish({ data: { ...receipt, code: 'STALE_PRIVATE_PRINT' } }));
    expect(view.queryByRole('dialog')).toBeNull();
    expect(view.queryByText('STALE_PRIVATE_PRINT')).toBeNull();
  });
});
