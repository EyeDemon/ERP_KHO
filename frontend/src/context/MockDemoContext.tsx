import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
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

  const runScenarioStep = useCallback((scenarioId: string) => {
    const scenario = getGoldenScenario(scenarioId);
    if (!scenario) return;
    if (activeScenarioId !== scenarioId) {
      setActiveScenarioId(scenarioId);
      setActiveScenarioStep(Math.min(1, scenario.steps.length));
      return;
    }
    setActiveScenarioStep((current) => Math.min(current + 1, scenario.steps.length));
  }, [activeScenarioId]);

  const resetScenario = useCallback((scenarioId?: string) => {
    if (scenarioId && scenarioId !== activeScenarioId) {
      setActiveScenarioId(scenarioId);
    }
    setActiveScenarioStep(0);
  }, [activeScenarioId]);

  const clearScenario = useCallback(() => {
    setActiveScenarioId(null);
    setActiveScenarioStep(0);
  }, []);

  const canSeeWarehouse = useCallback(
    (warehouseCode: string) => selectedUser.warehouses.includes(warehouseCode),
    [selectedUser.warehouses],
  );
  const isCapabilityInActiveScenario = useCallback(
    (capabilityId: string) => affectedScenarioCapabilities.includes(capabilityId),
    [affectedScenarioCapabilities],
  );

  const value = useMemo<MockDemoContextValue>(() => ({
    selectedUserCode: selectedUser.code,
    selectedUser,
    allowedWarehouses: selectedUser.warehouses,
    setSelectedUserCode,
    canSeeWarehouse,
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
    isCapabilityInActiveScenario,
  }), [
    activeScenario?.title,
    activeScenarioId,
    activeScenarioStep,
    affectedScenarioCapabilities,
    canSeeWarehouse,
    clearScenario,
    isCapabilityInActiveScenario,
    resetScenario,
    runScenarioStep,
    scenarioBalances,
    scenarioEventLog,
    scenarioHiddenBalanceCount,
    scenarioTotalBalanceCount,
    selectedUser,
  ]);

  return <MockDemoContext.Provider value={value}>{children}</MockDemoContext.Provider>;
};

export const useMockDemo = () => useContext(MockDemoContext);
