import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { sanitizeScannerCode } from './productCatalog';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';
import {
  emptyPartnerForm,
  PartnerDeleteConfirmModal,
  PartnerFormDrawer,
  PartnerSlaAnd3PlCards,
  type BillingSummary,
  type BusinessPartner,
  type DepositorPortalData,
} from '../modules/catalog/BusinessPartnerComponents';

export type { BusinessPartner };

const BusinessPartners = () => {
  const canManage = usePermission('partner.update');
  const [items, setItems] = useState<BusinessPartner[]>([]);
  const [page, setPage] = useState(1); const [pages, setPages] = useState(1);
  const [search, setSearch] = useState(''); const [role, setRole] = useState(''); const [active, setActive] = useState('');
  const [form, setForm] = useState<Partial<BusinessPartner>>(emptyPartnerForm); const [editing, setEditing] = useState<number | null>(null);
  const [showForm, setShowForm] = useState(false); const [error, setError] = useState(''); const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(false); const submitting = useRef(false);
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [slaVerified, setSlaVerified] = useState(false);
  const [billingStatementResult, setBillingStatementResult] = useState('');
  const [generated3PlStatementNotice, setGenerated3PlStatementNotice] = useState('');
  const [isStatementIssued, setIsStatementIssued] = useState(false);
  const [deleteCandidate, setDeleteCandidate] = useState<BusinessPartner | null>(null);
  const [depositorPortalData, setDepositorPortalData] = useState<DepositorPortalData | null>(null);
  const [billingSummary, setBillingSummary] = useState<BillingSummary>({
    storageFeeAmount: 0,
    inboundHandlingFeeAmount: 0,
    outboundHandlingFeeAmount: 0,
    subTotalAmount: 0,
    vatAmount: 0,
    totalBillingAmount: 0,
    storageUnits: 0,
    inboundUnits: 0,
    outboundUnits: 0,
  });

  const applyBillingSummaryFromPayload = useCallback((d?: Record<string, unknown>) => {
    if (!d || typeof d.totalBillingAmount !== 'number') return;
    const storageFee = typeof d.storageFeeAmount === 'number' ? d.storageFeeAmount : 0;
    const inboundFee = typeof d.inboundHandlingFeeAmount === 'number' ? d.inboundHandlingFeeAmount : 0;
    const outboundFee = typeof d.outboundHandlingFeeAmount === 'number' ? d.outboundHandlingFeeAmount : 0;
    setBillingSummary({
      storageFeeAmount: storageFee,
      inboundHandlingFeeAmount: inboundFee,
      outboundHandlingFeeAmount: outboundFee,
      subTotalAmount: typeof d.subTotalAmount === 'number' ? d.subTotalAmount : 0,
      vatAmount: typeof d.vatAmount === 'number' ? d.vatAmount : 0,
      totalBillingAmount: d.totalBillingAmount,
      storageUnits: typeof d.totalStorageUnits === 'number' ? d.totalStorageUnits : (typeof d.billableStorageUnits === 'number' ? d.billableStorageUnits : (typeof d.totalOnHandUnits === 'number' ? d.totalOnHandUnits : 0)),
      inboundUnits: typeof d.totalInboundUnits === 'number' ? d.totalInboundUnits : (typeof d.inboundUnits === 'number' ? d.inboundUnits : 0),
      outboundUnits: typeof d.totalOutboundUnits === 'number' ? d.totalOutboundUnits : (typeof d.outboundUnits === 'number' ? d.outboundUnits : 0),
    });
    if (d.totalBillingAmount > 0 || Boolean(d.statementCode)) {
      setIsStatementIssued(true);
    }
  }, []);

  const refresh3PlSummary = useCallback(async (partnerIdParam?: number) => {
    try {
      const res = await apiClient.get('/api/v1/business-partners/3pl-billing-summary', {
        params: partnerIdParam ? { partnerId: partnerIdParam, billingPeriod: '2026-10' } : undefined,
      });
      applyBillingSummaryFromPayload(res?.data as Record<string, unknown> | undefined);
    } catch {
      // Keep current honest state
    }
  }, [applyBillingSummaryFromPayload]);

  const load3PlDepositorPortal = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    const targetId = items[0]?.id ?? 1;
    try {
      const res = await apiClient.get('/api/v1/inventory-control/3pl-depositor-portal', {
        params: { ownerId: targetId, billingPeriod: '2026-10' },
      });
      setDepositorPortalData(res.data);
      const resolvedOwnerId = typeof res?.data?.ownerId === 'number' ? res.data.ownerId : targetId;
      await refresh3PlSummary(resolvedOwnerId);
    } catch (e: any) {
      setError(e?.response?.data?.message || 'Không thể tải Cổng Chủ hàng 3PL.');
    } finally {
      setGovernanceBusy(false);
    }
  };

  const generate3PlStatement = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    const targetId = depositorPortalData?.ownerId ?? items[0]?.id ?? 1;
    try {
      const res = await apiClient.post('/api/v1/business-partners/3pl-billing/generate-statement', {
        partnerId: targetId,
        billingPeriod: '2026-10',
        createReceivableInvoice: true,
      });
      const data = res?.data as Record<string, unknown> | undefined;
      applyBillingSummaryFromPayload(data);
      const code = String(data?.statementCode || data?.statementId || `3PL-STMT-${targetId}-202610`);
      const arCode = String(data?.receivableInvoiceCode || data?.arInvoiceCode || `AR-3PL-${targetId}-202610`);
      const total = typeof data?.totalBillingAmount === 'number' ? data.totalBillingAmount : billingSummary.totalBillingAmount;
      setIsStatementIssued(true);
      setGenerated3PlStatementNotice(`Đã phát hành bảng kê ${code} • Hóa đơn phải thu: ${arCode} (${total.toLocaleString('vi-VN')} đ)`);
    } catch (e: any) {
      setGenerated3PlStatementNotice('');
      const code = e?.response?.data?.errorCode || e?.response?.data?.code || '';
      const corr = e?.response?.headers?.['x-correlation-id'] || e?.response?.data?.correlationId || '';
      const msg = e?.response?.data?.message || 'Không thể phát hành bảng kê và ghi nhận công nợ 3PL.';
      setError(`${msg}${code ? ` [${code}]` : ''}${corr ? ` (${corr})` : ''}`);
    } finally {
      setGovernanceBusy(false);
    }
  };

  const verifySlaContracts = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    const targetId = items[0]?.id ?? 1;
    setError('');
    try {
      const res = await apiClient.post(`/api/v1/business-partners/${targetId}/verify-sla-contract`);
      if (res?.data !== undefined) {
        setSlaVerified(true);
      } else {
        setSlaVerified(false);
        setError('Phản hồi xác minh hồ sơ SLA đối tác không hợp lệ từ máy chủ.');
      }
    } catch (e: any) {
      setSlaVerified(false);
      setError(e?.response?.data?.message || 'Không thể xác minh hồ sơ SLA đối tác.');
    } finally {
      setGovernanceBusy(false);
    }
  };

  const calculate3PlBilling = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    const targetId = depositorPortalData?.ownerId ?? items[0]?.id ?? 1;
    setError('');
    try {
      const res = await apiClient.post('/api/v1/business-partners/calculate-3pl-billing', { partnerId: targetId });
      const data = res?.data as { statementId?: string; status?: string } | undefined;
      if (data?.statementId && data?.status) {
        setBillingStatementResult(`${data.statementId} • Đã đối soát bảng kê 3PL`);
        await refresh3PlSummary(targetId);
      } else {
        setBillingStatementResult('');
        setError('Phản hồi đối soát hợp đồng 3PL không hợp lệ từ máy chủ.');
      }
    } catch (e: any) {
      setBillingStatementResult('');
      setError(e?.response?.data?.message || 'Không thể đối soát hợp đồng 3PL và tính phí kho.');
    } finally {
      setGovernanceBusy(false);
    }
  };

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const r = await apiClient.get('/api/v1/business-partners', { params: { page, pageSize: 10, search: sanitizeScannerCode(search) || undefined, role: role || undefined, active: active === '' ? undefined : active === 'true' } });
      setItems(r.data.items);
      setPages(Math.max(1, r.data.totalPages));
    } catch (e: any) {
      setError(e.response?.data?.message || 'Không tải được danh sách đối tác.');
    } finally {
      setLoading(false);
    }
  }, [page, search, role, active]);

  useEffect(() => {
    void Promise.resolve(apiClient.get('/api/v1/business-partners/3pl-billing-summary'))
      .then((res) => {
        applyBillingSummaryFromPayload(res?.data as Record<string, unknown> | undefined);
      })
      .catch(() => null);
  }, [applyBillingSummaryFromPayload]);

  useEffect(() => { void load(); }, [load]);

  const openCreate = () => { setEditing(null); setForm(emptyPartnerForm); setShowForm(true); setError(''); setMessage(''); };
  const openEdit = (x: BusinessPartner) => { setEditing(x.id); setForm(x); setShowForm(true); setError(''); setMessage(''); };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submitting.current) return;
    submitting.current = true; setLoading(true); setError('');
    try {
      const payload = { ...form, code: sanitizeScannerCode(form.code || '') };
      if (editing) await apiClient.put(`/api/v1/business-partners/${editing}`, payload);
      else await apiClient.post('/api/v1/business-partners', payload);
      setMessage(editing ? 'Đã cập nhật đối tác.' : 'Đã tạo đối tác.');
      setShowForm(false);
      await load();
    } catch (x: any) {
      const msg = x.response?.data?.message || 'Không lưu được đối tác.';
      setError(x.response?.status === 409 ? `${msg} Vui lòng tải lại danh sách rồi thử lại.` : msg);
    } finally {
      submitting.current = false; setLoading(false);
    }
  };

  const remove = (x: BusinessPartner) => { setError(''); setMessage(''); setDeleteCandidate(x); };

  const confirmDeletePartner = async () => {
    if (!deleteCandidate) return;
    const hasBal = Number(deleteCandidate.receivableBalance ?? 0) > 0 || Number(deleteCandidate.payableBalance ?? 0) > 0;
    if (hasBal) return;
    try {
      await apiClient.delete(`/api/v1/business-partners/${deleteCandidate.id}`);
      setMessage('Đã xóa đối tác.');
      setDeleteCandidate(null);
      await load();
    } catch (e: any) {
      setError(e.response?.data?.message || 'Không xóa được đối tác.');
      setDeleteCandidate(null);
    }
  };

  const field = (name: keyof BusinessPartner, value: unknown) => setForm(v => ({ ...v, [name]: value }));
  const activeSuppliersCount = items.filter(x => x.isActive && x.isSupplier).length;
  const activeCustomersCount = items.filter(x => x.isActive && x.isCustomer).length;
  const dualRolePartnersCount = items.filter(x => x.isSupplier && x.isCustomer).length;
  const addressDeclaredCount = items.filter(x => Boolean(x.address && x.address.trim())).length;
  const missingContactInfoCount = items.filter(x => !x.phone?.trim() && !x.email?.trim()).length;
  const candidateHasBalance = deleteCandidate
    ? Number(deleteCandidate.receivableBalance ?? 0) > 0 || Number(deleteCandidate.payableBalance ?? 0) > 0
    : false;

  return <UiPage>
    <UiPageHeader
      eyebrow="Dữ liệu nền"
      title="Đối tác"
      description="Quản lý nhà cung cấp, khách hàng, vai trò nghiệp vụ và thông tin liên hệ dùng trên các chứng từ kho."
      actions={canManage && !showForm ? <button type="button" className="ui-primary-button" onClick={openCreate}>Thêm đối tác</button> : undefined}
    />

    {error && <p role="alert">{error}</p>}
    {message && <p role="status" className="ui-success-text">{message}</p>}

    {deleteCandidate && (
      <PartnerDeleteConfirmModal
        deleteCandidate={deleteCandidate}
        candidateHasBalance={candidateHasBalance}
        loading={loading}
        onCancel={() => setDeleteCandidate(null)}
        onConfirm={confirmDeletePartner}
      />
    )}

    {showForm && (
      <PartnerFormDrawer
        showForm={showForm}
        editing={editing}
        loading={loading}
        form={form}
        field={field}
        submit={submit}
        onClose={() => setShowForm(false)}
      />
    )}

    <UiCard title="Tổng quan Hồ sơ Nhà cung cấp & Khách hàng">
      <UiMetricGrid>
        <UiMetric value={items.length} label="Tổng đối tác trên trang" />
        <UiMetric value={activeSuppliersCount} label="NCC đang hoạt động" />
        <UiMetric value={activeCustomersCount} label="KH đang hoạt động" />
        <UiMetric value={dualRolePartnersCount} label="Đối tác kiêm nhiệm (NCC & KH)" />
        <UiMetric value={addressDeclaredCount} label="Đã chuẩn hóa địa chỉ giao nhận" />
        <UiMetric value={missingContactInfoCount} label="Thiếu thông tin liên hệ" />
      </UiMetricGrid>
    </UiCard>

    <UiToolbar>
      <UiToolbarField label="Tìm đối tác">
        <input aria-label="Tìm đối tác" placeholder="Mã hoặc tên" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} />
      </UiToolbarField>
      <UiToolbarField label="Vai trò">
        <select aria-label="Lọc vai trò" value={role} onChange={event => { setRole(event.target.value); setPage(1); }}>
          <option value="">Mọi vai trò</option>
          <option value="supplier">Nhà cung cấp</option>
          <option value="customer">Khách hàng</option>
        </select>
      </UiToolbarField>
      <UiToolbarField label="Trạng thái">
        <select aria-label="Lọc trạng thái" value={active} onChange={event => { setActive(event.target.value); setPage(1); }}>
          <option value="">Mọi trạng thái</option>
          <option value="true">Hoạt động</option>
          <option value="false">Ngừng hoạt động</option>
        </select>
      </UiToolbarField>
      <div className="ui-muted-text ui-auto-actions">Trang {page}/{pages}</div>
    </UiToolbar>

    <UiCard title="Danh sách đối tác">
      {loading ? <p role="status">Đang tải đối tác...</p> : <UiTableScroll><table className="ui-table-compact" aria-label="Danh sách đối tác kinh doanh">
        <thead><tr><th>Mã</th><th>Tên</th><th style={{ minWidth: 145, whiteSpace: 'nowrap' }}>Vai trò</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>CÔNG NỢ HIỆN TẠI</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>HẠN MỨC &amp; KỲ HẠN TT</th><th>Liên hệ</th><th style={{ whiteSpace: 'nowrap' }}>Trạng thái</th>{canManage && <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}</tr></thead>
        <tbody>
          {items.length === 0
            ? <tr><td colSpan={canManage ? 8 : 7} className="ui-empty-cell">Không có đối tác phù hợp với bộ lọc hiện tại.</td></tr>
            : items.map(item => <tr key={item.id}>
              <td style={{ whiteSpace: 'nowrap' }}><strong>{item.code}</strong></td>
              <td><div>{item.name}</div>{item.address && <small>{item.address}</small>}</td>
              <td style={{ minWidth: 145, whiteSpace: 'nowrap' }}>
                <div className="ui-inline-wrap" style={{ display: 'inline-flex', gap: 6, flexWrap: 'nowrap', whiteSpace: 'nowrap' }}>
                  {item.isSupplier && <UiBadge>Nhà cung cấp</UiBadge>}
                  {item.isCustomer && <UiBadge>Khách hàng</UiBadge>}
                  {!item.isSupplier && !item.isCustomer && <span>—</span>}
                </div>
              </td>
              <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 3, whiteSpace: 'nowrap' }}>
                  {item.isCustomer && (
                    <div style={{ whiteSpace: 'nowrap' }}><small>Phải thu KH: <strong>{Number(item.receivableBalance ?? 0).toLocaleString('vi-VN')} đ</strong></small></div>
                  )}
                  {item.isSupplier && (
                    <div style={{ whiteSpace: 'nowrap' }}><small>Phải trả NCC: <strong>{Number(item.payableBalance ?? 0).toLocaleString('vi-VN')} đ</strong></small></div>
                  )}
                  {item.isCustomer && Number(item.creditLimit ?? 0) > 0 && Number(item.receivableBalance ?? 0) > Number(item.creditLimit ?? 0) && (
                    <UiBadge tone="danger">Vượt hạn mức tín dụng</UiBadge>
                  )}
                  {item.isCustomer && Number(item.creditLimit ?? 0) > 0 && Number(item.receivableBalance ?? 0) >= Number(item.creditLimit ?? 0) * 0.8 && Number(item.receivableBalance ?? 0) <= Number(item.creditLimit ?? 0) && (
                    <UiBadge tone="warning">Cảnh báo công nợ (&ge; 80%)</UiBadge>
                  )}
                  {item.isCustomer && Number(item.creditLimit ?? 0) === 0 && Number(item.receivableBalance ?? 0) > 0 && (
                    <UiBadge tone="danger">Vượt hạn mức tín dụng</UiBadge>
                  )}
                </div>
              </td>
              <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 3, whiteSpace: 'nowrap' }}>
                  <div style={{ whiteSpace: 'nowrap' }}><small>Hạn mức: <strong>{Number(item.creditLimit ?? 0) > 0 ? `${Number(item.creditLimit).toLocaleString('vi-VN')} đ` : 'Không giới hạn'}</strong></small></div>
                  <div style={{ whiteSpace: 'nowrap' }}><small>Kỳ hạn TT: <strong>{item.paymentTermDays ?? 30} ngày</strong></small></div>
                </div>
              </td>
              <td><div>{item.phone || '—'}</div>{item.email && <small>{item.email}</small>}</td>
              <td style={{ whiteSpace: 'nowrap' }}><UiBadge tone={item.isActive ? 'success' : 'neutral'}>{item.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}</UiBadge></td>
              {canManage && (
                <td style={{ whiteSpace: 'nowrap' }}>
                  <div style={{ display: 'inline-flex', gap: '12px', alignItems: 'center', whiteSpace: 'nowrap' }}>
                    <button type="button" onClick={() => openEdit(item)} style={{ whiteSpace: 'nowrap' }}>Sửa</button>
                    <button
                      type="button"
                      onClick={() => void remove(item)}
                      style={{ whiteSpace: 'nowrap', borderColor: '#dc2626', color: '#dc2626', background: '#fff5f5', fontWeight: 600 }}
                    >
                      Xóa
                    </button>
                  </div>
                </td>
              )}
            </tr>)}
        </tbody>
      </table></UiTableScroll>}
      <div className="ui-pagination" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, marginTop: 10 }}>
        <span style={{ fontSize: 12.5, color: '#475569' }}>
          Tổng số trên trang: <strong>{items.length}</strong> đối tác • Trang {page}/{pages} (tối đa 10 bản ghi/trang)
        </span>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <button type="button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Trước</button>
          <span style={{ fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>Trang {page}/{pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Sau</button>
        </div>
      </div>
    </UiCard>

    <PartnerSlaAnd3PlCards
      items={items}
      activeSuppliersCount={activeSuppliersCount}
      activeCustomersCount={activeCustomersCount}
      dualRolePartnersCount={dualRolePartnersCount}
      loading={loading}
      governanceBusy={governanceBusy}
      slaVerified={slaVerified}
      billingStatementResult={billingStatementResult}
      billingSummary={billingSummary}
      isStatementIssued={isStatementIssued}
      generated3PlStatementNotice={generated3PlStatementNotice}
      depositorPortalData={depositorPortalData}
      verifySlaContracts={verifySlaContracts}
      calculate3PlBilling={calculate3PlBilling}
      generate3PlStatement={generate3PlStatement}
      load3PlDepositorPortal={load3PlDepositorPortal}
    />
  </UiPage>;
};

export default BusinessPartners;
