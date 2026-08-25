import { useState, useEffect } from 'react';
import apiClient from '../services/apiClient';

interface ImportReceiptDetail {
  id: number;
  productId: number;
  productCode: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  note: string;
}

interface ImportReceipt {
  id: number;
  code: string;
  warehouseId: number;
  warehouseName: string;
  status: string;
  note: string;
  createdBy: number;
  createdByName: string;
  createdAt: string;
  approvedBy: number;
  approvedByName: string;
  approvedAt: string;
  details: ImportReceiptDetail[];
}

interface Warehouse {
  id: number;
  name: string;
}

interface Product {
  id: number;
  name: string;
  code: string;
}

interface ReceiptDetailForm {
  productId: number | '';
  quantity: number | '';
  unitPrice: number | '';
  note: string;
}

const ImportReceipts = () => {
  const [receipts, setReceipts] = useState<ImportReceipt[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');
  const [selectedReceipt, setSelectedReceipt] = useState<ImportReceipt | null>(null);
  const [approvingId, setApprovingId] = useState<number | null>(null);

  // Form states
  const [code, setCode] = useState('');
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [details, setDetails] = useState<ReceiptDetailForm[]>([]);

  const fetchData = async () => {
    try {
      const [whRes, prRes] = await Promise.all([
        apiClient.get('/api/warehouses'),
        apiClient.get('/api/products')
      ]);
      setWarehouses(whRes.data);
      setProducts(prRes.data);
    } catch (err: any) {
      console.error(err);
      setError('Lỗi khi tải dữ liệu khởi tạo');
    }
  };

  const fetchReceipts = async () => {
    try {
      const res = await apiClient.get('/api/importreceipts');
      setReceipts(res.data);
    } catch (err: any) {
      console.error(err);
      setError('Lỗi khi tải danh sách phiếu nhập');
    }
  };

  useEffect(() => {
    fetchData();
    fetchReceipts();
  }, []);

  const handleApprove = async (id: number) => {
    if (approvingId === id) return;
    setApprovingId(id);
    setError('');
    try {
      await apiClient.post(`/api/importreceipts/${id}/approve`);
      alert('Duyệt thành công');
      await fetchReceipts();
      if (selectedReceipt?.id === id) {
        await handleViewDetails(id);
      }
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi duyệt phiếu');
    } finally {
      setApprovingId(null);
    }
  };

  const handleCancel = async (id: number) => {
    if (!window.confirm('Bạn có chắc chắn muốn hủy phiếu nhập này?')) return;
    try {
      await apiClient.put(`/api/importreceipts/${id}/cancel`);
      alert('Hủy thành công');
      fetchReceipts();
      if (selectedReceipt?.id === id) {
          handleViewDetails(id);
      }
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi hủy phiếu');
    }
  };

  const handleViewDetails = async (id: number) => {
    try {
      const res = await apiClient.get(`/api/importreceipts/${id}`);
      setSelectedReceipt(res.data);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tải chi tiết phiếu');
    }
  };

  const handleAddDetail = () => {
    setDetails([...details, { productId: '', quantity: '', unitPrice: '', note: '' }]);
  };

  const handleRemoveDetail = (index: number) => {
    setDetails(details.filter((_, i) => i !== index));
  };

  const handleDetailChange = (index: number, field: keyof ReceiptDetailForm, value: any) => {
    const newDetails = [...details];
    newDetails[index] = { ...newDetails[index], [field]: value };
    setDetails(newDetails);
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccessMsg('');

    // Validation
    if (!code.trim()) return setError('Vui lòng nhập mã phiếu');
    if (warehouseId === '') return setError('Vui lòng chọn kho');
    if (details.length === 0) return setError('Cần ít nhất 1 dòng chi tiết');

    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn sản phẩm`);
      if (d.quantity === '' || Number(d.quantity) <= 0) return setError(`Dòng ${i + 1}: Số lượng phải > 0`);
      if (d.unitPrice === '' || Number(d.unitPrice) < 0) return setError(`Dòng ${i + 1}: Đơn giá phải >= 0`);
    }

    try {
      const payload = {
        code: code.trim(),
        warehouseId: Number(warehouseId),
        note,
        details: details.map(d => ({
          productId: Number(d.productId),
          quantity: Number(d.quantity),
          unitPrice: Number(d.unitPrice),
          note: d.note
        }))
      };

      await apiClient.post('/api/importreceipts', payload);
      setSuccessMsg('Tạo phiếu nháp thành công!');
      
      // Reset form
      setCode('');
      setWarehouseId('');
      setNote('');
      setDetails([]);
      
      fetchReceipts();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tạo phiếu nhập');
    }
  };

  return (
    <div>
      <h2>Quản Lý Nhập Kho</h2>
      {error && <div style={{ color: 'red', marginBottom: '10px' }}>{error}</div>}
      {successMsg && <div style={{ color: 'green', marginBottom: '10px' }}>{successMsg}</div>}
      
      <div style={{ marginBottom: '30px', padding: '15px', border: '1px solid #ccc', borderRadius: '5px' }}>
        <h3>Tạo Phiếu Nhập (Nháp)</h3>
        <form onSubmit={handleCreate}>
          <div style={{ display: 'flex', gap: '15px', marginBottom: '15px' }}>
            <div>
              <label style={{ display: 'block' }}>Mã phiếu</label>
              <input value={code} onChange={e => setCode(e.target.value)} required />
            </div>
            <div>
              <label style={{ display: 'block' }}>Kho</label>
              <select value={warehouseId} onChange={e => setWarehouseId(e.target.value ? Number(e.target.value) : '')} required>
                <option value="">-- Chọn kho --</option>
                {warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
              </select>
            </div>
            <div>
              <label style={{ display: 'block' }}>Ghi chú phiếu</label>
              <input value={note} onChange={e => setNote(e.target.value)} />
            </div>
          </div>

          <h4>Chi tiết phiếu</h4>
          {details.map((d, i) => (
            <div key={i} style={{ display: 'flex', gap: '10px', marginBottom: '10px', alignItems: 'center' }}>
              <select 
                value={d.productId} 
                onChange={e => handleDetailChange(i, 'productId', e.target.value ? Number(e.target.value) : '')}
                required
              >
                <option value="">-- Chọn sản phẩm --</option>
                {products.map(p => <option key={p.id} value={p.id}>{p.code} - {p.name}</option>)}
              </select>
              
              <input 
                type="number" 
                placeholder="Số lượng" 
                value={d.quantity} 
                onChange={e => handleDetailChange(i, 'quantity', e.target.value ? Number(e.target.value) : '')}
                required
                min="0.01"
                step="0.01"
              />
              
              <input 
                type="number" 
                placeholder="Đơn giá" 
                value={d.unitPrice} 
                onChange={e => handleDetailChange(i, 'unitPrice', e.target.value ? Number(e.target.value) : '')}
                required
                min="0"
                step="0.01"
              />

              <input 
                placeholder="Ghi chú" 
                value={d.note} 
                onChange={e => handleDetailChange(i, 'note', e.target.value)}
              />

              <button type="button" onClick={() => handleRemoveDetail(i)}>Xóa</button>
            </div>
          ))}
          
          <button type="button" onClick={handleAddDetail} style={{ marginBottom: '15px' }}>+ Thêm dòng</button>
          
          <div style={{ marginTop: '15px' }}>
            <button type="submit" style={{ backgroundColor: '#2ecc71', color: '#fff', padding: '10px 20px', border: 'none', cursor: 'pointer' }}>Lưu Phiếu Nháp</button>
          </div>
        </form>
      </div>

      <hr style={{ margin: '30px 0' }} />

      <h3>Danh Sách Phiếu Nhập</h3>
      <table style={{ width: '100%', borderCollapse: 'collapse', marginBottom: '30px' }}>
        <thead>
          <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã phiếu</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Ngày tạo</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Kho</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Người tạo</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Trạng thái</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Hành động</th>
          </tr>
        </thead>
        <tbody>
          {receipts.map(r => (
            <tr key={r.id}>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.code}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{new Date(r.createdAt).toLocaleString()}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.warehouseName}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.createdByName}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7', fontWeight: 'bold', color: r.status === 'Draft' ? '#f39c12' : r.status === 'Approved' ? '#27ae60' : '#c0392b' }}>
                {r.status === 'Draft' ? 'Nháp' : r.status === 'Approved' ? 'Đã duyệt' : 'Đã hủy'}
              </td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                <button onClick={() => handleViewDetails(r.id)} style={{ cursor: 'pointer', marginRight: '5px' }}>Chi tiết</button>
                {r.status === 'Draft' && (
                  <>
                    <button 
                      onClick={() => handleApprove(r.id)} 
                      disabled={approvingId === r.id}
                      style={{ 
                        cursor: approvingId === r.id ? 'not-allowed' : 'pointer', 
                        marginRight: '5px', 
                        backgroundColor: approvingId === r.id ? '#95a5a6' : '#3498db', 
                        color: '#fff', 
                        border: 'none', 
                        padding: '5px 10px', 
                        borderRadius: '3px' 
                      }}
                    >
                      {approvingId === r.id ? 'Đang duyệt...' : 'Duyệt'}
                    </button>
                    <button onClick={() => handleCancel(r.id)} style={{ cursor: 'pointer', backgroundColor: '#e74c3c', color: '#fff', border: 'none', padding: '5px 10px', borderRadius: '3px' }}>Hủy</button>
                  </>
                )}
              </td>
            </tr>
          ))}
          {receipts.length === 0 && (
            <tr>
              <td colSpan={6} style={{ textAlign: 'center', padding: '10px' }}>Chưa có phiếu nhập</td>
            </tr>
          )}
        </tbody>
      </table>

      {selectedReceipt && (
        <div style={{ padding: '15px', border: '1px solid #34495e', borderRadius: '5px', backgroundColor: '#f9f9f9' }}>
          <h3>Chi Tiết Phiếu Nhập: {selectedReceipt.code}</h3>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px', marginBottom: '15px' }}>
            <div><strong>Kho:</strong> {selectedReceipt.warehouseName}</div>
            <div><strong>Trạng thái:</strong> {selectedReceipt.status === 'Draft' ? 'Nháp' : selectedReceipt.status === 'Approved' ? 'Đã duyệt' : 'Đã hủy'}</div>
            <div><strong>Người tạo:</strong> {selectedReceipt.createdByName}</div>
            <div><strong>Ngày tạo:</strong> {new Date(selectedReceipt.createdAt).toLocaleString()}</div>
            {selectedReceipt.status === 'Approved' && (
              <>
                <div><strong>Người duyệt:</strong> {selectedReceipt.approvedByName}</div>
                <div><strong>Ngày duyệt:</strong> {new Date(selectedReceipt.approvedAt).toLocaleString()}</div>
              </>
            )}
            <div><strong>Ghi chú:</strong> {selectedReceipt.note}</div>
          </div>
          
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã SP</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Tên SP</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>Số lượng</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>Đơn giá</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>Thành tiền</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Ghi chú</th>
              </tr>
            </thead>
            <tbody>
              {selectedReceipt.details.map(d => (
                <tr key={d.id}>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{d.productCode}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{d.productName}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>{d.quantity}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>{d.unitPrice.toLocaleString()}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>{(d.quantity * d.unitPrice).toLocaleString()}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{d.note}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr style={{ backgroundColor: '#e8f6f3', fontWeight: 'bold' }}>
                <td colSpan={4} style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>Tổng cộng:</td>
                <td style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>
                  {selectedReceipt.details.reduce((sum, d) => sum + (d.quantity * d.unitPrice), 0).toLocaleString()}
                </td>
                <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}></td>
              </tr>
            </tfoot>
          </table>
          
          <button onClick={() => setSelectedReceipt(null)} style={{ marginTop: '15px', cursor: 'pointer', padding: '8px 15px' }}>Đóng chi tiết</button>
        </div>
      )}
    </div>
  );
};

export default ImportReceipts;
