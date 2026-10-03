// Test-only host: keep the unchanged receipt component mounted across a real 403 identity refresh.
// Production PermissionRoute/MainLayout deliberately unmount it; this is component-in-browser evidence.
import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { HashRouter, Routes, Route } from 'react-router-dom';
import Login from '../src/pages/Login';
import ImportReceipts from '../src/pages/ImportReceipts';
import apiClient from '../src/services/apiClient';
import { usePermission } from '../src/services/authorization';

export function MountedReceipts() {
  const canRead = usePermission('receipt.read');
  const [refreshing, setRefreshing] = useState(false);
  async function refresh() {
    setRefreshing(true);
    // This reader lacks permission.read. The production 403 interceptor reloads /auth/me;
    // no synthetic permission event, mocked identity or production test hook is involved.
    try { await apiClient.get('/api/permissions'); } catch { /* Expected denied catalog read. */ }
    finally { setRefreshing(false); }
  }
  return <>
    <button disabled={refreshing} onClick={() => void refresh()}>Xác minh quyền truy cập</button>
    <p role="status">{canRead ? 'Có quyền đọc phiếu' : 'Quyền đọc phiếu đã bị thu hồi'}</p>
    <ImportReceipts />
  </>;
}

createRoot(document.getElementById('root')!).render(
  <HashRouter><Routes><Route path="/login" element={<Login />} /><Route path="/" element={<MountedReceipts />} /></Routes></HashRouter>,
);
