// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MockDemoProvider, useMockDemo } from './MockDemoContext';

const Probe = () => {
  const demo = useMockDemo();
  return (
    <div>
      <span>{demo.selectedUser.code}</span>
      <span>{demo.selectedUser.role}</span>
      <span>{demo.allowedWarehouses.join(',')}</span>
      <span>{String(demo.canSeeWarehouse('WH-HCM-01'))}</span>
      <span>{String(demo.canSeeWarehouse('WH-DN-01'))}</span>
      <button type="button" onClick={() => demo.setSelectedUserCode('U-DN-MGR')}>DN</button>
    </div>
  );
};

describe('MockDemoContext', () => {
  afterEach(cleanup);

  it('defaults to the admin persona and switches warehouse scope deterministically', () => {
    const view = render(<MockDemoProvider><Probe /></MockDemoProvider>);
    expect(view.getByText('U-ADMIN')).toBeTruthy();
    expect(view.getByText('Admin')).toBeTruthy();
    expect(view.getAllByText('true')).toHaveLength(2);

    fireEvent.click(view.getByText('DN'));
    expect(view.getByText('U-DN-MGR')).toBeTruthy();
    expect(view.getByText('Warehouse Manager')).toBeTruthy();
    expect(view.getByText('WH-DN-01')).toBeTruthy();
    expect(view.getByText('false')).toBeTruthy();
  });
});
