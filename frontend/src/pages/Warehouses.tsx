import React, { useState, useEffect, useMemo } from 'react';
import apiClient from '../services/apiClient';

interface Warehouse {
  id: number;
  code: string;
  name: string;
  address?: string;
  isActive: boolean;
}

const PAGE_SIZE = 10;

const Warehouses = () => {
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');

  // Search and Pagination
  const [searchTerm, setSearchTerm] = useState('');
  const [currentPage, setCurrentPage] = useState(1);

  // Form State
  const [showForm, setShowForm] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [formData, setFormData] = useState<Partial<Warehouse>>({
    code: '',
    name: '',
    address: '',
    isActive: true
  });
  const [formError, setFormError] = useState('');
  const [formLoading, setFormLoading] = useState(false);

  const fetchWarehouses = async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.get('/api/warehouses');
      setWarehouses(res.data);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tải danh sách kho');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchWarehouses();
  }, []);

  // Filter and Paginate
  const filteredWarehouses = useMemo(() => {
    if (!searchTerm) return warehouses;
    const lowerTerm = searchTerm.toLowerCase();
    return warehouses.filter(
      w => 
        w.code.toLowerCase().includes(lowerTerm) || 
        w.name.toLowerCase().includes(lowerTerm) || 
        (w.address && w.address.toLowerCase().includes(lowerTerm))
    );
  }, [warehouses, searchTerm]);

  const totalPages = Math.max(1, Math.ceil(filteredWarehouses.length / PAGE_SIZE));
  
  // Empty page protection
  useEffect(() => {
    if (currentPage > totalPages) {
      setCurrentPage(totalPages);
    }
  }, [totalPages, currentPage]);

  const paginatedWarehouses = useMemo(() => {
    const start = (currentPage - 1) * PAGE_SIZE;
    return filteredWarehouses.slice(start, start + PAGE_SIZE);
  }, [filteredWarehouses, currentPage]);

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchTerm(e.target.value);
    setCurrentPage(1); // Reset page on search
  };

  const handleAddNew = () => {
    setIsEditing(false);
    setFormData({
      code: '',
      name: '',
      address: '',
      isActive: true
    });
    setFormError('');
    setShowForm(true);
    setSuccessMsg('');
  };

  const handleEdit = (warehouse: Warehouse) => {
    setIsEditing(true);
    setFormData({ ...warehouse });
    setFormError('');
    setShowForm(true);
    setSuccessMsg('');
  };

  const handleCancelForm = () => {
    setShowForm(false);
    setFormError('');
  };

  const handleFormChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    const { name, value, type } = e.target;
    let finalValue: any = value;
    if (type === 'checkbox') {
      finalValue = (e.target as HTMLInputElement).checked;
    }
    setFormData(prev => ({ ...prev, [name]: finalValue }));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError('');
    setSuccessMsg('');

    if (!formData.code || !formData.code.trim()) {
      setFormError('Mã kho không được để trống.');
      return;
    }
    if (!formData.name || !formData.name.trim()) {
      setFormError('Tên kho không được để trống.');
      return;
    }

    setFormLoading(true);
    try {
      if (isEditing && formData.id) {
        const updatePayload = {
          name: formData.name,
          address: formData.address,
          isActive: formData.isActive
        };
        await apiClient.put(`/api/warehouses/${formData.id}`, updatePayload);
        setSuccessMsg('Cập nhật kho thành công.');
      } else {
        const createPayload = {
          code: formData.code,
          name: formData.name,
          address: formData.address
        };
        await apiClient.post('/api/warehouses', createPayload);
        setSuccessMsg('Thêm mới kho thành công.');
      }
      setShowForm(false);
      setCurrentPage(1);
      fetchWarehouses();
    } catch (err: any) {
      setFormError(err.response?.data?.message || 'Lỗi khi lưu kho.');
    } finally {
      setFormLoading(false);
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('Bạn có chắc muốn xóa kho này?')) return;
    setSuccessMsg('');
    setError('');
    try {
      await apiClient.delete(`/api/warehouses/${id}`);
      setSuccessMsg('Xóa kho thành công.');
      setCurrentPage(1);
      fetchWarehouses();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi xóa kho');
    }
  };

  return (
    <div>
      <h2>Quản lý danh mục Kho</h2>
      
      {successMsg && <div style={{ color: 'green', marginBottom: '10px' }}>{successMsg}</div>}
      {error && (
        <div style={{ color: 'red', marginBottom: '10px' }}>
          {error} <button onClick={fetchWarehouses} style={{cursor: 'pointer'}}>Thử lại</button>
        </div>
      )}

      {!showForm ? (
        <>
          <div style={{ display: 'flex', justifyContent: 'space-between', flexWrap: 'wrap', gap: '10px', marginBottom: '15px' }}>
            <input 
              type="text" 
              placeholder="Tìm kiếm theo mã, tên hoặc địa chỉ..." 
              value={searchTerm}
              onChange={handleSearchChange}
              style={{ padding: '8px', minWidth: '250px', flex: '1', maxWidth: '400px' }}
            />
            <button onClick={handleAddNew} style={{ padding: '8px 16px', cursor: 'pointer', backgroundColor: '#3498db', color: '#fff', border: 'none', borderRadius: '4px' }}>
              Thêm mới
            </button>
          </div>

          {loading ? (
            <div>Đang tải...</div>
          ) : (
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: '10px' }}>
                <thead>
                  <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã kho</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Tên kho</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Địa chỉ</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Trạng thái</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Hành động</th>
                  </tr>
                </thead>
                <tbody>
                  {paginatedWarehouses.map(w => (
                    <tr key={w.id}>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{w.code}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7', wordBreak: 'break-word', maxWidth: '200px' }}>{w.name}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7', wordBreak: 'break-word', maxWidth: '300px' }}>{w.address || '-'}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{w.isActive ? 'Hoạt động' : 'Khóa'}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                        <button onClick={() => handleEdit(w)} style={{ marginRight: '10px', cursor: 'pointer' }}>Sửa</button>
                        <button onClick={() => handleDelete(w.id)} style={{ color: 'red', cursor: 'pointer' }}>Xóa</button>
                      </td>
                    </tr>
                  ))}
                  {filteredWarehouses.length === 0 && (
                    <tr>
                      <td colSpan={5} style={{ textAlign: 'center', padding: '20px' }}>
                        {warehouses.length === 0 ? 'Chưa có kho nào' : 'Không tìm thấy kho nào phù hợp'}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
              
              {/* Pagination */}
              {totalPages > 1 && (
                <div style={{ marginTop: '15px', display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                  <button 
                    disabled={currentPage === 1}
                    onClick={() => setCurrentPage(1)}
                  >
                    &laquo; Đầu
                  </button>
                  <button 
                    disabled={currentPage === 1}
                    onClick={() => setCurrentPage(prev => prev - 1)}
                  >
                    &lsaquo; Trước
                  </button>
                  <span>Trang {currentPage} / {totalPages}</span>
                  <button 
                    disabled={currentPage === totalPages}
                    onClick={() => setCurrentPage(prev => prev + 1)}
                  >
                    Sau &rsaquo;
                  </button>
                  <button 
                    disabled={currentPage === totalPages}
                    onClick={() => setCurrentPage(totalPages)}
                  >
                    Cuối &raquo;
                  </button>
                </div>
              )}
            </div>
          )}
        </>
      ) : (
        <div style={{ border: '1px solid #bdc3c7', padding: '20px', borderRadius: '4px', maxWidth: '600px' }}>
          <h3>{isEditing ? 'Sửa kho' : 'Thêm mới kho'}</h3>
          {formError && <div style={{ color: 'red', marginBottom: '15px' }}>{formError}</div>}
          <form onSubmit={handleSubmit}>
            <div style={{ marginBottom: '15px' }}>
              <label style={{ display: 'block', marginBottom: '5px' }}>Mã kho <span style={{ color: 'red' }}>*</span></label>
              <input 
                type="text" 
                name="code" 
                value={formData.code || ''} 
                onChange={handleFormChange}
                style={{ width: '100%', padding: '8px', boxSizing: 'border-box', backgroundColor: isEditing ? '#f2f2f2' : '#fff' }}
                disabled={formLoading || isEditing}
              />
            </div>
            <div style={{ marginBottom: '15px' }}>
              <label style={{ display: 'block', marginBottom: '5px' }}>Tên kho <span style={{ color: 'red' }}>*</span></label>
              <input 
                type="text" 
                name="name" 
                value={formData.name || ''} 
                onChange={handleFormChange}
                style={{ width: '100%', padding: '8px', boxSizing: 'border-box' }}
                disabled={formLoading}
              />
            </div>
            <div style={{ marginBottom: '15px' }}>
              <label style={{ display: 'block', marginBottom: '5px' }}>Địa chỉ</label>
              <textarea 
                name="address" 
                value={formData.address || ''} 
                onChange={handleFormChange}
                style={{ width: '100%', padding: '8px', boxSizing: 'border-box', minHeight: '60px' }}
                disabled={formLoading}
              />
            </div>
            <div style={{ marginBottom: '15px' }}>
              <label style={{ display: 'flex', alignItems: 'center', cursor: 'pointer' }}>
                <input 
                  type="checkbox" 
                  name="isActive" 
                  checked={formData.isActive || false}
                  onChange={handleFormChange}
                  disabled={formLoading}
                  style={{ marginRight: '8px' }}
                />
                Trạng thái hoạt động
              </label>
            </div>
            <div style={{ display: 'flex', gap: '10px' }}>
              <button type="submit" disabled={formLoading} style={{ padding: '8px 16px', cursor: 'pointer', backgroundColor: '#2ecc71', color: '#fff', border: 'none', borderRadius: '4px' }}>
                {formLoading ? 'Đang lưu...' : 'Lưu'}
              </button>
              <button type="button" onClick={handleCancelForm} disabled={formLoading} style={{ padding: '8px 16px', cursor: 'pointer' }}>
                Hủy
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
};

export default Warehouses;
