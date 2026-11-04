// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useHardwareScannerWedge } from '../../hooks/useHardwareScannerWedge';
import {
  MobileBarcodeScannerBar,
  parseGs1BarcodeClient,
  playScannerErrorBuzz,
} from '../MobileBarcodeScannerBar';

function WedgeHarness({ onScan }: { onScan: (barcode: string) => void }) {
  useHardwareScannerWedge({ onScan, maxInterKeyIntervalMs: 35, minLength: 4 });
  return (
    <div>
      <button type="button" data-testid="non-editable-focus-target">
        Vùng sàn kho không phải ô nhập
      </button>
      <input type="text" data-testid="regular-text-input" placeholder="Ô nhập văn bản thường" />
      <textarea data-testid="regular-textarea" placeholder="Ô ghi chú thường" />
    </div>
  );
}

describe('TC-P1-02: Hardware Scanner Wedge, GS1-128 FNC1 Parser, Undo & Error Buzz', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    cleanup();
  });

  it('bắt trọn chuỗi keydown tốc độ cao (<= 35ms) kết thúc bằng Enter kèm ký tự FNC1 (\\x1d) khi không focus vào ô input', () => {
    const onScan = vi.fn();
    render(<WedgeHarness onScan={onScan} />);

    const buttonEl = screen.getByTestId('non-editable-focus-target');
    buttonEl.focus();
    expect(document.activeElement).toBe(buttonEl);

    const rawGs1Stream = '010893500123456710LOT99\x1d1727063021SER001';
    for (const ch of rawGs1Stream) {
      window.dispatchEvent(
        new KeyboardEvent('keydown', {
          key: ch,
          keyCode: ch === '\x1d' ? 29 : ch.charCodeAt(0),
          bubbles: true,
          cancelable: true,
        }),
      );
    }
    window.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'Enter',
        code: 'Enter',
        keyCode: 13,
        bubbles: true,
        cancelable: true,
      }),
    );

    expect(onScan).toHaveBeenCalledTimes(1);
    expect(onScan).toHaveBeenCalledWith(rawGs1Stream);
    expect(onScan.mock.calls[0][0]).toContain('\x1d');
  });

  it('bỏ qua không cướp phím khi người dùng đang focus trong <input> hoặc <textarea> thông thường', () => {
    const onScan = vi.fn();
    render(<WedgeHarness onScan={onScan} />);

    const inputEl = screen.getByTestId('regular-text-input') as HTMLInputElement;
    inputEl.focus();
    expect(document.activeElement).toBe(inputEl);

    for (const ch of 'BIN-A01-01') {
      window.dispatchEvent(
        new KeyboardEvent('keydown', { key: ch, bubbles: true, cancelable: true }),
      );
    }
    window.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
    );
    expect(onScan).not.toHaveBeenCalled();

    const textareaEl = screen.getByTestId('regular-textarea') as HTMLTextAreaElement;
    textareaEl.focus();
    expect(document.activeElement).toBe(textareaEl);

    for (const ch of 'LOT-MANUAL-01') {
      window.dispatchEvent(
        new KeyboardEvent('keydown', { key: ch, bubbles: true, cancelable: true }),
      );
    }
    window.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
    );
    expect(onScan).not.toHaveBeenCalled();
  });

  it('parseGs1BarcodeClient giải mã chính xác cả chuỗi GS1-128 có ngoặc đơn và chuỗi thô ngăn cách bằng FNC1 (\\x1d)', () => {
    const bracketed = parseGs1BarcodeClient('(01)08935001234567(10)LOT99(17)270630(21)SER001');
    expect(bracketed).not.toBeNull();
    expect(bracketed).toMatchObject({
      gtin: '08935001234567',
      lotNumber: 'LOT99',
      expiryDate: '2027-06-30',
      expiryDisplay: '30/06/2027',
      serialNumber: 'SER001',
    });

    const rawFnc1 = parseGs1BarcodeClient('010893500123456710LOT99\x1d1727063021SER001');
    expect(rawFnc1).not.toBeNull();
    expect(rawFnc1).toMatchObject({
      gtin: '08935001234567',
      lotNumber: 'LOT99',
      expiryDate: '2027-06-30',
      expiryDisplay: '30/06/2027',
      serialNumber: 'SER001',
    });
  });

  it('hỗ trợ nút Hoàn tác lần quét gần nhất (Undo) trên MobileBarcodeScannerBar và hàm playScannerErrorBuzz()', async () => {
    const vibrateMock = vi.fn(() => true);
    Object.defineProperty(window.navigator, 'vibrate', {
      value: vibrateMock,
      configurable: true,
      writable: true,
    });

    playScannerErrorBuzz();
    expect(vibrateMock).toHaveBeenCalledWith([200, 100, 200]);

    const onScan = vi.fn();
    const onUndoLastScan = vi.fn();

    render(
      <MobileBarcodeScannerBar
        resolveViaApi={false}
        onScan={onScan}
        onUndoLastScan={onUndoLastScan}
      />,
    );

    const undoBtn = screen.getByTestId('scanner-undo-last-btn') as HTMLButtonElement;
    expect(undoBtn.disabled).toBe(true);
    expect(undoBtn.textContent).toContain('Hoàn tác');
    expect(undoBtn.textContent).not.toContain('(Undo)');

    const searchInput = screen.getByRole('searchbox');
    fireEvent.change(searchInput, { target: { value: 'BIN-A-01' } });
    fireEvent.keyDown(searchInput, { key: 'Enter', code: 'Enter', keyCode: 13 });

    await waitFor(() => {
      expect(onScan).toHaveBeenCalledWith('BIN-A-01', null);
    });

    await waitFor(() => {
      expect(undoBtn.disabled).toBe(false);
      expect(undoBtn.textContent).toContain('(1)');
    });

    act(() => {
      fireEvent.click(undoBtn);
    });

    expect(onUndoLastScan).toHaveBeenCalledTimes(1);
    expect(onUndoLastScan).toHaveBeenCalledWith('BIN-A-01');
    expect(undoBtn.disabled).toBe(true);
    expect(screen.getByTestId('scanner-last-scan-feedback').textContent).toContain(
      'Đã hoàn tác lần quét gần nhất',
    );
  });
});
