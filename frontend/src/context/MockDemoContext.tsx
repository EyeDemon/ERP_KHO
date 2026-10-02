import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { mockUsers, type MockUser } from '../mocks/erpWmsMockData';

interface MockDemoContextValue {
  selectedUserCode: string;
  selectedUser: MockUser;
  allowedWarehouses: string[];
  setSelectedUserCode: (code: string) => void;
  canSeeWarehouse: (warehouseCode: string) => boolean;
}

const defaultUser = mockUsers[0];

const MockDemoContext = createContext<MockDemoContextValue>({
  selectedUserCode: defaultUser.code,
  selectedUser: defaultUser,
  allowedWarehouses: defaultUser.warehouses,
  setSelectedUserCode: () => undefined,
  canSeeWarehouse: (warehouseCode) => defaultUser.warehouses.includes(warehouseCode),
});

export const MockDemoProvider = ({ children }: { children: ReactNode }) => {
  const [selectedUserCode, setSelectedUserCode] = useState(defaultUser.code);
  const selectedUser = mockUsers.find((user) => user.code === selectedUserCode) ?? defaultUser;

  const value = useMemo<MockDemoContextValue>(() => ({
    selectedUserCode: selectedUser.code,
    selectedUser,
    allowedWarehouses: selectedUser.warehouses,
    setSelectedUserCode,
    canSeeWarehouse: (warehouseCode: string) => selectedUser.warehouses.includes(warehouseCode),
  }), [selectedUser]);

  return <MockDemoContext.Provider value={value}>{children}</MockDemoContext.Provider>;
};

export const useMockDemo = () => useContext(MockDemoContext);
