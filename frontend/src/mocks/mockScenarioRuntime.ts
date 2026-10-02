export interface MockScenarioRuntimeBalance {
  warehouse: string;
  location: string;
  productCode: string;
  onHand: number;
  reserved: number;
  allocated: number;
  picked: number;
  available: number | null;
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
  available: number | null,
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
        balance('WH-HCM-01', 'RETURN-DISPOSITION', 'SKU-1001', 20, null),
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
  'GS-07': {
    scenarioId: 'GS-07',
    affectedCapabilities: ['OUT-02', 'OUT-03', 'INV-04'],
    initialBalances: [
      balance('WH-HCM-01', 'ALLOC-POOL', 'SKU-1001', 10, 10),
    ],
    stepBalances: [
      [balance('WH-HCM-01', 'ALLOC-POOL', 'SKU-1001', 10, 10)],
      [balance('WH-HCM-01', 'ALLOC-POOL', 'SKU-1001', 10, 2, { reserved: 8 })],
      [balance('WH-HCM-01', 'ALLOC-POOL', 'SKU-1001', 10, 2, { reserved: 8 })],
    ],
  },
  'GS-08': {
    scenarioId: 'GS-08',
    affectedCapabilities: ['OUT-08', 'INV-02', 'IG-04'],
    initialBalances: [
      balance('WH-HCM-01', 'LOADED', 'SKU-1001', 20, 0, { picked: 20 }),
    ],
    stepBalances: [
      [balance('WH-HCM-01', 'DISPATCHED', 'SKU-1001', 0, 0)],
      [balance('WH-HCM-01', 'DISPATCHED', 'SKU-1001', 0, 0)],
      [balance('WH-HCM-01', 'DISPATCHED', 'SKU-1001', 0, 0)],
    ],
  },
  'GS-10': {
    scenarioId: 'GS-10',
    affectedCapabilities: ['MO-10', 'OP-05', 'IN-07', 'OUT-08'],
    initialBalances: [],
    stepBalances: [[], [], []],
  },
  'GS-13': {
    scenarioId: 'GS-13',
    affectedCapabilities: ['AX-13', 'QR-04', 'QR-06', 'INV-05'],
    initialBalances: [],
    stepBalances: [[], [], [], []],
  },
  'GS-14': {
    scenarioId: 'GS-14',
    affectedCapabilities: ['AX-19', 'QR-02', 'QR-03', 'INV-05'],
    initialBalances: [
      balance('WH-HCM-01', 'COLD-01', 'SKU-COLD-01', 100, 100),
    ],
    stepBalances: [
      [balance('WH-HCM-01', 'COLD-01', 'SKU-COLD-01', 100, 100)],
      [balance('WH-HCM-01', 'QUARANTINE-COLD', 'SKU-COLD-01', 100, 0, { quarantine: 100 })],
      [balance('WH-HCM-01', 'QA-DISPOSITION', 'SKU-COLD-01', 100, null)],
    ],
  },
  'GS-15': {
    scenarioId: 'GS-15',
    affectedCapabilities: ['AX-20', 'MD-04', 'IN-04', 'IN-07'],
    initialBalances: [],
    stepBalances: [[], [], []],
  },
  'GS-16': {
    scenarioId: 'GS-16',
    affectedCapabilities: ['AX-22', 'AX-05', 'IG-09', 'RP-08'],
    initialBalances: [],
    stepBalances: [[], [], [], []],
  },
  'GS-17': {
    scenarioId: 'GS-17',
    affectedCapabilities: ['AX-24', 'AX-31', 'TR-01', 'TR-03', 'TR-04'],
    initialBalances: [
      balance('WH-HCM-01', 'NETWORK-SOURCE', 'SKU-1001', 180, 180),
      balance('WH-DN-01', 'NETWORK-DEST', 'SKU-1001', 0, 0),
    ],
    stepBalances: [
      [
        balance('WH-HCM-01', 'NETWORK-SOURCE', 'SKU-1001', 180, 180),
        balance('WH-DN-01', 'NETWORK-DEST', 'SKU-1001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'NETWORK-SOURCE', 'SKU-1001', 180, 180),
        balance('WH-DN-01', 'NETWORK-DEST', 'SKU-1001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'NETWORK-SOURCE', 'SKU-1001', 0, 0, { inTransit: 180 }),
        balance('WH-DN-01', 'NETWORK-DEST', 'SKU-1001', 0, 0),
      ],
      [
        balance('WH-HCM-01', 'NETWORK-SOURCE', 'SKU-1001', 0, 0),
        balance('WH-DN-01', 'NETWORK-DEST', 'SKU-1001', 180, 180),
      ],
    ],
  },
  'GS-18': {
    scenarioId: 'GS-18',
    affectedCapabilities: ['AX-34', 'AD-04', 'IG-02', 'IN-01'],
    initialBalances: [],
    stepBalances: [[], [], [], []],
  },
  'GS-19': {
    scenarioId: 'GS-19',
    affectedCapabilities: ['IG-09', 'IG-10', 'RP-06', 'OP-02'],
    initialBalances: [],
    stepBalances: [[], [], [], []],
  },
  'GS-20': {
    scenarioId: 'GS-20',
    affectedCapabilities: ['OP-10', 'INV-09', 'IN-07', 'AD-04'],
    initialBalances: [
      balance('WH-HCM-01', 'REPAIR-TARGET', 'SKU-1001', 100, 100),
    ],
    stepBalances: [
      [balance('WH-HCM-01', 'REPAIR-TARGET', 'SKU-1001', 100, 100)],
      [balance('WH-HCM-01', 'REPAIR-TARGET', 'SKU-1001', 100, 100)],
      [balance('WH-HCM-01', 'REPAIR-TARGET', 'SKU-1001', 0, 0)],
      [balance('WH-HCM-01', 'REPAIR-TARGET', 'SKU-1001', 80, 80)],
    ],
  },
  'GS-21': {
    scenarioId: 'GS-21',
    affectedCapabilities: ['AX-15', 'OV-06', 'OUT-05', 'INV-02'],
    initialBalances: [],
    stepBalances: [[], [], [], []],
  },
  'GS-22': {
    scenarioId: 'GS-22',
    affectedCapabilities: ['AX-14', 'AX-18', 'OV-06', 'INV-08'],
    initialBalances: [],
    stepBalances: [[], [], [], []],
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
