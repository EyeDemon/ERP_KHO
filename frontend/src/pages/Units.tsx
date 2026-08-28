import React, { useState, useEffect, useMemo } from 'react';
import apiClient from '../services/apiClient';
import { canManageCatalogs, currentRole } from '../services/authorization';

interface Unit {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
}

const PAGE_SIZE = 10;

const Units = () => {
  const canManage = canManageCatalogs(currentRole());
  const [units, setUnits] = useState<Unit[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');

  // Search and Pagination
  const [searchTerm, setSearchTerm] = useState('');
  const [currentPage, setCurrentPage] = useState(1);

  // Form State
  const [showForm, setShowForm] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [formData, setFormData] = useState<Partial<Unit>>({
    code: '',
    name: '',
    isActive: true
  });
  const [formError, setFormError] = useState('');
  const [formLoading, setFormLoading] = useState(false);

  const fetchUnits = async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.get('/api/units');
      setUnits(res.data);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tải danh sách đơn vị tính');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchUnits();
  }, []);

  // Filter and Paginate
  const filteredUnits = useMemo(() => {
    if (!searchTerm) return units;
    const lowerTerm = searchTerm.toLowerCase();
    return units.filter(
      u => u.code.toLowerCase().includes(lowerTerm) || u.name.toLowerCase().includes(lowerTerm)
    );
  }, [units, searchTerm]);

  const totalPages = Math.max(1, Math.ceil(filteredUnits.length / PAGE_SIZE));
  
  // Empty page protection
  useEffect(() => {
    if (currentPage > totalPages) {
      setCurrentPage(totalPages);
    }
  }, [totalPages, currentPage]);

  const paginatedUnits = useMemo(() => {
    const start = (currentPage - 1) * PAGE_SIZE;
    return filteredUnits.slice(start, start + PAGE_SIZE);
  }, [filteredUnits, currentPage]);

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchTerm(e.target.value);
    setCurrentPage(1); // Reset page on search
  };

  const handleAddNew = () => {
    setIsEditing(false);
    setFormData({
      code: '',
      name: '',
      isActive: true
    });
    setFormError('');
    setShowForm(true);
    setSuccessMsg('');
  };

  const handleEdit = (unit: Unit) => {
    setIsEditing(true);
    setFormData({ ...unit });
    setFormError('');
    setShowForm(true);
    setSuccessMsg('');
  };

  const handleCancelForm = () => {
    setShowForm(false);
    setFormError('');
  };

  const handleFormChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value, type, checked } = e.target;
    const finalValue = type === 'checkbox' ? checked : value;
    setFormData(prev => ({ ...prev, [name]: finalValue }));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError('');
    setSuccessMsg('');

    if (!formData.code || !formData.code.trim()) {
      setFormError('Mã đơn vị tính không được để trống.');
      return;
    }
    if (!formData.name || !formData.name.trim()) {
      setFormError('Tên đơn vị tính không được để trống.');
      return;
    }

    setFormLoading(true);
    try {
      if (isEditing && formData.id) {
        const updatePayload = {
          name: formData.name,
          isActive: formData.isActive
        };
        await apiClient.put(`/api/units/${formData.id}`, updatePayload);
        setSuccessMsg('Cập nhật đơn vị tính thành công.');
      } else {
        const createPayload = {
          code: formData.code,
          name: formData.name
        };
        await apiClient.post('/api/units', createPayload);
        setSuccessMsg('Thêm mới đơn vị tính thành công.');
      }
      setShowForm(false);
      setCurrentPage(1);
      fetchUnits();
    } catch (err: any) {
      setFormError(err.response?.data?.message || 'Lỗi khi lưu đơn vị tính.');
    } finally {
      setFormLoading(false);
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('Bạn có chắc muốn xóa đơn vị tính này?')) return;
    setSuccessMsg('');
    setError('');
    try {
      await apiClient.delete(`/api/units/${id}`);
      setSuccessMsg('Xóa đơn vị tính thành công.');
      setCurrentPage(1);
      fetchUnits();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi xóa đơn vị tính');
    }
  };

  return (
    <div>
      <h2>Quản lý danh mục Đơn vị tính</h2>
      
      {successMsg && <div style={{ color: 'green', marginBottom: '10px' }}>{successMsg}</div>}
      {error && (
        <div style={{ color: 'red', marginBottom: '10px' }}>
          {error} <button onClick={fetchUnits} style={{cursor: 'pointer'}}>Thử lại</button>
        </div>
      )}

      {!showForm ? (
        <>
          <div style={{ display: 'flex', justifyContent: 'space-between', flexWrap: 'wrap', gap: '10px', marginBottom: '15px' }}>
            <input 
              type="text" 
              placeholder="Tìm kiếm theo mã hoặc tên..." 
              value={searchTerm}
              onChange={handleSearchChange}
              style={{ padding: '8px', minWidth: '200px', flex: '1', maxWidth: '300px' }}
            />
            {canManage && <button onClick={handleAddNew} style={{ padding: '8px 16px', cursor: 'pointer', backgroundColor: '#3498db', color: '#fff', border: 'none', borderRadius: '4px' }}>Thêm mới</button>}
          </div>

          {loading ? (
            <div>Đang tải...</div>
          ) : (
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: '10px' }}>
                <thead>
                  <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã ĐVT</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Tên ĐVT</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Trạng thái</th>
                    {canManage && <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Hành động</th>}
                  </tr>
                </thead>
                <tbody>
                  {paginatedUnits.map(u => (
                    <tr key={u.id}>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{u.code}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7', wordBreak: 'break-word', maxWidth: '300px' }}>{u.name}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{u.isActive ? 'Hoạt động' : 'Khóa'}</td>
                      {canManage && <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                        <button onClick={() => handleEdit(u)} style={{ marginRight: '10px', cursor: 'pointer' }}>Sửa</button>
                        <button onClick={() => handleDelete(u.id)} style={{ color: 'red', cursor: 'pointer' }}>Xóa</button>
                      </td>}
                    </tr>
                  ))}
                  {filteredUnits.length === 0 && (
                    <tr>
                      <td colSpan={canManage ? 4 : 3} style={{ textAlign: 'center', padding: '20px' }}>
                        {units.length === 0 ? 'Chưa có đơn vị tính nào' : 'Không tìm thấy đơn vị tính nào phù hợp'}
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
          <h3>{isEditing ? 'Sửa đơn vị tính' : 'Thêm mới đơn vị tính'}</h3>
          {formError && <div style={{ color: 'red', marginBottom: '15px' }}>{formError}</div>}
          <form onSubmit={handleSubmit}>
            <div style={{ marginBottom: '15px' }}>
              <label style={{ display: 'block', marginBottom: '5px' }}>Mã đơn vị tính <span style={{ color: 'red' }}>*</span></label>
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
              <label style={{ display: 'block', marginBottom: '5px' }}>Tên đơn vị tính <span style={{ color: 'red' }}>*</span></label>
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

export default Units;
