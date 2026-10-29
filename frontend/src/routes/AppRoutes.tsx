import { Suspense, lazy, type ReactNode } from 'react';
import { Navigate, Routes, Route } from 'react-router-dom';
import MainLayout from '../layouts/MainLayout';
import { canViewApprovals, canViewStocktakes, hasPermission, usePermissionSet } from '../services/authorization';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';
import { UiCard, UiPage } from '../ui/ProductionUi';

const Dashboard = lazy(() => import('../pages/Dashboard'));
const NotFound = lazy(() => import('../pages/NotFound'));
const Login = lazy(() => import('../pages/Login'));
const Products = lazy(() => import('../pages/Products'));
const Categories = lazy(() => import('../pages/Categories'));
const Warehouses = lazy(() => import('../pages/Warehouses'));
const WarehouseLocations = lazy(() => import('../pages/WarehouseLocations'));
const WarehouseMap = lazy(() => import('../pages/WarehouseMap'));
const WarehouseCalendar = lazy(() => import('../pages/WarehouseCalendar'));
const DockYard = lazy(() => import('../pages/DockYard'));
const Units = lazy(() => import('../pages/Units'));
const ExportReceipts = lazy(() => import('../pages/ExportReceipts'));
const ImportReceipts = lazy(() => import('../pages/ImportReceipts'));
const PurchaseOrders = lazy(() => import('../pages/PurchaseOrders'));
const Asns = lazy(() => import('../pages/Asns'));
const Inventory = lazy(() => import('../pages/Inventory'));
const InventoryLocks = lazy(() => import('../pages/InventoryLocks'));
const InventoryMovements = lazy(() => import('../pages/InventoryMovements'));
const InventoryReversals = lazy(() => import('../pages/InventoryReversals'));
const InventoryTraceability = lazy(() => import('../pages/InventoryTraceability'));
const InventoryReconciliation = lazy(() => import('../pages/InventoryReconciliation'));
const Stocktakes = lazy(() => import('../pages/Stocktakes'));
const StockTransfers = lazy(() => import('../pages/StockTransfers'));
const StockReservations = lazy(() => import('../pages/StockReservations'));
const StockAllocations = lazy(() => import('../pages/StockAllocations'));
const PickingTasks = lazy(() => import('../pages/PickingTasks'));
const PackingSessions = lazy(() => import('../pages/PackingSessions'));
const Shipments = lazy(() => import('../pages/Shipments'));
const OutboundDemand = lazy(() => import('../pages/OutboundDemand'));
const Approvals = lazy(() => import('../pages/Approvals'));
const BusinessPartners = lazy(() => import('../pages/BusinessPartners'));
const PartnerDebts = lazy(() => import('../pages/PartnerDebts'));
const PutawayTasks = lazy(() => import('../pages/PutawayTasks'));
const Permissions = lazy(() => import('../pages/Permissions'));
const SaasBilling = lazy(() => import('../pages/SaasBilling'));
const CodReconciliation = lazy(() => import('../pages/CodReconciliation'));
const OnboardingWizard = lazy(() => import('../pages/OnboardingWizard'));
const HandoverManifests = lazy(() => import('../pages/HandoverManifests'));
const CostValuation = lazy(() => import('../pages/CostValuation'));

const AccessDenied = () => (
  <UiPage>
    <div role="alert">
      <UiCard title="Không có quyền truy cập">
        <p className="ui-muted-text">
          Tài khoản hiện tại chưa có quyền truy cập màn hình này. Vui lòng kiểm tra vai trò, phạm vi kho hoặc quyền được cấp.
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
  return (isReadOnlyDemoRuntime() || canViewStocktakes()) ? children : <Navigate to="/" replace />;
};
const ApprovalRoute = () => {
  usePermissionSet();
  return canViewApprovals() ? <Approvals /> : <AccessDenied />;
};

const AppRoutes = () => {
  return (
    <Suspense fallback={<div role="status" className="ui-page">Đang tải phân hệ...</div>}>
      <Routes>
        <Route path="/login" element={<Login />} />

        {/* Hệ thống vận hành thật (Production ERP WMS) */}
        <Route path="/" element={<MainLayout />}>
          <Route index element={<Dashboard />} />
          <Route path="dashboard" element={<Dashboard />} />
          <Route path="products" element={<PermissionRoute permission="product.read"><Products /></PermissionRoute>} />
          <Route path="categories" element={<PermissionRoute permission="product.read"><Categories /></PermissionRoute>} />
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
          <Route path="stock-transfers" element={<PermissionRoute permission="inventory.read"><StockTransfers /></PermissionRoute>} />
          <Route path="stock-reservations" element={<PermissionRoute permission="inventory.read"><StockReservations /></PermissionRoute>} />
          <Route path="stock-allocations" element={<PermissionRoute permission="allocation.read"><StockAllocations /></PermissionRoute>} />
          <Route path="picking-tasks" element={<PermissionRoute permission="picking.read"><PickingTasks /></PermissionRoute>} />
          <Route path="packing-sessions" element={<PermissionRoute permission="packing.read"><PackingSessions /></PermissionRoute>} />
          <Route path="shipments" element={<PermissionRoute permission="shipment.read"><Shipments /></PermissionRoute>} />
          <Route path="handover-manifests" element={<PermissionRoute permission="inventory.read"><HandoverManifests /></PermissionRoute>} />
          <Route path="backorders" element={<PermissionRoute permission={['backorder.read', 'sales_order.read']}><OutboundDemand /></PermissionRoute>} />
          <Route path="outbound-demand" element={<Navigate to="/backorders" replace />} />
          <Route path="cod-reconciliation" element={<PermissionRoute permission="inventory.cost.read"><CodReconciliation /></PermissionRoute>} />
          <Route path="cost-valuation" element={<PermissionRoute permission="inventory.cost.read"><CostValuation /></PermissionRoute>} />
          <Route path="misa-exports" element={<PermissionRoute permission="inventory.cost.read"><CostValuation /></PermissionRoute>} />
          <Route path="onboarding-wizard" element={<PermissionRoute permission="permission.read"><OnboardingWizard /></PermissionRoute>} />
          <Route path="saas-billing" element={<PermissionRoute permission="permission.read"><SaasBilling /></PermissionRoute>} />
          <Route path="approvals" element={<ApprovalRoute />} />
          <Route path="putaway-tasks" element={<PermissionRoute permission="putaway.read"><PutawayTasks /></PermissionRoute>} />
          <Route path="permissions" element={<PermissionRoute permission="permission.read"><Permissions /></PermissionRoute>} />
          <Route path="users" element={<Navigate to="/permissions" replace />} />
          <Route path="roles" element={<Navigate to="/permissions" replace />} />
          <Route path="warehouse-scopes" element={<Navigate to="/permissions" replace />} />
        </Route>
        <Route path="*" element={<NotFound />} />
      </Routes>
    </Suspense>
  );
};

export default AppRoutes;
