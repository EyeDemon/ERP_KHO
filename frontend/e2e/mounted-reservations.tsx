// Test-only entry: keep the real reservation component mounted during permission refresh.
import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { HashRouter, Routes, Route } from 'react-router-dom';
import Login from '../src/pages/Login';
import StockReservations from '../src/pages/StockReservations';
import apiClient from '../src/services/apiClient';
import { usePermission } from '../src/services/authorization';
export function MountedReservations() {
  const canRead=usePermission('reservation.read');const [refreshing,setRefreshing]=useState(false);
  async function refresh(){setRefreshing(true);try{await apiClient.get('/api/permissions');}catch{/* Expected denied request refreshes the real permission context. */}finally{setRefreshing(false);}}
  return <><button disabled={refreshing} onClick={()=>void refresh()}>Xác minh quyền truy cập</button><p role="status">{canRead?'Có quyền đọc giữ hàng':'Quyền đọc giữ hàng đã bị thu hồi'}</p><StockReservations/></>;
}
createRoot(document.getElementById('root')!).render(<HashRouter><Routes><Route path="/login" element={<Login/>}/><Route path="/" element={<MountedReservations/>}/></Routes></HashRouter>);
