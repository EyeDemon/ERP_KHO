import React, { useState, useEffect, useMemo } from 'react';
import apiClient from '../services/apiClient';

interface Product {
  id: number;
  code: string;
  name: string;
  description?: string;
  unitId: number;
  unitName?: string;
  isActive: boolean;
}

interface Unit {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
}

const PAGE_SIZE = 10;

const Products = () => {
  const [products, setProducts] = useState<Product[]>([]);
  const [units, setUnits] = useState<Unit[]>([]);
  const [unitsLoading, setUnitsLoading] = useState(false);
  const [unitsError, setUnitsError] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');

  // Search and Pagination
  const [searchTerm, setSearchTerm] = useState('');
  const [currentPage, setCurrentPage] = useState(1);

  // Form State
  const [showForm, setShowForm] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [formData, setFormData] = useState<Partial<Product>>({
    code: '',
    name: '',
    description: '',
    unitId: 0,
    isActive: true
  });
  const [formError, setFormError] = useState('');
  const [formLoading, setFormLoading] = useState(false);

  const fetchProducts = async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.get('/api/products');
      setProducts(res.data);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tải danh sách sản phẩm');
    } finally {
      setLoading(false);
    }
  };

  const fetchUnits = async () => {
    setUnitsLoading(true);
    setUnitsError('');
    try {
      const res = await apiClient.get('/api/units');
      setUnits(res.data);
    } catch (err: any) {
      const errMsg = err.response?.data?.message || 'Lỗi khi tải danh sách đơn vị tính';
      setUnitsError(errMsg);
      console.error('Lỗi khi tải danh sách đơn vị tính', err);
    } finally {
      setUnitsLoading(false);
    }
  };

  useEffect(() => {
    fetchProducts();
    fetchUnits();
  }, []);

  // Filter and Paginate
  const filteredProducts = useMemo(() => {
    if (!searchTerm) return products;
    const lowerTerm = searchTerm.toLowerCase();
    return products.filter(
      p => p.code.toLowerCase().includes(lowerTerm) || p.name.toLowerCase().includes(lowerTerm)
    );
  }, [products, searchTerm]);

  const totalPages = Math.max(1, Math.ceil(filteredProducts.length / PAGE_SIZE));
  
  // Empty page protection
  useEffect(() => {
    if (currentPage > totalPages) {
      setCurrentPage(totalPages);
    }
  }, [totalPages, currentPage]);

  const paginatedProducts = useMemo(() => {
    const start = (currentPage - 1) * PAGE_SIZE;
    return filteredProducts.slice(start, start + PAGE_SIZE);
  }, [filteredProducts, currentPage]);

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchTerm(e.target.value);
    setCurrentPage(1); // Reset page on search
  };

  const handleAddNew = () => {
    setIsEditing(false);
    setFormData({
      code: '',
      name: '',
      description: '',
      unitId: units.length > 0 ? units[0].id : 0,
      isActive: true
    });
    setFormError('');
    setShowForm(true);
    setSuccessMsg('');
  };

  const handleEdit = (product: Product) => {
    setIsEditing(true);
    setFormData({ ...product });
    setFormError('');
    setShowForm(true);
    setSuccessMsg('');
  };

  const handleCancelForm = () => {
    setShowForm(false);
    setFormError('');
  };

  const handleFormChange = (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => {
    const { name, value, type } = e.target;
    let finalValue: any = value;
    if (type === 'checkbox') {
      finalValue = (e.target as HTMLInputElement).checked;
    } else if (name === 'unitId') {
      finalValue = parseInt(value, 10);
    }
    setFormData(prev => ({ ...prev, [name]: finalValue }));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError('');
    setSuccessMsg('');

    if (!formData.code || !formData.code.trim()) {
      setFormError('Mã sản phẩm không được để trống.');
      return;
    }
    if (!formData.name || !formData.name.trim()) {
      setFormError('Tên sản phẩm không được để trống.');
      return;
    }
    if (!formData.unitId) {
      setFormError('Vui lòng chọn đơn vị tính.');
      return;
    }

    setFormLoading(true);
    try {
      if (isEditing && formData.id) {
        const updatePayload = {
          name: formData.name,
          description: formData.description,
          unitId: formData.unitId,
          isActive: formData.isActive
        };
        await apiClient.put(`/api/products/${formData.id}`, updatePayload);
        setSuccessMsg('Cập nhật sản phẩm thành công.');
      } else {
        const createPayload = {
          code: formData.code,
          name: formData.name,
          description: formData.description,
          unitId: formData.unitId
        };
        await apiClient.post('/api/products', createPayload);
        setSuccessMsg('Thêm mới sản phẩm thành công.');
      }
      setShowForm(false);
      fetchProducts();
    } catch (err: any) {
      setFormError(err.response?.data?.message || 'Lỗi khi lưu sản phẩm.');
    } finally {
      setFormLoading(false);
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('Bạn có chắc muốn xóa sản phẩm này?')) return;
    setSuccessMsg('');
    setError('');
    try {
      await apiClient.delete(`/api/products/${id}`);
      setSuccessMsg('Xóa sản phẩm thành công.');
      fetchProducts();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi xóa sản phẩm');
    }
  };

  return (
    <div>
      <h2>Quản lý danh mục Sản phẩm</h2>
      
      {successMsg && <div style={{ color: 'green', marginBottom: '10px' }}>{successMsg}</div>}
      {error && <div style={{ color: 'red', marginBottom: '10px' }}>{error}</div>}

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
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
              {unitsLoading && <span>Đang tải danh sách đơn vị...</span>}
              {unitsError && <span style={{ color: 'red' }}>{unitsError} <button onClick={fetchUnits} style={{cursor: 'pointer'}}>Thử lại</button></span>}
              <button 
                onClick={handleAddNew} 
                disabled={unitsLoading || !!unitsError}
                style={{ padding: '8px 16px', cursor: (unitsLoading || !!unitsError) ? 'not-allowed' : 'pointer', backgroundColor: (unitsLoading || !!unitsError) ? '#95a5a6' : '#3498db', color: '#fff', border: 'none', borderRadius: '4px' }}>
                Thêm mới
              </button>
            </div>
          </div>

          {loading ? (
            <div>Đang tải...</div>
          ) : (
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: '10px' }}>
                <thead>
                  <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã SP</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Tên SP</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mô tả</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Đơn vị</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Trạng thái</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Hành động</th>
                  </tr>
                </thead>
                <tbody>
                  {paginatedProducts.map(p => (
                    <tr key={p.id}>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{p.code}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7', wordBreak: 'break-word', maxWidth: '200px' }}>{p.name}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7', wordBreak: 'break-word', maxWidth: '300px' }}>{p.description}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{p.unitName || p.unitId}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{p.isActive ? 'Hoạt động' : 'Khóa'}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                        <button 
                          onClick={() => handleEdit(p)} 
                          disabled={unitsLoading || !!unitsError}
                          style={{ marginRight: '10px', cursor: (unitsLoading || !!unitsError) ? 'not-allowed' : 'pointer', opacity: (unitsLoading || !!unitsError) ? 0.6 : 1 }}
                        >Sửa</button>
                        <button onClick={() => handleDelete(p.id)} style={{ color: 'red', cursor: 'pointer' }}>Xóa</button>
                      </td>
                    </tr>
                  ))}
                  {filteredProducts.length === 0 && (
                    <tr>
                      <td colSpan={6} style={{ textAlign: 'center', padding: '20px' }}>
                        Không tìm thấy sản phẩm nào
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
              
              {/* Pagination */}
              {totalPages > 1 && (
                <div style={{ marginTop: '15px', display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '10px' }}>
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
          <h3>{isEditing ? 'Sửa sản phẩm' : 'Thêm mới sản phẩm'}</h3>
          {formError && <div style={{ color: 'red', marginBottom: '15px' }}>{formError}</div>}
          <form onSubmit={handleSubmit}>
            <div style={{ marginBottom: '15px' }}>
              <label style={{ display: 'block', marginBottom: '5px' }}>Mã sản phẩm <span style={{ color: 'red' }}>*</span></label>
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
              <label style={{ display: 'block', marginBottom: '5px' }}>Tên sản phẩm <span style={{ color: 'red' }}>*</span></label>
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
              <label style={{ display: 'block', marginBottom: '5px' }}>Mô tả</label>
              <textarea 
                name="description" 
                value={formData.description || ''} 
                onChange={handleFormChange}
                style={{ width: '100%', padding: '8px', boxSizing: 'border-box', minHeight: '80px' }}
                disabled={formLoading}
              />
            </div>
            <div style={{ marginBottom: '15px' }}>
              <label style={{ display: 'block', marginBottom: '5px' }}>Đơn vị tính <span style={{ color: 'red' }}>*</span></label>
              <select 
                name="unitId" 
                value={formData.unitId || ''} 
                onChange={handleFormChange}
                style={{ width: '100%', padding: '8px', boxSizing: 'border-box' }}
                disabled={formLoading}
              >
                <option value="">-- Chọn đơn vị tính --</option>
                {units.map(u => (
                  <option key={u.id} value={u.id}>{u.name} ({u.code})</option>
                ))}
              </select>
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

export default Products;
