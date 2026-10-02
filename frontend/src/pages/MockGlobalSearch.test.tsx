// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { MockDemoProvider } from '../context/MockDemoContext';
import MockGlobalSearch from './MockGlobalSearch';

describe('MockGlobalSearch', () => {
  afterEach(cleanup);

  it('resolves exact barcode and document examples', () => {
    const view = render(<MemoryRouter><MockDemoProvider><MockGlobalSearch /></MockDemoProvider></MemoryRouter>);
    const input = view.getByLabelText('Global search');
    fireEvent.change(input, { target: { value: '8938501000011' } });
    expect(view.getByText('EXACT')).toBeTruthy();
    expect(view.getByText(/SKU-1001/)).toBeTruthy();

    fireEvent.change(input, { target: { value: 'GR-2026-1048' } });
    expect(view.getAllByText('GR-2026-1048').length).toBeGreaterThan(0);
    expect(view.getAllByText('WH-HCM-01').length).toBeGreaterThan(0);
  });

  it('supports one-click example queries', () => {
    const view = render(<MemoryRouter><MockDemoProvider><MockGlobalSearch /></MockDemoProvider></MemoryRouter>);
    fireEvent.click(view.getByText('SUP-001'));
    expect(view.getAllByText('Công ty Nông Sản Cao Nguyên')).toHaveLength(1);
  });
});
