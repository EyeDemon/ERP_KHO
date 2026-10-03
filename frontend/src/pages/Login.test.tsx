// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import apiClient from '../services/apiClient';
import Login from './Login';

vi.mock('../services/apiClient', () => ({ default: { post: vi.fn() }, setAccessToken: vi.fn() }));
afterEach(cleanup);

it('liên kết nhãn đăng nhập tiếng Việt và không hiển thị lỗi kỹ thuật', async () => {
  vi.mocked(apiClient.post).mockRejectedValue({ response: { status: 500, data: { message: 'SQL provider private detail' } } });
  const view = render(<MemoryRouter><Login /></MemoryRouter>);
  fireEvent.change(view.getByLabelText('Tên đăng nhập'), { target: { value: 'qa-user' } });
  const password = view.getByLabelText('Mật khẩu');
  expect(password.getAttribute('type')).toBe('password');
  expect(password.getAttribute('autocomplete')).toBe('current-password');
  fireEvent.change(password, { target: { value: 'QA_TEST_INPUT_ONLY' } });
  fireEvent.submit(view.getByRole('button', { name: 'Đăng nhập' }).closest('form')!);
  expect((await view.findByRole('alert')).textContent).toBe('Đăng nhập thất bại. Vui lòng kiểm tra thông tin và thử lại.');
  expect(view.queryByText('SQL provider private detail')).toBeNull();
});
