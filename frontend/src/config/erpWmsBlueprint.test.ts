import { describe, expect, it } from 'vitest';
import { erpWmsBlueprint } from './erpWmsBlueprint';

describe('ERP WMS blueprint registry', () => {
  it('keeps the complete module and capability registry unique', () => {
    expect(erpWmsBlueprint).toHaveLength(17);
    const moduleKeys = erpWmsBlueprint.map((module) => module.key);
    expect(new Set(moduleKeys).size).toBe(moduleKeys.length);

    const capabilities = erpWmsBlueprint.flatMap((module) => module.capabilities);
    expect(capabilities).toHaveLength(179);
    const ids = capabilities.map((capability) => capability.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('requires every capability to carry goal, surface, status and spec traceability', () => {
    for (const module of erpWmsBlueprint) {
      expect(module.name.trim().length).toBeGreaterThan(0);
      expect(module.description.trim().length).toBeGreaterThan(0);
      expect(module.capabilities.length).toBeGreaterThan(0);
      for (const capability of module.capabilities) {
        expect(capability.name.trim().length).toBeGreaterThan(0);
        expect(capability.goal.trim().length).toBeGreaterThan(0);
        expect(capability.spec.trim().length).toBeGreaterThan(0);
        expect(capability.spec).not.toMatch(/\d+\s*-\s*\d+/);
        expect(capability.surfaces.length).toBeGreaterThan(0);
        expect(['live', 'foundation', 'planned', 'optional']).toContain(capability.status);
        if (capability.route) {
          expect(capability.route.startsWith('/')).toBe(true);
          expect(capability.route.startsWith('/system-blueprint')).toBe(false);
        }
        if (capability.mockRoute) {
          expect(capability.mockRoute.startsWith('/system-blueprint/')).toBe(true);
        }
      }
    }
  });

  it('keeps production and Blueprint routes strictly separated', () => {
    const capabilities = erpWmsBlueprint.flatMap((module) => module.capabilities);
    const wh02 = capabilities.find((capability) => capability.id === 'WH-02');
    expect(wh02?.status).toBe('live');
    expect(wh02?.route).toBe('/warehouse-structure');
    expect(wh02?.mockRoute).toBe('/system-blueprint/warehouse-structure/WH-02/workbench');
    const wh03 = capabilities.find((capability) => capability.id === 'WH-03');
    expect(wh03?.status).toBe('live');
    expect(wh03?.route).toBe('/warehouse-structure');
    const wh04 = capabilities.find((capability) => capability.id === 'WH-04');
    expect(wh04?.status).toBe('live');
    expect(wh04?.route).toBe('/warehouse-map');
    const wh05 = capabilities.find((capability) => capability.id === 'WH-05');
    expect(wh05?.status).toBe('live');
    expect(wh05?.route).toBe('/warehouse-calendar');
    const wh06 = capabilities.find((capability) => capability.id === 'WH-06');
    expect(wh06?.status).toBe('live');
    expect(wh06?.route).toBe('/dock-yard');

    for (const capability of capabilities) {
      if (capability.route) expect(capability.route.startsWith('/system-blueprint')).toBe(false);
      if (capability.mockRoute) expect(capability.mockRoute.startsWith('/system-blueprint/')).toBe(true);
    }
  });

  it('keeps canonical audit additions explicit and mapped to primary specs', () => {
    const capabilities = erpWmsBlueprint.flatMap((module) => module.capabilities);
    const byId = new Map(capabilities.map((capability) => [capability.id, capability]));
    for (const id of ['OV-06', 'OV-07', 'MD-08', 'OUT-10', 'INV-11', 'RP-09', 'RP-10', 'AD-09', 'AD-10', 'AD-11', 'AD-12', 'AD-13', 'AD-14', 'IG-08', 'OP-08', 'OP-09', 'AX-08', 'AX-09', 'AX-10', 'AX-11', 'AX-12', 'AX-13', 'AX-14', 'AX-15', 'AX-16', 'AX-17', 'AX-18', 'AX-19', 'AX-20', 'AX-21', 'AX-22', 'AX-23', 'AX-24', 'AX-25', 'AX-26', 'AX-27', 'AX-28', 'AX-29', 'AX-30', 'AX-31', 'AX-32', 'AX-33', 'AX-34', 'AX-35', 'AX-36', 'AX-37', 'AX-38', 'AX-39', 'AX-40', 'AX-41', 'AX-42', 'MD-09', 'WH-07', 'IG-09', 'IG-10', 'OP-10', 'OP-11', 'OV-08', 'MO-11', 'MO-12']) {
      expect(byId.has(id)).toBe(true);
    }
    expect(byId.get('OUT-05')?.spec.split(/,\s*/)).toContain('36');
    expect(byId.get('INV-09')?.spec).toBe('32');
    expect(byId.get('INV-11')?.spec.split(/,\s*/)).toContain('82');
    expect(byId.get('TR-05')?.spec.split(/,\s*/)).toContain('44');
    expect(byId.get('HU-01')?.spec.split(/,\s*/)).toContain('37');
    expect(byId.get('DY-04')?.spec.split(/,\s*/)).toContain('47');
    expect(byId.get('AD-07')?.spec.split(/,\s*/)).toContain('61');
    expect(byId.get('AX-14')?.spec).toBe('249');
    expect(byId.get('AX-22')?.spec.split(/,\s*/)).toContain('246');
    expect(byId.get('RP-10')?.spec.split(/,\s*/)).toContain('250');
    expect(byId.get('AX-23')?.spec).toBe('89');
    expect(byId.get('AX-31')?.spec).toBe('189');
    expect(byId.get('AX-42')?.spec.split(/,\s*/)).toContain('203');
    expect(byId.get('IG-09')?.spec.split(/,\s*/)).toContain('154');
    expect(byId.get('IG-10')?.spec.split(/,\s*/)).toContain('171');
    expect(byId.get('OP-10')?.spec).toBe('127');
    expect(byId.get('OV-08')?.spec.split(/,\s*/)).toContain('83');
    expect(byId.get('MO-11')?.spec.split(/,\s*/)).toContain('95');
    expect(byId.get('MO-12')?.spec.split(/,\s*/)).toContain('71');
  });

  it('keeps merged production capabilities synchronized with the main integration branch', () => {
    const capabilities = erpWmsBlueprint.flatMap((module) => module.capabilities);
    const byId = new Map(capabilities.map((capability) => [capability.id, capability]));

    for (const id of [
      'MD-02', 'MD-03', 'MD-05',
      'IN-01', 'IN-03', 'IN-04', 'IN-05', 'IN-06', 'IN-07', 'IN-08',
      'OUT-01', 'OUT-02',
      'DY-01', 'DY-02', 'DY-03',
      'RP-06', 'AD-02',
    ]) {
      expect(byId.get(id)?.status).toBe('live');
      expect(byId.get(id)?.route).toBeTruthy();
    }

    expect(byId.get('IN-02')?.status).toBe('foundation');
    expect(byId.get('IN-02')?.route).toBe('/dock-yard');
    expect(byId.get('IN-02')?.goal).toMatch(/liên kết canonical.*PO\/ASN\/Receipt.*hoàn thiện/i);

    expect(byId.get('IN-06')?.status).toBe('live');
    expect(byId.get('QR-01')?.status).toBe('planned');
    expect(byId.get('QR-01')?.goal).toMatch(/Inbound QC.*IN-06.*chưa.*live/i);

    expect(byId.get('TR-02')?.status).toBe('foundation');
    expect(byId.get('TR-02')?.route).toBe('/stock-transfers');
    for (const id of ['TR-03', 'TR-04']) {
      expect(byId.get(id)?.status).toBe('live');
      expect(byId.get(id)?.route).toBe('/stock-transfers');
    }

    expect(byId.get('INV-11')?.status).toBe('foundation');
    expect(byId.get('INV-11')?.goal).toMatch(/read-only reconciliation.*controlled rebuild\/remediation.*chưa/i);
    expect(byId.get('OUT-01')?.goal).toMatch(/không đồng nghĩa Shipment LOADED\/DISPATCHED/i);
    expect(byId.get('OUT-03')?.status).toBe('foundation');
    expect(byId.get('OUT-03')?.route).toBe('/stock-allocations');
    expect(byId.get('OUT-03')?.goal).toMatch(/Location.*FEFO\/FIFO.*chưa/i);
    expect(byId.get('OUT-05')?.status).toBe('foundation');
    expect(byId.get('OUT-05')?.route).toBe('/picking-tasks');
    expect(byId.get('OUT-05')?.goal).toMatch(/scan location.*Short Pick.*không trừ OnHand.*Lot\/Serial.*chưa/i);
    expect(byId.get('OUT-08')?.status).toBe('planned');
    expect(byId.get('OUT-08')?.goal).toMatch(/Shipment state machine riêng/i);

    for (const id of ['OUT-03', 'OUT-05', 'OUT-06', 'OUT-07', 'OUT-08', 'OUT-09', 'OUT-10']) {
      expect(byId.get(id)?.status).not.toBe('live');
    }
  });

  it('keeps critical core modules in the blueprint', () => {
    const keys = new Set(erpWmsBlueprint.map((module) => module.key));
    for (const key of [
      'inbound',
      'outbound',
      'inventory-control',
      'transfer-replenishment',
      'count-adjustment',
      'quality-returns',
      'administration',
      'integration',
      'mobile',
      'operations-resilience',
    ]) {
      expect(keys.has(key)).toBe(true);
    }
  });
});
