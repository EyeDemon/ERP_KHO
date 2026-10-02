import { createContext, useContext, useState, type ReactNode } from 'react';
import { mockUsers, type MockUser } from '../mocks/erpWmsMockData';
import { getGoldenScenario } from '../mocks/erpWmsMockScenarios';
import { getMockScenarioRuntimeBalances, getMockScenarioRuntimeDefinition, type MockScenarioRuntimeBalance } from '../mocks/mockScenarioRuntime';

interface MockDemoContextValue {
  selectedUserCode: string;
  selectedUser: MockUser;
  allowedWarehouses: string[];
  setSelectedUserCode: (code: string) => void;
  canSeeWarehouse: (warehouseCode: string) => boolean;
  activeScenarioId: string | null;
  activeScenarioTitle: string | null;
  activeScenarioStep: number;
  scenarioBalances: MockScenarioRuntimeBalance[];
  scenarioTotalBalanceCount: number;
  scenarioHiddenBalanceCount: number;
  scenarioEventLog: Array<{ label: string; state: string; inventoryEffect: string }>;
  affectedScenarioCapabilities: string[];
  runScenarioStep: (scenarioId: string) => void;
  resetScenario: (scenarioId?: string) => void;
  clearScenario: () => void;
  isCapabilityInActiveScenario: (capabilityId: string) => boolean;
}

const defaultUser = mockUsers[0];

const MockDemoContext = createContext<MockDemoContextValue>({
  selectedUserCode: defaultUser.code,
  selectedUser: defaultUser,
  allowedWarehouses: defaultUser.warehouses,
  setSelectedUserCode: () => undefined,
  canSeeWarehouse: (warehouseCode) => defaultUser.warehouses.includes(warehouseCode),
  activeScenarioId: null,
  activeScenarioTitle: null,
  activeScenarioStep: 0,
  scenarioBalances: [],
  scenarioTotalBalanceCount: 0,
  scenarioHiddenBalanceCount: 0,
  scenarioEventLog: [],
  affectedScenarioCapabilities: [],
  runScenarioStep: () => undefined,
  resetScenario: () => undefined,
  clearScenario: () => undefined,
  isCapabilityInActiveScenario: () => false,
});

export const MockDemoProvider = ({ children }: { children: ReactNode }) => {
  const [selectedUserCode, setSelectedUserCode] = useState(defaultUser.code);
  const [activeScenarioId, setActiveScenarioId] = useState<string | null>(null);
  const [activeScenarioStep, setActiveScenarioStep] = useState(0);
  const selectedUser = mockUsers.find((user) => user.code === selectedUserCode) ?? defaultUser;
  const activeScenario = getGoldenScenario(activeScenarioId ?? '');
  const runtimeDefinition = getMockScenarioRuntimeDefinition(activeScenarioId);
  const canonicalScenarioBalances = activeScenarioId
    ? getMockScenarioRuntimeBalances(activeScenarioId, activeScenarioStep)
    : [];
  const scenarioBalances = canonicalScenarioBalances
    .filter((item) => selectedUser.warehouses.includes(item.warehouse));
  const scenarioTotalBalanceCount = canonicalScenarioBalances.length;
  const scenarioHiddenBalanceCount = scenarioTotalBalanceCount - scenarioBalances.length;
  const scenarioEventLog = activeScenario?.steps.slice(0, activeScenarioStep) ?? [];
  const affectedScenarioCapabilities = runtimeDefinition?.affectedCapabilities ?? [];

  const runScenarioStep = (scenarioId: string) => {
    const scenario = getGoldenScenario(scenarioId);
    if (!scenario) return;
    if (activeScenarioId !== scenarioId) {
      setActiveScenarioId(scenarioId);
      setActiveScenarioStep(Math.min(1, scenario.steps.length));
      return;
    }
    setActiveScenarioStep((current) => Math.min(current + 1, scenario.steps.length));
  };

  const resetScenario = (scenarioId?: string) => {
    if (scenarioId && scenarioId !== activeScenarioId) {
      setActiveScenarioId(scenarioId);
    }
    setActiveScenarioStep(0);
  };

  const clearScenario = () => {
    setActiveScenarioId(null);
    setActiveScenarioStep(0);
  };

  const value: MockDemoContextValue = {
    selectedUserCode: selectedUser.code,
    selectedUser,
    allowedWarehouses: selectedUser.warehouses,
    setSelectedUserCode,
    canSeeWarehouse: (warehouseCode: string) => selectedUser.warehouses.includes(warehouseCode),
    activeScenarioId,
    activeScenarioTitle: activeScenario?.title ?? null,
    activeScenarioStep,
    scenarioBalances,
    scenarioTotalBalanceCount,
    scenarioHiddenBalanceCount,
    scenarioEventLog,
    affectedScenarioCapabilities,
    runScenarioStep,
    resetScenario,
    clearScenario,
    isCapabilityInActiveScenario: (capabilityId: string) => affectedScenarioCapabilities.includes(capabilityId),
  };

  return <MockDemoContext.Provider value={value}>{children}</MockDemoContext.Provider>;
};

export const useMockDemo = () => useContext(MockDemoContext);
