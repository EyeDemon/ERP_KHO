export interface MockScenarioRuntimeBalance {
  warehouse: string;
  location: string;
  productCode: string;
  onHand: number;
  reserved: number;
  allocated: number;
  picked: number;
  available: number;
  qcHold: number;
  quarantine: number;
  inTransit: number;
}

export interface MockScenarioRuntimeDefinition {
  scenarioId: string;
  affectedCapabilities: string[];
  initialBalances: MockScenarioRuntimeBalance[];
  stepBalances: MockScenarioRuntimeBalance[][];
}

const balance = (
  warehouse: string,
  location: string,
  productCode: string,
  onHand: number,
  available: number,
  options: Partial<Omit<MockScenarioRuntimeBalance, 'warehouse' | 'location' | 'productCode' | 'onHand' | 'available'>> = {},
): MockScenarioRuntimeBalance => ({
  warehouse,
  location,
  productCode,
  onHand,
  available,
  reserved: options.reserved ?? 0,
  allocated: options.allocated ?? 0,
  picked: options.picked ?? 0,
  qcHold: options.qcHold ?? 0,
  quarantine: options.quarantine ?? 0,
  inTransit: options.inTransit ?? 0,
});

export const mockScenarioRuntimeDefinitions: Record<string, MockScenarioRuntimeDefinition> = {
  'GS-01': {
    scenarioId: 'GS-01',
    affectedCapabilities: ['IN-04', 'IN-07', 'IN-08', 'INV-01', 'INV-02'],
    initialBalances: [
      balance('WH-HCM-01', 'RECV-01', 'SKU-1001', 0, 0),
      balance('WH-HCM-01', 'A01-R02-L03-B04', 'SKU-1001', 0, 0),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'RECV-01', 'SKU-1001', 0, 0),
        balance('WH-HCM-01', 'A01-R02-L03-B04', 'SKU-1001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'RECV-01', 'SKU-1001', 100, 100),
        balance('WH-HCM-01', 'A01-R02-L03-B04', 'SKU-1001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'RECV-01', 'SKU-1001', 60, 60),
        balance('WH-HCM-01', 'A01-R02-L03-B04', 'SKU-1001', 40, 40),
      ],
      [
        balance('WH-HCM-01', 'RECV-01', 'SKU-1001', 0, 0),
        balance('WH-HCM-01', 'A01-R02-L03-B04', 'SKU-1001', 100, 100),
      ],
    ],
  },
  'GS-02': {
    scenarioId: 'GS-02',
    affectedCapabilities: ['OUT-02', 'OUT-03', 'OUT-05', 'OUT-08', 'INV-01', 'INV-04'],
    initialBalances: [
      balance('WH-HCM-01', 'PICK-A-01', 'SKU-1001', 100, 100),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'PICK-A-01', 'SKU-1001', 100, 70, { reserved: 30 }),
      ],
      [
        balance('WH-HCM-01', 'PICK-A-01', 'SKU-1001', 100, 70, { reserved: 30, allocated: 20 }),
      ],
      [
        balance('WH-HCM-01', 'PICK-A-01', 'SKU-1001', 100, 70, { reserved: 30, allocated: 20, picked: 20 }),
      ],
      [
        balance('WH-HCM-01', 'PICK-A-01', 'SKU-1001', 80, 70, { reserved: 10 }),
      ],
    ],
  },
  'GS-03': {
    scenarioId: 'GS-03',
    affectedCapabilities: ['TR-01', 'TR-02', 'TR-03', 'TR-04', 'INV-01'],
    initialBalances: [
      balance('WH-HCM-01', 'STORAGE-A', 'SKU-1002', 100, 100),
      balance('WH-DN-01', 'RECEIVING-DN', 'SKU-1002', 0, 0),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'STORAGE-A', 'SKU-1002', 100, 100),
        balance('WH-DN-01', 'RECEIVING-DN', 'SKU-1002', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'STORAGE-A', 'SKU-1002', 0, 0, { inTransit: 100 }),
        balance('WH-DN-01', 'RECEIVING-DN', 'SKU-1002', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'STORAGE-A', 'SKU-1002', 0, 0),
        balance('WH-DN-01', 'RECEIVING-DN', 'SKU-1002', 100, 100),
      ],
    ],
  },
  'GS-04': {
    scenarioId: 'GS-04',
    affectedCapabilities: ['CT-02', 'CT-03', 'CT-05', 'CT-06', 'CT-07', 'INV-01', 'INV-02'],
    initialBalances: [
      balance('WH-HCM-01', 'COUNT-A-01', 'SKU-2001', 84, 84),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'COUNT-A-01', 'SKU-2001', 84, 84),
      ],
      [
        balance('WH-HCM-01', 'COUNT-A-01', 'SKU-2001', 84, 84),
      ],
      [
        balance('WH-HCM-01', 'COUNT-A-01', 'SKU-2001', 84, 84),
      ],
      [
        balance('WH-HCM-01', 'COUNT-A-01', 'SKU-2001', 82, 82),
      ],
    ],
  },
  'GS-05': {
    scenarioId: 'GS-05',
    affectedCapabilities: ['IN-04', 'IN-06', 'IN-07', 'QR-01', 'QR-02', 'QR-03', 'INV-01', 'INV-05'],
    initialBalances: [
      balance('WH-HCM-01', 'QC-01', 'SKU-1002', 0, 0),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'QC-01', 'SKU-1002', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'QC-01', 'SKU-1002', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'AVAILABLE-STORAGE', 'SKU-1002', 80, 80),
        balance('WH-HCM-01', 'DAMAGED-HOLD', 'SKU-1002', 15, 0),
      ],
    ],
  },
  'GS-06': {
    scenarioId: 'GS-06',
    affectedCapabilities: ['IN-07', 'INV-02', 'INV-09', 'OP-10'],
    initialBalances: [
      balance('WH-HCM-01', 'RECV-01', 'SKU-1001', 0, 0),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'RECV-01', 'SKU-1001', 100, 100),
      ],
      [
        balance('WH-HCM-01', 'RECV-01', 'SKU-1001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'RECV-01', 'SKU-1001', 80, 80),
      ],
    ],
  },
  'GS-09': {
    scenarioId: 'GS-09',
    affectedCapabilities: ['QR-04', 'INV-01', 'INV-05', 'MO-09'],
    initialBalances: [
      balance('WH-HCM-01', 'RETURN-RECV', 'SKU-2001', 0, 0),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'RETURN-RECV', 'SKU-2001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'RETURN-RECV', 'SKU-2001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'RESTOCK', 'SKU-2001', 4, 4),
        balance('WH-HCM-01', 'QUARANTINE', 'SKU-2001', 2, 0, { quarantine: 2 }),
      ],
    ],
  },
  'GS-11': {
    scenarioId: 'GS-11',
    affectedCapabilities: ['OUT-10', 'QR-04', 'INV-01', 'INV-10'],
    initialBalances: [
      balance('WH-HCM-01', 'RETURN-RECV', 'SKU-1001', 0, 0),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'RETURN-RECV', 'SKU-1001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'RETURN-RECV', 'SKU-1001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'RETURN-RECV', 'SKU-1001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'RETURN-RECV', 'SKU-1001', 20, 20),
      ],
    ],
  },
  'GS-12': {
    scenarioId: 'GS-12',
    affectedCapabilities: ['OUT-05', 'OUT-06', 'OUT-07', 'OUT-08', 'HU-01', 'HU-02', 'INV-01'],
    initialBalances: [
      balance('WH-HCM-01', 'PICKED-STAGE', 'SKU-1001', 20, 0, { picked: 20 }),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'PICKED-STAGE', 'SKU-1001', 20, 0, { picked: 20 }),
      ],
      [
        balance('WH-HCM-01', 'PACK-02', 'SKU-1001', 20, 0, { picked: 20 }),
      ],
      [
        balance('WH-HCM-01', 'STAGE-OUT-03', 'SKU-1001', 20, 0, { picked: 20 }),
      ],
      [
        balance('WH-HCM-01', 'DISPATCHED', 'SKU-1001', 0, 0),
      ],
    ],
  },
};

export const getMockScenarioRuntimeDefinition = (scenarioId?: string | null) =>
  scenarioId ? mockScenarioRuntimeDefinitions[scenarioId] : undefined;

export const getMockScenarioRuntimeBalances = (scenarioId: string, executedSteps: number) => {
  const definition = getMockScenarioRuntimeDefinition(scenarioId);
  if (!definition) return [];
  if (executedSteps <= 0) return definition.initialBalances;
  return definition.stepBalances[Math.min(executedSteps, definition.stepBalances.length) - 1]
    ?? definition.initialBalances;
};
