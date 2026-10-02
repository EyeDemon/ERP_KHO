import { describe, expect, it } from 'vitest';
import { mockGoldenScenarios } from './erpWmsMockScenarios';

const byId = (id: string) => {
  const scenario = mockGoldenScenarios.find((item) => item.id === id);
  if (!scenario) throw new Error(`Missing scenario ${id}`);
  return scenario;
};

describe('ERP WMS golden mock scenarios', () => {
  it('covers eight canonical golden scenarios plus returns and offline sync', () => {
    expect(mockGoldenScenarios).toHaveLength(10);
    expect(mockGoldenScenarios.slice(0, 8).map((item) => item.id)).toEqual(
      ['GS-01', 'GS-02', 'GS-03', 'GS-04', 'GS-05', 'GS-06', 'GS-07', 'GS-08'],
    );
  });

  it('posts inbound exactly once and keeps putaway warehouse-neutral', () => {
    const m = byId('GS-01').metrics;
    expect(m.initialOnHand).toBe(0);
    expect(m.postedQuantity).toBe(100);
    expect(m.finalOnHand).toBe(100);
    expect(Number(m.finalReceivingLocation) + Number(m.finalStorageLocation)).toBe(100);
  });

  it('keeps allocation inside reservation and deducts physical stock only on dispatch', () => {
    const m = byId('GS-02').metrics;
    expect(Number(m.allocated)).toBeLessThanOrEqual(Number(m.reserved));
    expect(m.availableAfterReserve).toBe(70);
    expect(Number(m.initialOnHand) - Number(m.dispatchQuantity)).toBe(m.finalOnHand);
  });

  it('conserves transfer quantity after dispatch and receive', () => {
    const m = byId('GS-03').metrics;
    expect(Number(m.afterDispatchSource) + Number(m.afterDispatchTransit)).toBe(m.requested);
    expect(Number(m.afterReceiveTransit) + Number(m.finalDestination)).toBe(m.requested);
  });

  it('creates count adjustment only from the accepted final count', () => {
    const m = byId('GS-04').metrics;
    expect(Number(m.finalAccepted) - Number(m.systemQty)).toBe(m.adjustment);
    expect(Number(m.systemQty) + Number(m.adjustment)).toBe(m.finalOnHand);
  });

  it('keeps Receive/QC ledger-neutral and posts canonical disposition only at receipt POST', () => {
    const m = byId('GS-05').metrics;
    expect(m.prePostOnHand).toBe(0);
    expect(Number(m.accepted) + Number(m.damaged) + Number(m.rejectedAtDoor)).toBe(m.received);
    expect(Number(m.available) + Number(m.damaged)).toBe(m.physicalOnHand);
    expect(Number(m.physicalOnHand) + Number(m.rejectedAtDoor)).toBe(m.received);
  });

  it('keeps reversal ledger history and resolves to the corrected net', () => {
    const m = byId('GS-06').metrics;
    expect(Number(m.original) + Number(m.reversal) + Number(m.correction)).toBe(m.net);
    expect(m.ledgerTransactions).toBe(3);
  });

  it('prevents concurrent over-reservation', () => {
    const m = byId('GS-07').metrics;
    expect(m.successCount).toBe(1);
    expect(m.conflictCount).toBe(1);
    expect(Number(m.finalReserved) + Number(m.finalAvailable)).toBe(m.initialAvailable);
  });

  it('deduplicates repeated dispatch requests by idempotency key', () => {
    const m = byId('GS-08').metrics;
    expect(m.requests).toBe(5);
    expect(m.committedDispatches).toBe(1);
    expect(m.ledgerRows).toBe(1);
    expect(m.balanceMutations).toBe(1);
    expect(m.outboxRows).toBe(1);
  });

  it('balances return disposition and blocks high-risk offline commands', () => {
    const returned = byId('GS-09').metrics;
    expect(Number(returned.sellable) + Number(returned.damaged)).toBe(returned.returned);
    expect(returned.posted).toBe(returned.returned);

    const offline = byId('GS-10').metrics;
    expect(offline.highRiskBlocked).toBe(1);
    expect(offline.dataLoss).toBe(false);
    expect(offline.serverRevalidation).toBe(true);
  });
});
