import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { sanitizeScannerCode } from './productCatalog';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import {
  emptyPartnerForm,
  PartnerDeleteConfirmModal,
  PartnerFormDrawer,
  PartnerSlaAnd3PlCards,
  type BillingSummary,
  type BusinessPartner,
  type DepositorPortalData,
} from '../modules/catalog/BusinessPartnerComponents';
import { BusinessPartnerListSection } from '../modules/catalog/BusinessPartnerListSection';

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
      setItems(Array.isArray(r.data?.items) ? r.data.items : Array.isArray(r.data) ? r.data : []);
      setPages(Math.max(1, Number(r.data?.totalPages ?? 1)));
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

    <BusinessPartnerListSection
      items={items}
      loading={loading}
      canManage={canManage}
      page={page}
      pages={pages}
      setPage={setPage}
      search={search}
      setSearch={setSearch}
      role={role}
      setRole={setRole}
      active={active}
      setActive={setActive}
      activeSuppliersCount={activeSuppliersCount}
      activeCustomersCount={activeCustomersCount}
      dualRolePartnersCount={dualRolePartnersCount}
      addressDeclaredCount={addressDeclaredCount}
      missingContactInfoCount={missingContactInfoCount}
      openEdit={openEdit}
      remove={remove}
    />

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
