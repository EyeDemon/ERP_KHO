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

const ScenarioProbe = () => {
  const demo = useMockDemo();
  const firstBalance = demo.scenarioBalances[0];
  return (
    <div>
      <span data-testid="scenario-id">{demo.activeScenarioId ?? 'NONE'}</span>
      <span data-testid="scenario-step">{demo.activeScenarioStep}</span>
      <span data-testid="scenario-onhand">{firstBalance?.onHand ?? 'NA'}</span>
      <span data-testid="scenario-linked">{String(demo.isCapabilityInActiveScenario('IN-07'))}</span>
      <span data-testid="scenario-visible">{demo.scenarioBalances.length}</span>
      <span data-testid="scenario-hidden">{demo.scenarioHiddenBalanceCount}</span>
      <button type="button" onClick={() => demo.runScenarioStep('GS-01')}>Run GS01</button>
      <button type="button" onClick={() => demo.runScenarioStep('GS-03')}>Run GS03</button>
      <button type="button" onClick={() => demo.setSelectedUserCode('U-DN-MGR')}>DN scope</button>
      <button type="button" onClick={() => demo.resetScenario('GS-01')}>Reset GS01</button>
    </div>
  );
};

describe('MockDemoContext', () => {
  afterEach(cleanup);

  it('shares scenario execution state and runtime balances across consumers', () => {
    const view = render(<MockDemoProvider><ScenarioProbe /></MockDemoProvider>);
    expect(view.getByTestId('scenario-id').textContent).toBe('NONE');
    fireEvent.click(view.getByText('Run GS01'));
    expect(view.getByTestId('scenario-id').textContent).toBe('GS-01');
    expect(view.getByTestId('scenario-step').textContent).toBe('1');
    expect(view.getByTestId('scenario-onhand').textContent).toBe('0');
    expect(view.getByTestId('scenario-linked').textContent).toBe('true');

    fireEvent.click(view.getByText('Run GS01'));
    expect(view.getByTestId('scenario-step').textContent).toBe('2');
    expect(view.getByTestId('scenario-onhand').textContent).toBe('100');

    fireEvent.click(view.getByText('Reset GS01'));
    expect(view.getByTestId('scenario-step').textContent).toBe('0');
    expect(view.getByTestId('scenario-onhand').textContent).toBe('0');
  });

  it('filters shared runtime balances by the active persona warehouse scope', () => {
    const view = render(<MockDemoProvider><ScenarioProbe /></MockDemoProvider>);
    fireEvent.click(view.getByText('Run GS03'));
    expect(view.getByTestId('scenario-id').textContent).toBe('GS-03');
    expect(view.getByTestId('scenario-visible').textContent).toBe('2');
    expect(view.getByTestId('scenario-hidden').textContent).toBe('0');

    fireEvent.click(view.getByText('DN scope'));
    expect(view.getByTestId('scenario-visible').textContent).toBe('1');
    expect(view.getByTestId('scenario-hidden').textContent).toBe('1');
  });

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
