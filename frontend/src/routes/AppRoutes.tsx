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
import { canViewStocktakes, currentRole } from '../services/authorization';

const StocktakeRoute = ({ children }: { children: ReactNode }) =>
  canViewStocktakes(currentRole()) ? children : <Navigate to="/" replace />;

const AppRoutes = () => {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/" element={<MainLayout />}>
        <Route index element={<Dashboard />} />
        <Route path="products" element={<Products />} />
        <Route path="warehouses" element={<Warehouses />} />
        <Route path="units" element={<Units />} />
        <Route path="export-receipts" element={<ExportReceipts />} />
        <Route path="import-receipts" element={<ImportReceipts />} />
        <Route path="inventory" element={<Inventory />} />
        <Route path="stocktakes" element={<StocktakeRoute><Stocktakes /></StocktakeRoute>} />
        <Route path="stock-transfers" element={<StockTransfers />} />
        <Route path="stock-reservations" element={<StockReservations />} />
        <Route path="approvals" element={<Approvals />} />
      </Route>
      <Route path="*" element={<NotFound />} />
    </Routes>
  );
};

export default AppRoutes;
