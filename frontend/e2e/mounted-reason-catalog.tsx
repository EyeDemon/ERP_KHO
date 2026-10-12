// Test-only: retain the unchanged catalog while the real 403 interceptor refreshes identity.
import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { HashRouter, Routes, Route } from 'react-router-dom';
import Login from '../src/pages/Login';
import ReceivingReasonCodes from '../src/pages/ReceivingReasonCodes';
import apiClient from '../src/services/apiClient';
import { usePermission } from '../src/services/authorization';
export function MountedCatalog() {
  const canRead=usePermission('reason_code.read'), [refreshing,setRefreshing]=useState(false);
  async function refresh(){setRefreshing(true);try{await apiClient.get('/api/permissions');}catch{/* Real denied request refreshes permissions. */}finally{setRefreshing(false);}}
  return <><button disabled={refreshing} onClick={()=>void refresh()}>Xác minh quyền truy cập</button><p role="status">{canRead?'Có quyền đọc danh mục':'Quyền đọc danh mục đã bị thu hồi'}</p><ReceivingReasonCodes/></>;
}
createRoot(document.getElementById('root')!).render(<HashRouter><Routes><Route path="/login" element={<Login/>}/><Route path="/" element={<MountedCatalog/>}/></Routes></HashRouter>);
