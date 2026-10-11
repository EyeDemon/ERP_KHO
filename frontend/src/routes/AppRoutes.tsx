import { Navigate, Routes, Route } from 'react-router-dom';
import type { ReactNode } from 'react';
import MainLayout from '../layouts/MainLayout';
import Dashboard from '../pages/Dashboard';
import NotFound from '../pages/NotFound';
import Login from '../pages/Login';
import Products from '../pages/Products';
import Warehouses from '../pages/Warehouses';
import Units from '../pages/Units';
import ExportReceipts from '../pages/ExportReceipts';
import ImportReceipts from '../pages/ImportReceipts';
import Inventory from '../pages/Inventory';
import Stocktakes from '../pages/Stocktakes';
import StockTransfers from '../pages/StockTransfers';
import StockReservations from '../pages/StockReservations';
import Approvals from '../pages/Approvals';
import BusinessPartners from '../pages/BusinessPartners';
import PutawayTasks from '../pages/PutawayTasks';
import Permissions from '../pages/Permissions';
import { canViewApprovals, canViewStocktakes, usePermission, usePermissionSet } from '../services/authorization';

const PermissionRoute = ({ permission, children }: { permission: string; children: ReactNode }) =>
  usePermission(permission) ? children : <p role="alert">Bạn không có quyền thực hiện thao tác này.</p>;

const StocktakeRoute = ({ children }: { children: ReactNode }) =>
  canViewStocktakes() ? children : <Navigate to="/" replace />;
const ApprovalRoute = () => {
  usePermissionSet();
  return canViewApprovals() ? <Approvals /> : <p role="alert">Bạn không có quyền thực hiện thao tác này.</p>;
};

const AppRoutes = () => {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/" element={<MainLayout />}>
        <Route index element={<Dashboard />} />
        <Route path="products" element={<PermissionRoute permission="product.read"><Products /></PermissionRoute>} />
        <Route path="warehouses" element={<PermissionRoute permission="warehouse.read"><Warehouses /></PermissionRoute>} />
        <Route path="units" element={<PermissionRoute permission="uom.read"><Units /></PermissionRoute>} />
        <Route path="business-partners" element={<PermissionRoute permission="partner.read"><BusinessPartners /></PermissionRoute>} />
        <Route path="export-receipts" element={<PermissionRoute permission="export_receipt.read"><ExportReceipts /></PermissionRoute>} />
        <Route path="import-receipts" element={<PermissionRoute permission="receipt.read"><ImportReceipts /></PermissionRoute>} />
        <Route path="inventory" element={<Inventory />} />
        <Route path="stocktakes" element={<StocktakeRoute><Stocktakes /></StocktakeRoute>} />
        <Route path="stock-transfers" element={<StockTransfers />} />
        <Route path="stock-reservations" element={<PermissionRoute permission="reservation.read"><StockReservations /></PermissionRoute>} />
        <Route path="approvals" element={<ApprovalRoute />} />
        <Route path="putaway-tasks" element={<PermissionRoute permission="putaway.read"><PutawayTasks /></PermissionRoute>} />
        <Route path="permissions" element={<PermissionRoute permission="permission.read"><Permissions /></PermissionRoute>} />
      </Route>
      <Route path="*" element={<NotFound />} />
    </Routes>
  );
};

export default AppRoutes;
