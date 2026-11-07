// @vitest-environment jsdom
import React, { useState } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import {
  UiVnDateInput,
  isoToVnDate,
  isValidVnCalendarDate,
  vnDateToIso,
} from './UiVnDateInput';

const ControlledDateInput: React.FC<{
  mode?: 'date' | 'datetime';
  verbalPrefix?: string;
  initialValue?: string;
}> = ({ mode = 'date', verbalPrefix, initialValue = '' }) => {
  const [val, setVal] = useState(initialValue);
  return (
    <UiVnDateInput
      testId="test-vn-date-input"
      mode={mode}
      verbalPrefix={verbalPrefix}
      value={val}
      onChange={setVal}
      ariaLabel="Ngày kiểm thử"
    />
  );
};

describe('UiVnDateInput (Wave-16 UIX-02)', () => {
  afterEach(cleanup);

  it('renders zero native date/datetime-local inputs and masks digits smoothly into dd/MM/yyyy', () => {
    const view = render(<ControlledDateInput mode="date" verbalPrefix="Hiệu lực đến" />);

    expect(view.container.querySelectorAll('input[type="date"]').length).toBe(0);
    expect(view.container.querySelectorAll('input[type="datetime-local"]').length).toBe(0);

    const input = screen.getByTestId('test-vn-date-input') as HTMLInputElement;
    expect(input.type).toBe('text');
    expect(input.placeholder).toBe('dd/MM/yyyy');
    expect(input.getAttribute('lang')).toBe('vi');
    expect(screen.getByTestId('vn-date-verbal-helper').textContent).toBe(
      'Định dạng: Ngày/Tháng/Năm (VD: 31/12/2026)',
    );

    const trigger = screen.getByLabelText('Mở lịch chọn ngày');
    expect(trigger.textContent?.trim()).toBe('');

    fireEvent.change(input, { target: { value: '02112027' } });
    expect(input.value).toBe('02/11/2027');
    expect(screen.getByTestId('vn-date-verbal-helper').textContent).toBe(
      'Hiệu lực đến: Ngày 02 tháng 11 năm 2027',
    );
  });

  it('masks dd/MM/yyyy HH:mm in datetime mode and renders verbal Vietnamese confirmation', () => {
    const view = render(<ControlledDateInput mode="datetime" verbalPrefix="Thời điểm ghi có" />);

    expect(view.container.querySelectorAll('input[type="date"]').length).toBe(0);
    expect(view.container.querySelectorAll('input[type="datetime-local"]').length).toBe(0);

    const input = screen.getByTestId('test-vn-date-input') as HTMLInputElement;
    expect(input.placeholder).toBe('dd/MM/yyyy HH:mm');
    fireEvent.change(input, { target: { value: '021120261815' } });
    expect(input.value).toBe('02/11/2026 18:15');
    expect(screen.getByTestId('vn-date-verbal-helper').textContent).toBe(
      'Thời điểm ghi có: 18:15 — Ngày 02 tháng 11 năm 2026',
    );
  });

  it('flags invalid Gregorian calendar dates such as 31/02/2026 with role="alert"', () => {
    render(<ControlledDateInput mode="date" verbalPrefix="Hiệu lực đến" />);

    const input = screen.getByTestId('test-vn-date-input') as HTMLInputElement;
    fireEvent.change(input, { target: { value: '31/02/2026' } });

    const helper = screen.getByTestId('vn-date-verbal-helper');
    expect(helper.getAttribute('role')).toBe('alert');
    expect(helper.textContent).toBe('Ngày không hợp lệ theo lịch dương (dd/MM/yyyy)');
    expect(isValidVnCalendarDate('31/02/2026', 'date')).toBe(false);
    expect(isValidVnCalendarDate('28/02/2026', 'date')).toBe(true);
  });

  it('opens Vietnamese calendar popover, supports day & quick-button selection, and closes on Escape', () => {
    render(<ControlledDateInput mode="date" verbalPrefix="Hiệu lực đến" initialValue="15/11/2026" />);

    const trigger = screen.getByLabelText('Mở lịch chọn ngày');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');

    fireEvent.click(trigger);
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    const popover = screen.getByTestId('vn-date-popover');
    expect(popover.textContent).toContain('Tháng 11 / 2026');
    expect(popover.textContent).toContain('T2');
    expect(popover.textContent).toContain('CN');

    fireEvent.click(screen.getByRole('button', { name: 'Cuối năm 2026' }));
    const input = screen.getByTestId('test-vn-date-input') as HTMLInputElement;
    expect(input.value).toBe('31/12/2026');
    expect(screen.getByTestId('vn-date-verbal-helper').textContent).toBe(
      'Hiệu lực đến: Ngày 31 tháng 12 năm 2026',
    );
    expect(screen.queryByTestId('vn-date-popover')).toBeNull();

    fireEvent.click(trigger);
    expect(screen.getByTestId('vn-date-popover')).toBeTruthy();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByTestId('vn-date-popover')).toBeNull();
  });

  it('converts cleanly between ISO and Vietnamese date/datetime strings', () => {
    expect(isoToVnDate('2026-12-31')).toBe('31/12/2026');
    expect(isoToVnDate('2026-11-02T18:15')).toBe('02/11/2026 18:15');
    expect(vnDateToIso('31/12/2026')).toBe('2026-12-31');
    expect(vnDateToIso('02/11/2026 18:15')).toBe('2026-11-02T18:15');
  });
});
