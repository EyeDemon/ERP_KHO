import { Navigate, Routes, Route } from 'react-router-dom';
import type { ReactNode } from 'react';
import MainLayout from '../layouts/MainLayout';
import BlueprintLayout from '../layouts/BlueprintLayout';
import Dashboard from '../pages/Dashboard';
import NotFound from '../pages/NotFound';
import Login from '../pages/Login';
import Products from '../pages/Products';
import Warehouses from '../pages/Warehouses';
import WarehouseStructure from '../pages/WarehouseStructure';
import WarehouseLocations from '../pages/WarehouseLocations';
import WarehouseMap from '../pages/WarehouseMap';
import WarehouseCalendar from '../pages/WarehouseCalendar';
import DockYard from '../pages/DockYard';
import Units from '../pages/Units';
import ExportReceipts from '../pages/ExportReceipts';
import ImportReceipts from '../pages/ImportReceipts';
import PurchaseOrders from '../pages/PurchaseOrders';
import Asns from '../pages/Asns';
import Inventory from '../pages/Inventory';
import InventoryLocks from '../pages/InventoryLocks';
import InventoryMovements from '../pages/InventoryMovements';
import InventoryReversals from '../pages/InventoryReversals';
import InventoryTraceability from '../pages/InventoryTraceability';
import InventoryReconciliation from '../pages/InventoryReconciliation';
import Stocktakes from '../pages/Stocktakes';
import StockTransfers from '../pages/StockTransfers';
import StockReservations from '../pages/StockReservations';
import StockAllocations from '../pages/StockAllocations';
import PickingTasks from '../pages/PickingTasks';
import PackingSessions from '../pages/PackingSessions';
import Shipments from '../pages/Shipments';
import OutboundDemand from '../pages/OutboundDemand';
import Approvals from '../pages/Approvals';
import BusinessPartners from '../pages/BusinessPartners';
import PartnerDebts from '../pages/PartnerDebts';
import PutawayTasks from '../pages/PutawayTasks';
import Permissions from '../pages/Permissions';
import SystemBlueprint from '../pages/SystemBlueprint';
import ModuleBlueprint from '../pages/ModuleBlueprint';
import MockScenarioLab from '../pages/MockScenarioLab';
import MockDataLab from '../pages/MockDataLab';
import MockGlobalSearch from '../pages/MockGlobalSearch';
import CapabilityPreview from '../pages/CapabilityPreview';
import SystemCoverage from '../pages/SystemCoverage';
import { MockDemoProvider } from '../context/MockDemoContext';
import { canViewApprovals, canViewStocktakes, hasPermission, usePermissionSet } from '../services/authorization';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';
import { UiCard, UiPage } from '../ui/ProductionUi';

const AccessDenied = () => (
  <UiPage>
    <div role="alert">
      <UiCard title="Không có quyền truy cập">
        <p className="ui-muted-text">
          Tài khoản hiện tại chưa có quyền đọc màn hình này. Hãy kiểm tra vai trò, warehouse scope hoặc permission grant.
        </p>
      </UiCard>
    </div>
  </UiPage>
);

const PermissionRoute = ({ permission, children }: { permission: string | string[]; children: ReactNode }) => {
  usePermissionSet();
  const allowed = Array.isArray(permission)
    ? permission.some(code => hasPermission(code))
    : hasPermission(permission);
  return allowed ? children : <AccessDenied />;
};

const StocktakeRoute = ({ children }: { children: ReactNode }) => {
  usePermissionSet();
  return (isBlueprintDemoRuntime() || canViewStocktakes()) ? children : <Navigate to="/" replace />;
};
const ApprovalRoute = () => {
  usePermissionSet();
  return canViewApprovals() ? <Approvals /> : <AccessDenied />;
};

const AppRoutes = () => {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />

      {/* Hệ thống thiết kế / Mô phỏng độc lập (Tách biệt hoàn toàn khỏi Hệ thống thật) */}
      <Route path="/system-blueprint" element={<MockDemoProvider><BlueprintLayout /></MockDemoProvider>}>
        <Route index element={<SystemBlueprint />} />
        <Route path="scenarios" element={<MockScenarioLab />} />
        <Route path="mock-data" element={<MockDataLab />} />
        <Route path="search" element={<MockGlobalSearch />} />
        <Route path="coverage" element={<SystemCoverage />} />
        <Route path="warehouse-structure/WH-02/workbench" element={<WarehouseStructure />} />
        <Route path=":moduleKey/:capabilityId" element={<CapabilityPreview />} />
        <Route path=":moduleKey" element={<ModuleBlueprint />} />
      </Route>

      {/* Hệ thống vận hành thật (Production ERP WMS) */}
      <Route path="/" element={<MainLayout />}>
        <Route index element={<Dashboard />} />
        <Route path="products" element={<PermissionRoute permission="product.read"><Products /></PermissionRoute>} />
        <Route path="warehouses" element={<PermissionRoute permission="warehouse.read"><Warehouses /></PermissionRoute>} />
        <Route path="warehouse-structure" element={<PermissionRoute permission="location.read"><WarehouseLocations /></PermissionRoute>} />
        <Route path="warehouse-locations" element={<Navigate to="/warehouse-structure" replace />} />
        <Route path="warehouse-map" element={<PermissionRoute permission="location.read"><WarehouseMap /></PermissionRoute>} />
        <Route path="warehouse-calendar" element={<PermissionRoute permission="warehouse.read"><WarehouseCalendar /></PermissionRoute>} />
        <Route path="dock-yard" element={<PermissionRoute permission="dock_appointment.read"><DockYard /></PermissionRoute>} />
        <Route path="units" element={<PermissionRoute permission="uom.read"><Units /></PermissionRoute>} />
        <Route path="business-partners" element={<PermissionRoute permission="partner.read"><BusinessPartners /></PermissionRoute>} />
        <Route path="partner-debts" element={<PermissionRoute permission="partner.read"><PartnerDebts /></PermissionRoute>} />
        <Route path="export-receipts" element={<PermissionRoute permission="export_receipt.read"><ExportReceipts /></PermissionRoute>} />
        <Route path="purchase-orders" element={<PermissionRoute permission="purchase_order.read"><PurchaseOrders /></PermissionRoute>} />
        <Route path="asns" element={<PermissionRoute permission="asn.read"><Asns /></PermissionRoute>} />
        <Route path="import-receipts" element={<PermissionRoute permission="receipt.read"><ImportReceipts /></PermissionRoute>} />
        <Route path="inventory" element={<PermissionRoute permission="inventory.read"><Inventory /></PermissionRoute>} />
        <Route path="inventory-locks" element={<PermissionRoute permission="inventory_lock.read"><InventoryLocks /></PermissionRoute>} />
        <Route path="inventory-movements" element={<PermissionRoute permission="inventory.read"><InventoryMovements /></PermissionRoute>} />
        <Route path="inventory-reversals" element={<PermissionRoute permission="inventory_ledger.read"><InventoryReversals /></PermissionRoute>} />
        <Route path="inventory-traceability" element={<PermissionRoute permission="inventory_traceability.read"><InventoryTraceability /></PermissionRoute>} />
        <Route path="inventory-reconciliation" element={<PermissionRoute permission="inventory_ledger.read"><InventoryReconciliation /></PermissionRoute>} />
        <Route path="stocktakes" element={<StocktakeRoute><Stocktakes /></StocktakeRoute>} />
        <Route path="stock-transfers" element={<StockTransfers />} />
        <Route path="stock-reservations" element={<StockReservations />} />
        <Route path="stock-allocations" element={<PermissionRoute permission="allocation.read"><StockAllocations /></PermissionRoute>} />
        <Route path="picking-tasks" element={<PermissionRoute permission="picking.read"><PickingTasks /></PermissionRoute>} />
        <Route path="packing-sessions" element={<PermissionRoute permission="packing.read"><PackingSessions /></PermissionRoute>} />
        <Route path="shipments" element={<PermissionRoute permission="shipment.read"><Shipments /></PermissionRoute>} />
        <Route path="backorders" element={<PermissionRoute permission={['backorder.read', 'sales_order.read']}><OutboundDemand /></PermissionRoute>} />
        <Route path="outbound-demand" element={<Navigate to="/backorders" replace />} />
        <Route path="approvals" element={<ApprovalRoute />} />
        <Route path="putaway-tasks" element={<PermissionRoute permission="putaway.read"><PutawayTasks /></PermissionRoute>} />
        <Route path="permissions" element={<PermissionRoute permission="permission.read"><Permissions /></PermissionRoute>} />
      </Route>
      <Route path="*" element={<NotFound />} />
    </Routes>
  );
};

export default AppRoutes;
