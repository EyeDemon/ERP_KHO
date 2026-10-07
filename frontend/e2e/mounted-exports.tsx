// Test-only entry; retain the real component through the real denied-request identity refresh.
import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { HashRouter, Routes, Route } from 'react-router-dom';
import Login from '../src/pages/Login';
import ExportReceipts from '../src/pages/ExportReceipts';
import apiClient from '../src/services/apiClient';
import { usePermission } from '../src/services/authorization';

export function MountedExports() {
  const canRead = usePermission('export_receipt.read');
  const [refreshing, setRefreshing] = useState(false);
  async function refresh() {
    setRefreshing(true);
    try { await apiClient.get('/api/permissions'); } catch { /* Expected reader denial refreshes /auth/me. */ }
    finally { setRefreshing(false); }
  }
  return <>
    <button disabled={refreshing} onClick={() => void refresh()}>Xác minh quyền truy cập</button>
    <p role="status">{canRead ? 'Có quyền đọc phiếu xuất' : 'Quyền đọc phiếu xuất đã bị thu hồi'}</p>
    <ExportReceipts />
  </>;
}
createRoot(document.getElementById('root')!).render(
  <HashRouter><Routes><Route path="/login" element={<Login />} /><Route path="/" element={<MountedExports />} /></Routes></HashRouter>,
);
