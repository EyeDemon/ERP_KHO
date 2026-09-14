// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import ImportReceipts from './ImportReceipts';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));
const get = vi.mocked(apiClient.get);
const receipt = { id: 3, code: 'I3', status: 'Draft', warehouseName: 'Kho', createdAt: '2026-09-14T08:00:00Z', details: [] };

describe('receipt print loading and errors', () => {
  afterEach(cleanup);
  beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem('role', 'Admin'); localStorage.setItem('userId', '1'); });
  it('loads a fresh detail and clears preview data before a failed reload', async () => {
    get.mockImplementation(async url => ({ data: url === '/api/importreceipts' ? [receipt] : url === '/api/importreceipts/3' ? receipt : url === '/api/business-partners' ? { items: [] } : [] }) as never);
    const view = render(<ImportReceipts />); await view.findByText('I3');
    fireEvent.click(view.getByText('Xem bản in')); expect(view.getByText('Đang tải bản in...')).toBeTruthy();
    expect(await view.findByRole('dialog')).toBeTruthy(); fireEvent.click(view.getByText('Đóng'));
    get.mockImplementation(async url => { if (url === '/api/importreceipts/3') throw { response: { data: { message: 'Bị từ chối' } } }; return ({ data: url === '/api/importreceipts' ? [receipt] : url === '/api/business-partners' ? { items: [] } : [] }) as never; });
    fireEvent.click(view.getByText('Xem bản in')); await waitFor(() => expect(view.getByText('Bị từ chối')).toBeTruthy());
    expect(view.queryByRole('dialog')).toBeNull(); expect((view.getByText('Xem bản in') as HTMLButtonElement).disabled).toBe(false);
  });
});
