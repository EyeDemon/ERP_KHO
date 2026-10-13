// @vitest-environment jsdom
import { cleanup, render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import ReceiptQcBasis from './ReceiptQcBasis';

afterEach(cleanup);

describe('căn cứ kiểm tra chất lượng đã lưu', () => {
  it('mở/thu gọn bằng bàn phím, giữ focus và hiển thị phiên bản nghiệp vụ', async () => {
    const user = userEvent.setup();
    const view = render(<ReceiptQcBasis requiresQc qcPolicySource="ProductSupplier" qcPolicyVersion={3} qcPolicyEffectiveAtUtc="2026-09-20T16:00:00Z"/>);
    const button = view.getByRole('button');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    await user.tab(); await user.keyboard('{Enter}');
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(view.getByText('Chính sách theo sản phẩm và nhà cung cấp')).toBeTruthy();
    expect(view.getByText('Phiên bản 3')).toBeTruthy();
    expect(view.getByText(/20\/9\/2026.*UTC/)).toBeTruthy();
    await user.keyboard(' ');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(button);
  });

  it.each(['None', 'Product', 'ProductSupplier'])('quyết định không yêu cầu được lưu với nguồn %s', async source => {
    const view = render(<ReceiptQcBasis requiresQc={false} qcPolicySource={source}/>);
    await userEvent.click(view.getByRole('button'));
    expect(view.getByText('Không yêu cầu kiểm tra chất lượng')).toBeTruthy();
  });

  it.each([false, true])('legacy thiếu căn cứ không suy ra quyết định từ boolean %s', async requiresQc => {
    const view = render(<ReceiptQcBasis requiresQc={requiresQc}/>);
    await userEvent.click(view.getByRole('button'));
    expect(view.getByText('Chưa có thông tin căn cứ đã lưu')).toBeTruthy();
    expect(view.queryByText('Không yêu cầu kiểm tra chất lượng')).toBeNull();
    expect(view.queryByText('Yêu cầu kiểm tra chất lượng')).toBeNull();
  });

  it('nguồn sai lệch không tự bổ sung phiên bản/thời điểm từ master', async () => {
    const view = render(<ReceiptQcBasis requiresQc qcPolicySource="DiscrepancyRouteToQc"/>);
    await userEvent.click(view.getByRole('button'));
    expect(view.getByText('Chuyển sang kiểm tra chất lượng do sai lệch nhận hàng')).toBeTruthy();
    expect(view.getByText('Chưa có thông tin phiên bản đã lưu')).toBeTruthy();
    expect(view.getByText('Chưa có thông tin thời điểm hiệu lực đã lưu')).toBeTruthy();
    expect(view.queryByText('DiscrepancyRouteToQc')).toBeNull();
  });

  it('timestamp không múi giờ giữ nguyên các trường ngày giờ, không gán UTC', async () => {
    const view = render(<ReceiptQcBasis requiresQc qcPolicySource="Product" qcPolicyEffectiveAtUtc="2026-09-20T16:00:00"/>);
    await userEvent.click(view.getByRole('button'));
    expect(view.getByText('20/09/2026 16:00:00 (chưa có thông tin múi giờ trong dữ liệu trả về)')).toBeTruthy();
    expect(view.queryByText(/\(UTC\)/)).toBeNull();
  });

  it.each(['InternalUnknown', 'constructor'])('nguồn lạ %s/thời điểm lỗi không lộ enum hoặc technical message', async source => {
    const view = render(<ReceiptQcBasis requiresQc qcPolicySource={source} qcPolicyEffectiveAtUtc="badZ"/>);
    await userEvent.click(view.getByRole('button'));
    expect(view.getByText('Chưa có thông tin nguồn căn cứ đã lưu')).toBeTruthy();
    expect(view.getByText('Chưa có thông tin thời điểm hiệu lực hợp lệ')).toBeTruthy();
    expect(view.queryByText(source)).toBeNull();
  });
});
