import { useState, useEffect, useRef, useCallback } from 'react';
import apiClient from '../services/apiClient';
import { usePermission, currentUserId, hasPermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import ReceiptPrintPreview from '../components/ReceiptPrintPreview';
import ReceiptInventoryIdentityEditor from '../components/ReceiptInventoryIdentityEditor';
import { permissionError } from '../services/permissionPresentation';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';
import './ImportReceipts.css';

interface ImportReceiptDetail {
  id: number;
  productId: number;
  productCode: string;
  productName: string;
  operationUnitId: number; operationUnitCode: string; baseUnitCode: string; conversionFactor: number; conversionVersion: number;
  expectedQuantity: number; receivedQuantity: number; acceptedQuantity: number; damagedQuantity: number; rejectedQuantity: number;
  postedQuantity: number; baseExpectedQuantity: number; baseReceivedQuantity: number; baseAcceptedQuantity: number; baseDamagedQuantity: number; baseRejectedQuantity: number; basePostedQuantity: number;
  trackingType: 'None' | 'Lot' | 'Serial'; expiryControl: boolean; shelfLifeDays?: number | null;
  unitPrice?: number;
  note: string;
  requiresQc: boolean; qcState: string; qcPolicyId?: number; qcPolicyVersion?: number; qcPolicySource?: string;
  qcDispositionReasonCode?: string; qcDispositionNote?: string;
  observedQuantity: number; doorRejectedQuantity: number; finalReceivedQuantity: number; baseFinalReceivedQuantity: number;
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
  supplierId?: number;
  supplierCode?: string;
  supplierName?: string;
  requiresQc: boolean;
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
  unitId: number; unitCode: string; unitName: string; unitDecimalPlaces: number;
  uoms: Array<{ unitId:number; unitCode:string; unitName:string; decimalPlaces:number; conversionFactor:number; version:number }>;
}
interface Partner { id:number; code:string; name:string; isActive:boolean; }

interface ReceiptDetailForm {
  productId: number | '';
  operationUnitId: number | '';
  expectedQuantity: number | '';
  unitPrice: number | '';
  note: string;
}
type ReceiveLineForm = { receivedQuantity: number; acceptedQuantity: number; damagedQuantity: number; rejectedQuantity: number; reasonCode?: string; note?: string };
interface Discrepancy {
  id:number; receiptLineId:number; status:string; expectedQuantity:number; observedQuantity:number; normalizedObservedQuantity:number; differenceQuantity:number;
  doorRejectedQuantity:number; finalReceivedQuantity:number; operationUnitId:number; observedUnitId:number; observedUnitCode:string; operationUnitCode:string;
  baseUnitCode:string; conversionFactor:number; conversionVersion:number; rowVersion:string;
}
interface ReasonCode { code:string; name:string; version:number; requiresNote:boolean; requiresAttachment:boolean; requiresApproval:boolean; }
type ResolutionForm = { action:string; doorRejectedQuantity:number; reasonCode:string; responsibleParty:string; supplierClaimRequired:boolean; note:string; evidenceReference:string; confirmNormalizedObservation:boolean };

const ImportReceipts = () => {
  const canApprove = usePermission('receipt.complete');
  const canRead = usePermission('receipt.read');
  const canCreate = usePermission('receipt.create');
  const canUpdate = usePermission('receipt.update');
  const canCancel = usePermission('receipt.cancel');
  const canReceive = usePermission('receipt.receive');
  const canPost = usePermission('receipt.post');
  const canQc = usePermission('quality_inspection.execute');
  const canCompleteQc = usePermission('quality_inspection.complete');
  const canApproveQc = usePermission('quality_disposition.approve');
  const canReadDiscrepancy = usePermission('receiving_discrepancy.read');
  const canObserve = usePermission('receiving_discrepancy.create');
  const canSubmit = usePermission('receiving_discrepancy.submit');
  const canApproveDiscrepancy = usePermission('receiving_discrepancy.approve');
  const canRejectDiscrepancy = usePermission('receiving_discrepancy.reject');
  const canResolve = usePermission('receiving_discrepancy.resolve');
  const canReadReasons = usePermission('reason_code.read');
  const canReadWarehouses = usePermission('warehouse.read');
  const canReadProducts = usePermission('product.read');
  const canReadPartners = usePermission('partner.read');
  const userId = currentUserId();
  const [receipts, setReceipts] = useState<ImportReceipt[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [suppliers, setSuppliers] = useState<Partner[]>([]);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');
  const [selectedReceipt, setSelectedReceipt] = useState<ImportReceipt | null>(null);
  const [receiveLines, setReceiveLines] = useState<Record<number, ReceiveLineForm>>({});
  const [qcExcludedLines, setQcExcludedLines] = useState<number[]>([]);
  const [discrepancies, setDiscrepancies] = useState<Discrepancy[]>([]);
  const [reasonCodes, setReasonCodes] = useState<ReasonCode[]>([]);
  const [resolutionForms, setResolutionForms] = useState<Record<number, ResolutionForm>>({});
  const [approvingId, setApprovingId] = useState<number | null>(null);
  const [workflowId, setWorkflowId] = useState<number | null>(null);
  const createInFlight = useRef(false);
  const approveInFlight = useRef<number | null>(null);
  const cancelInFlight = useRef<number | null>(null);
  const workflowInFlight = useRef<number | null>(null);
  const partnerMutationInFlight = useRef(false);
  const detailRequest = useRef(0);
  const listRequest = useRef(0);
  const referenceRequest = useRef(0);
  const printRequest = useRef(0);
  const [creating, setCreating] = useState(false);
  const [partnerUpdating, setPartnerUpdating] = useState(false);
  const [printReceipt, setPrintReceipt] = useState<ImportReceipt | null>(null);
  const [printFetchedAt, setPrintFetchedAt] = useState<Date | null>(null);
  const [printLoadingId, setPrintLoadingId] = useState<number | null>(null);
  const printTriggerRef = useRef<HTMLButtonElement | null>(null);

  // Form states
  const [code, setCode] = useState('');
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [supplierId, setSupplierId] = useState<number | ''>('');
  const [details, setDetails] = useState<ReceiptDetailForm[]>([]);

  const fetchData = useCallback(async () => {
    const request = ++referenceRequest.current;
    try {
      const [whRes, prRes, bpRes] = await Promise.all([
        canReadWarehouses ? apiClient.get('/api/warehouses') : Promise.resolve({ data: [] }),
        canReadProducts ? apiClient.get('/api/products') : Promise.resolve({ data: [] }),
        canReadPartners ? apiClient.get('/api/business-partners', { params: { role: 'supplier', pageSize: 100 } }) : Promise.resolve({ data: { items: [] } })
      ]);
      if (request !== referenceRequest.current) return;
      setWarehouses(hasPermission('warehouse.read') ? whRes.data : []);
      setProducts(hasPermission('product.read') ? prRes.data : []);
      setSuppliers(hasPermission('partner.read') ? bpRes.data.items : []);
    } catch (err: any) {
      if (request !== referenceRequest.current) return;
      console.error(err);
      setWarehouses([]); setProducts([]); setSuppliers([]);
      setError(permissionError(err, 'Không thể tải dữ liệu tham khảo. Vui lòng thử lại.'));
    }
  }, [canReadWarehouses, canReadProducts, canReadPartners]);

  const fetchReceipts = useCallback(async () => {
    const request = ++listRequest.current;
    if (!canRead) return;
    try {
      const res = await apiClient.get('/api/importreceipts');
      if (request !== listRequest.current) return;
      setReceipts(hasPermission('receipt.read') ? res.data : []);
    } catch (err: any) {
      if (request !== listRequest.current) return;
      console.error(err);
      setReceipts([]); setSelectedReceipt(null); setPrintReceipt(null);
      setError(permissionError(err, 'Không thể tải danh sách phiếu nhập. Vui lòng thử lại.'));
    }
  }, [canRead]);

  useEffect(() => {
    fetchData();
    fetchReceipts();
    const references = referenceRequest, lists = listRequest;
    return () => { references.current++; lists.current++; };
  }, [fetchData, fetchReceipts]);

  useEffect(() => {
    const details = detailRequest, prints = printRequest;
    return () => { details.current++; prints.current++; };
  }, [canRead]);

  useEffect(() => {
    if (!canRead) { detailRequest.current++; setReceipts([]); setSelectedReceipt(null); setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(null); setReceiveLines({}); }
    if (!canReadDiscrepancy) { setDiscrepancies([]); setResolutionForms({}); }
    if (!canReadReasons) setReasonCodes([]);
    if (!canReadPartners) setSuppliers([]);
    if (!canReadProducts) setProducts([]);
    if (!canReadWarehouses) setWarehouses([]);
  }, [canRead, canReadDiscrepancy, canReadReasons, canReadPartners, canReadProducts, canReadWarehouses]);

  const handleApprove = async (id: number) => {
    if (!canApprove || (receipts.find(r => r.id === id)?.status === 'QcCompleted' && !canApproveQc) || approveInFlight.current !== null) return;
    approveInFlight.current = id;
    setApprovingId(id);
    setError('');
    try {
      const action = `import-approve:${id}`;
      await apiClient.post(`/api/importreceipts/${id}/approve`, undefined, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      alert('Duyệt thành công');
      await fetchReceipts();
      if (selectedReceipt?.id === id) {
        await handleViewDetails(id);
      }
    } catch (err: any) {
      if (selectedReceipt?.id === id) setSelectedReceipt(null);
      setError(permissionError(err) || 'Lỗi khi duyệt phiếu');
    } finally {
      approveInFlight.current = null;
      setApprovingId(null);
    }
  };

  const handleCancel = async (id: number) => {
    if (!canCancel || cancelInFlight.current !== null) return;
    if (!window.confirm('Bạn có chắc chắn muốn hủy phiếu nhập này?')) return;
    cancelInFlight.current = id;
    try {
      const action = `import-cancel:${id}`;
      await apiClient.put(`/api/importreceipts/${id}/cancel`, undefined, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      alert('Hủy thành công');
      fetchReceipts();
      if (selectedReceipt?.id === id) {
          handleViewDetails(id);
      }
    } catch (err: any) {
      if (selectedReceipt?.id === id) setSelectedReceipt(null);
      setError(permissionError(err) || 'Lỗi khi hủy phiếu');
    } finally { cancelInFlight.current = null; }
  };

  const handleViewDetails = async (id: number) => {
    if (!canRead) return;
    const request = ++detailRequest.current;
    setSelectedReceipt(null); setDiscrepancies([]); setReasonCodes([]); setResolutionForms({});
    try {
      const res = await apiClient.get(`/api/importreceipts/${id}`);
      if (request !== detailRequest.current || !hasPermission('receipt.read')) return;
      setSelectedReceipt(res.data);
      setQcExcludedLines([]);
      setDiscrepancies([]); setReasonCodes([]); setResolutionForms({});
      setReceiveLines(Object.fromEntries((res.data.details as ImportReceiptDetail[]).map(d => [d.id, { receivedQuantity: d.receivedQuantity || d.expectedQuantity, acceptedQuantity: d.requiresQc ? d.acceptedQuantity : d.expectedQuantity, damagedQuantity: d.damagedQuantity, rejectedQuantity: d.rejectedQuantity, reasonCode: d.qcDispositionReasonCode || '', note: d.qcDispositionNote || '' }])));
      const [discrepancyRes, reasonRes] = await Promise.allSettled([
        canReadDiscrepancy ? apiClient.get(`/api/importreceipts/${id}/discrepancies`) : Promise.resolve({ data: [] }),
        canReadReasons ? apiClient.get('/api/importreceipts/discrepancy-reasons') : Promise.resolve({ data: [] }),
      ]);
      if (request !== detailRequest.current || !hasPermission('receipt.read')) return;
      setDiscrepancies(hasPermission('receiving_discrepancy.read') && discrepancyRes.status === 'fulfilled' ? discrepancyRes.value.data : []);
      setReasonCodes(hasPermission('reason_code.read') && reasonRes.status === 'fulfilled' ? reasonRes.value.data : []);
    } catch (err: any) {
      if (request !== detailRequest.current) return;
      setSelectedReceipt(null);
      setDiscrepancies([]);
      setError(permissionError(err) || 'Lỗi khi tải chi tiết phiếu');
    }
  };

  const observeReceipt = async (id:number, receipt?:ImportReceipt) => {
    if (!canObserve || workflowInFlight.current !== null) return;
    workflowInFlight.current=id; setWorkflowId(id); setError('');
    try {
      const source=receipt || (await apiClient.get(`/api/importreceipts/${id}`)).data as ImportReceipt;
      const lines=(source.details||[]).map(d=>({lineId:d.id,observedQuantity:receiveLines[d.id]?.receivedQuantity ?? d.expectedQuantity,observedUnitId:d.operationUnitId,items:[]}));
      if(lines.some(x=>x.observedQuantity<0)) throw new Error('Số lượng quan sát không được âm.');
      const action=`import-discrepancy-observe:${id}`;
      await apiClient.post(`/api/importreceipts/${id}/discrepancies/observe`,{lines},{headers:idempotencyHeaders(action)});
      completeIdempotentAction(action); setSuccessMsg('Đã ghi nhận số lượng thực tế. Tồn kho chưa thay đổi.');
      await fetchReceipts(); if(selectedReceipt?.id===id) await handleViewDetails(id);
    } catch(err:any) {
      if(err.response && selectedReceipt?.id===id) setSelectedReceipt(null);
      setError(err.response?.status===409?'Dữ liệu đã thay đổi. Vui lòng tải lại phiếu.':permissionError(err)||err.message||'Không thể ghi nhận số lượng.');
    } finally { workflowInFlight.current=null; setWorkflowId(null); }
  };

  const submitResolution = async (d:Discrepancy) => {
    if(!canSubmit || workflowInFlight.current!==null || !selectedReceipt) return;
    workflowInFlight.current=selectedReceipt.id; setWorkflowId(selectedReceipt.id); setError('');
    try {
      const form=resolutionForms[d.id]||{action:'ACCEPT_OBSERVED',doorRejectedQuantity:0,reasonCode:'',responsibleParty:'UNKNOWN',supplierClaimRequired:false,note:'',evidenceReference:'',confirmNormalizedObservation:false};
      if(!form.reasonCode) throw new Error('Chọn mã lý do.');
      if((form.responsibleParty==='UNKNOWN'||reasonCodes.find(x=>x.code===form.reasonCode)?.requiresNote)&&!form.note.trim()) throw new Error('Lý do hoặc bên chịu trách nhiệm này yêu cầu ghi chú.');
      const action=`import-discrepancy-submit:${selectedReceipt.id}:${d.id}`;
      await apiClient.post(`/api/importreceipts/${selectedReceipt.id}/discrepancies/${d.id}/submit`,{...form,rowVersion:d.rowVersion},{headers:idempotencyHeaders(action)});
      completeIdempotentAction(action); setSuccessMsg('Đã gửi phương án xử lý. Tồn kho chưa thay đổi.');
      await fetchReceipts(); await handleViewDetails(selectedReceipt.id);
    } catch(err:any) {
      if(err.response) { setSelectedReceipt(null); setDiscrepancies([]); }
      setError(err.response?.status===409?'Phương án xử lý đã thay đổi. Vui lòng tải lại phiếu.':permissionError(err)||err.message||'Không thể gửi phương án xử lý.');
    } finally { workflowInFlight.current=null; setWorkflowId(null); }
  };

  const reviewResolution = async (d:Discrepancy, decision:'approve'|'reject') => {
    if (decision === 'approve' ? !canApproveDiscrepancy : !canRejectDiscrepancy) return;
    if(workflowInFlight.current!==null || !selectedReceipt) return;
    workflowInFlight.current=selectedReceipt.id; setWorkflowId(selectedReceipt.id); setError('');
    try {
      const action=`import-discrepancy-${decision}:${selectedReceipt.id}:${d.id}`;
      await apiClient.post(`/api/importreceipts/${selectedReceipt.id}/discrepancies/${d.id}/${decision}`,d.rowVersion,{headers:idempotencyHeaders(action)});
      completeIdempotentAction(action); await fetchReceipts(); await handleViewDetails(selectedReceipt.id);
    } catch(err:any) {
      if(err.response) { setSelectedReceipt(null); setDiscrepancies([]); }
      setError(err.response?.status===409?'Phương án xử lý đã thay đổi. Vui lòng tải lại phiếu.':permissionError(err)||'Không thể duyệt phương án xử lý.');
    } finally { workflowInFlight.current=null; setWorkflowId(null); }
  };

  const recountDiscrepancy = async (d:Discrepancy) => {
    if (!canResolve) return;
    if(workflowInFlight.current!==null || !selectedReceipt) return;
    workflowInFlight.current=selectedReceipt.id; setWorkflowId(selectedReceipt.id); setError('');
    try {
      const quantity=receiveLines[d.receiptLineId]?.receivedQuantity ?? d.observedQuantity;
      const action=`import-discrepancy-recount:${selectedReceipt.id}:${d.id}:${quantity}`;
      await apiClient.post(`/api/importreceipts/${selectedReceipt.id}/discrepancies/${d.id}/recount`,{observedQuantity:quantity,observedUnitId:d.operationUnitId,voidedObservationItemIds:[],items:[],rowVersion:d.rowVersion},{headers:idempotencyHeaders(action)});
      completeIdempotentAction(action); await handleViewDetails(selectedReceipt.id);
    } catch(err:any) {
      if(err.response) { setSelectedReceipt(null); setDiscrepancies([]); }
      setError(err.response?.status===409?'Số lượng quan sát đã thay đổi. Vui lòng tải lại phiếu.':permissionError(err)||'Không thể kiểm đếm lại.');
    } finally { workflowInFlight.current=null; setWorkflowId(null); }
  };

  const handleAddDetail = () => {
    setDetails([...details, { productId: '', operationUnitId: '', expectedQuantity: '', unitPrice: '', note: '' }]);
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
    if (!canCreate || createInFlight.current) return;
    setError('');
    setSuccessMsg('');

    // Validation
    if (!code.trim()) return setError('Vui lòng nhập mã phiếu');
    if (warehouseId === '') return setError('Vui lòng chọn kho');
    if (details.length === 0) return setError('Cần ít nhất 1 dòng chi tiết');

    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn sản phẩm`);
      if (d.expectedQuantity === '' || Number(d.expectedQuantity) <= 0) return setError(`Dòng ${i + 1}: Số lượng dự kiến phải > 0`);
      if (d.operationUnitId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn Đơn vị thao tác`);
      if (d.unitPrice === '' || Number(d.unitPrice) < 0) return setError(`Dòng ${i + 1}: Đơn giá phải >= 0`);
    }

    createInFlight.current = true; setCreating(true);
    try {
      const payload = {
        code: code.trim(),
        warehouseId: Number(warehouseId),
        supplierId: supplierId === '' ? null : Number(supplierId),
        note,
        details: details.map(d => ({
          productId: Number(d.productId),
          operationUnitId: Number(d.operationUnitId),
          expectedQuantity: Number(d.expectedQuantity),
          unitPrice: Number(d.unitPrice),
          note: d.note
        }))
      };

      const action = `import-create:${JSON.stringify(payload)}`;
      await apiClient.post('/api/importreceipts', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg('Tạo phiếu nháp thành công!');
      
      // Reset form
      setCode('');
      setWarehouseId('');
      setSupplierId('');
      setNote('');
      setDetails([]);
      
      fetchReceipts();
    } catch (err: any) {
      setError(permissionError(err) || 'Lỗi khi tạo phiếu nhập');
    } finally {
      createInFlight.current = false; setCreating(false);
    }
  };

  const runWorkflow = async (id: number, command: 'receive' | 'qc-disposition' | 'post', receipt?: ImportReceipt) => {
    if (command === 'receive' ? !canReceive : command === 'qc-disposition' ? !canQc : !canPost) return;
    if (workflowInFlight.current !== null) return;
    workflowInFlight.current = id;
    setWorkflowId(id); setError('');
    try {
      const action = `import-${command}:${id}`;
      const source = !receipt && (command === 'receive' || command === 'qc-disposition' || command === 'post') ? (await apiClient.get(`/api/importreceipts/${id}`)).data as ImportReceipt : receipt;
      const body = command === 'receive' ? { lines: (source?.details || []).map(d => ({ lineId: d.id, ...(receiveLines[d.id] || { receivedQuantity: d.expectedQuantity, acceptedQuantity: d.requiresQc ? 0 : d.expectedQuantity, damagedQuantity: 0, rejectedQuantity: 0 }) })) }
        : command === 'qc-disposition' ? { lines: (source?.details || []).filter(d => d.requiresQc && d.qcState === 'QcPending' && !qcExcludedLines.includes(d.id)).map(d => ({ lineId: d.id, ...(receiveLines[d.id] || { acceptedQuantity: 0, damagedQuantity: 0, rejectedQuantity: 0 }), reasonCode: receiveLines[d.id]?.reasonCode, note: receiveLines[d.id]?.note })) } : undefined;
      if (command === 'receive' && body?.lines.some(line => line.receivedQuantity <= 0 || line.acceptedQuantity < 0 || (!source?.details.find(d => d.id === line.lineId)?.requiresQc && line.acceptedQuantity + line.damagedQuantity + line.rejectedQuantity !== line.receivedQuantity)))
        throw new Error('Tổng chấp nhận, hư hỏng và từ chối phải bằng số lượng nhận.');
      if (command === 'qc-disposition' && body?.lines.some(line => line.acceptedQuantity < 0 || line.damagedQuantity < 0 || line.rejectedQuantity < 0 || line.acceptedQuantity + line.damagedQuantity + line.rejectedQuantity !== source?.details.find(d => d.id === line.lineId)?.receivedQuantity || ((line.damagedQuantity > 0 || line.rejectedQuantity > 0) && !line.reasonCode?.trim())))
        throw new Error('Tổng chấp nhận, hư hỏng và từ chối phải bằng số lượng nhận; lượng hư hỏng hoặc từ chối cần mã lý do.');
      if (command === 'qc-disposition') {
        if (!body?.lines.length) return setError('Chọn ít nhất một dòng để ghi kết quả kiểm tra chất lượng.');
        const pendingCount = source?.details.filter(d => d.requiresQc && d.qcState === 'QcPending').length;
        if (body.lines.length === pendingCount && !canCompleteQc) return setError('Bạn không có quyền hoàn tất kiểm tra chất lượng. Hãy ghi kết quả từng phần hoặc liên hệ người có quyền.');
      }
      if (command === 'post') {
        const available = source?.details.reduce((sum, line) => sum + line.baseAcceptedQuantity, 0) ?? 0;
        const damaged = source?.details.reduce((sum, line) => sum + line.damagedQuantity * line.conversionFactor, 0) ?? 0;
        const rejected = source?.details.reduce((sum, line) => sum + line.rejectedQuantity * line.conversionFactor, 0) ?? 0;
        if (!window.confirm(`Ghi nhận tồn kho sẽ tăng lượng có thể sử dụng ${available}, hư hỏng ${damaged}, hàng bị từ chối ${rejected} theo đơn vị cơ sở đã lưu. Tiếp tục?`)) return;
      }
      await apiClient.post(`/api/importreceipts/${id}/${command}`, body, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg(command === 'receive' ? 'Đã hoàn tất nhận hàng. Tồn kho chưa thay đổi.' : command === 'qc-disposition' ? 'Đã ghi nhận kết quả kiểm tra chất lượng. Tồn kho chưa thay đổi.' : 'Đã ghi nhận tồn kho phiếu theo từng trạng thái tồn kho.');
      await fetchReceipts();
      if (selectedReceipt?.id === id) await handleViewDetails(id);
    } catch (err: any) { if (err.response && selectedReceipt?.id === id) setSelectedReceipt(null); setError(err.response?.status === 409 ? 'Dữ liệu đã thay đổi. Vui lòng tải lại phiếu.' : permissionError(err) || 'Không thể xử lý phiếu nhập'); }
    finally { workflowInFlight.current = null; setWorkflowId(null); }
  };

  const statusLabel = (status: string) => ({ Draft: 'Nháp', DiscrepancyPending:'Chờ xử lý sai lệch',DiscrepancySubmitted:'Đã gửi xử lý sai lệch',DiscrepancyPendingApproval:'Chờ duyệt sai lệch',DiscrepancyResolved:'Sai lệch đã xử lý',DiscrepancyRejected:'Sai lệch bị trả lại', Received: 'Đã nhận — chưa ghi tồn', QcPending: 'Chờ kết quả kiểm tra chất lượng', QcCompleted: 'Kiểm tra chất lượng đã hoàn tất — chờ duyệt', ReadyToPost: 'Sẵn sàng ghi nhận tồn kho', Posted: 'Đã ghi nhận tồn kho', Approved: 'Đã duyệt (dữ liệu cũ)', Cancelled: 'Đã hủy' }[status] || 'Trạng thái chưa xác định');

  const statusTone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' => {
    if (status === 'Cancelled' || status === 'DiscrepancyRejected') return 'danger';
    if (status === 'Posted' || status === 'QcCompleted' || status === 'ReadyToPost') return 'success';
    if (status === 'Draft' || status.includes('Pending') || status === 'Received') return 'warning';
    return 'neutral';
  };

  const changeSupplier = async (value: number | null) => {
    if (!canUpdate || !canReadPartners || !selectedReceipt || partnerMutationInFlight.current) return;
    partnerMutationInFlight.current = true; setPartnerUpdating(true); setError('');
    try { await apiClient.put(`/api/importreceipts/${selectedReceipt.id}/supplier`, { partnerId: value }); await fetchReceipts(); await handleViewDetails(selectedReceipt.id); }
    catch (x: any) { setError(permissionError(x) || 'Không đổi được nhà cung cấp.'); }
    finally { partnerMutationInFlight.current = false; setPartnerUpdating(false); }
  };

  const openPrintPreview = async (id: number, trigger: HTMLButtonElement) => {
    const request = ++printRequest.current;
    printTriggerRef.current = trigger; setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(id); setError('');
    try { const res = await apiClient.get(`/api/importreceipts/${id}`); if (request === printRequest.current && hasPermission('receipt.read')) { setPrintReceipt(res.data); setPrintFetchedAt(new Date()); } }
    catch (x: any) { if (request === printRequest.current) setError(permissionError(x, 'Không tải được dữ liệu bản in.')); }
    finally { if (request === printRequest.current) setPrintLoadingId(null); }
  };
  const closePrintPreview = () => { printRequest.current++; setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(null); queueMicrotask(() => printTriggerRef.current?.focus()); };

  return (
    <UiPage>
      <div className="import-receipts">
        <UiPageHeader
          eyebrow="Inbound"
          title="Quản lý nhập kho"
          description="Tạo và theo dõi phiếu nhập từ nhận hàng, xử lý sai lệch, kiểm tra chất lượng đến ghi nhận tồn kho."
        />

        {error && <p role="alert">{error}</p>}
        {successMsg && <p role="status" className="ui-success-text">{successMsg}</p>}

        {canCreate && (
          <UiCard title="Tạo Phiếu Nhập (Nháp)">
            <form onSubmit={handleCreate} className="import-form">
              <div className="import-form-grid">
                <label className="ui-stack">
                  <span>Mã phiếu</span>
                  <input aria-label="Mã phiếu" value={code} onChange={e => setCode(e.target.value)} required />
                </label>
                <label className="ui-stack">
                  <span>Kho</span>
                  <select aria-label="Kho" value={warehouseId} onChange={e => setWarehouseId(e.target.value ? Number(e.target.value) : '')} required>
                    <option value="">-- Chọn kho --</option>
                    {warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Ghi chú phiếu</span>
                  <input aria-label="Ghi chú phiếu" value={note} onChange={e => setNote(e.target.value)} />
                </label>
                <label className="ui-stack">
                  <span>Nhà cung cấp</span>
                  <select aria-label="Nhà cung cấp" value={supplierId} onChange={e => setSupplierId(e.target.value ? Number(e.target.value) : '')}>
                    <option value="">-- Không chọn --</option>
                    {suppliers.filter(x => x.isActive).map(x => <option key={x.id} value={x.id}>{x.code} - {x.name}</option>)}
                  </select>
                </label>
              </div>

              <h3>Chi tiết phiếu</h3>
              <div className="ui-stack">
                {details.map((d, i) => (
                  <div key={i} className="import-line">
                    <label className="ui-stack">
                      <span>Sản phẩm</span>
                      <select
                        aria-label={'Sản phẩm dòng ' + (i + 1)}
                        value={d.productId}
                        onChange={e => {
                          const productId = e.target.value ? Number(e.target.value) : '';
                          const product = products.find(x => x.id === productId);
                          const next = [...details];
                          next[i] = { ...next[i], productId, operationUnitId: product?.unitId || '' };
                          setDetails(next);
                        }}
                        required
                      >
                        <option value="">-- Chọn sản phẩm --</option>
                        {products.map(p => <option key={p.id} value={p.id}>{p.code} - {p.name}</option>)}
                      </select>
                    </label>

                    <label className="ui-stack">
                      <span>Đơn vị thao tác</span>
                      <select aria-label={'Đơn vị thao tác dòng ' + (i + 1)} value={d.operationUnitId} onChange={e => handleDetailChange(i, 'operationUnitId', e.target.value ? Number(e.target.value) : '')} required>
                        <option value="">-- Đơn vị thao tác --</option>
                        {(products.find(x => x.id === d.productId)?.uoms || []).map(u => <option key={u.unitId + '-' + u.version} value={u.unitId}>{u.unitCode} — × {u.conversionFactor}</option>)}
                      </select>
                    </label>

                    <label className="ui-stack">
                      <span>Số lượng dự kiến</span>
                      <input
                        type="number"
                        aria-label={'Số lượng dự kiến dòng ' + (i + 1)}
                        placeholder="Số lượng dự kiến"
                        value={d.expectedQuantity}
                        onChange={e => handleDetailChange(i, 'expectedQuantity', e.target.value ? Number(e.target.value) : '')}
                        required
                        min="0.01"
                        step="0.01"
                      />
                    </label>

                    <label className="ui-stack">
                      <span>Đơn giá</span>
                      <input
                        type="number"
                        aria-label={'Đơn giá dòng ' + (i + 1)}
                        placeholder="Đơn giá"
                        value={d.unitPrice}
                        onChange={e => handleDetailChange(i, 'unitPrice', e.target.value ? Number(e.target.value) : '')}
                        required
                        min="0"
                        step="0.01"
                      />
                    </label>

                    <label className="ui-stack">
                      <span>Ghi chú</span>
                      <input
                        aria-label={'Ghi chú dòng ' + (i + 1)}
                        placeholder="Ghi chú"
                        value={d.note}
                        onChange={e => handleDetailChange(i, 'note', e.target.value)}
                      />
                    </label>

                    <div className="import-actions">
                      <button type="button" aria-label={'Xóa dòng ' + (i + 1)} onClick={() => handleRemoveDetail(i)}>Xóa</button>
                    </div>

                    {(() => {
                      const p = products.find(x => x.id === d.productId);
                      const u = p?.uoms?.find(x => x.unitId === d.operationUnitId);
                      return p && u && d.expectedQuantity !== ''
                        ? <div className="import-line-summary">{d.expectedQuantity} {u.unitCode} = {Number(d.expectedQuantity) * u.conversionFactor} {p.unitCode} Đơn vị cơ sở</div>
                        : null;
                    })()}
                  </div>
                ))}
              </div>

              <div className="import-section-actions">
                <button type="button" onClick={handleAddDetail}>+ Thêm dòng</button>
                <button type="submit" disabled={creating}>{creating ? 'Đang lưu...' : 'Lưu Phiếu Nháp'}</button>
              </div>
            </form>
          </UiCard>
        )}

        <UiCard title="Danh Sách Phiếu Nhập">
          <UiTableScroll>
            <table aria-label="Danh sách phiếu nhập">
              <thead>
                <tr>
                  <th>Mã phiếu</th>
                  <th>Ngày tạo</th>
                  <th>Kho</th>
                  <th>Nhà cung cấp</th>
                  <th>Người tạo</th>
                  <th>Trạng thái</th>
                  <th>Hành động</th>
                </tr>
              </thead>
              <tbody>
                {receipts.map(r => (
                  <tr key={r.id}>
                    <td><strong>{r.code}</strong></td>
                    <td>{new Date(r.createdAt).toLocaleString('vi-VN')}</td>
                    <td>{r.warehouseName}</td>
                    <td>{r.supplierCode ? r.supplierCode + ' - ' + r.supplierName : '—'}</td>
                    <td>{r.createdByName}</td>
                    <td className="import-status-cell"><UiBadge tone={statusTone(r.status)}>{statusLabel(r.status)}</UiBadge></td>
                    <td>
                      <div className="import-row-actions">
                        <button type="button" onClick={() => handleViewDetails(r.id)}>Chi tiết</button>
                        <button type="button" disabled={printLoadingId !== null} onClick={e => void openPrintPreview(r.id, e.currentTarget)}>
                          {printLoadingId === r.id ? 'Đang tải bản in...' : 'Xem bản in'}
                        </button>
                        {r.status === 'Draft' && (
                          <>
                            {canObserve && <button type="button" disabled={workflowId === r.id} onClick={() => void observeReceipt(r.id)}>{workflowId === r.id ? 'Đang ghi nhận...' : 'Ghi nhận số lượng thực tế'}</button>}
                            {canCancel && <button type="button" onClick={() => handleCancel(r.id)}>Hủy</button>}
                          </>
                        )}
                        {(r.status === 'Received' || (r.status === 'QcCompleted' && canApproveQc)) && canApprove && r.createdBy !== userId && (
                          <button type="button" onClick={() => handleApprove(r.id)} disabled={approvingId === r.id}>
                            {approvingId === r.id ? 'Đang duyệt...' : 'Duyệt để ghi nhận tồn kho'}
                          </button>
                        )}
                        {r.status === 'ReadyToPost' && canPost && r.createdBy !== userId && (
                          <button type="button" disabled={workflowId === r.id} onClick={() => void runWorkflow(r.id, 'post')}>
                            {workflowId === r.id ? 'Đang ghi nhận tồn kho...' : 'Ghi nhận tồn kho'}
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {receipts.length === 0 && <tr><td colSpan={7} className="ui-empty-cell">Chưa có phiếu nhập</td></tr>}
              </tbody>
            </table>
          </UiTableScroll>
        </UiCard>

        {selectedReceipt && (
          <UiCard title={'Chi Tiết Phiếu Nhập: ' + selectedReceipt.code}>
            <div className="import-detail-meta">
              <div><strong>Kho:</strong> {selectedReceipt.warehouseName}</div>
              <div><strong>Nhà cung cấp:</strong> {selectedReceipt.supplierCode ? selectedReceipt.supplierCode + ' - ' + selectedReceipt.supplierName : '—'}</div>
              {selectedReceipt.status === 'Draft' && canUpdate && canReadPartners && (
                <div>
                  <label className="ui-stack">
                    <span>Đổi nhà cung cấp</span>
                    <select aria-label="Đổi nhà cung cấp" disabled={partnerUpdating} value={selectedReceipt.supplierId || ''} onChange={e => void changeSupplier(e.target.value ? Number(e.target.value) : null)}>
                      <option value="">-- Gỡ liên kết --</option>
                      {suppliers.filter(x => x.isActive || x.id === selectedReceipt.supplierId).map(x => <option key={x.id} value={x.id}>{x.code} - {x.name}{x.isActive ? '' : ' (ngừng hoạt động)'}</option>)}
                    </select>
                  </label>
                  {partnerUpdating && <span role="status">Đang cập nhật...</span>}
                </div>
              )}
              <div><strong>Trạng thái:</strong> <UiBadge tone={statusTone(selectedReceipt.status)}>{statusLabel(selectedReceipt.status)}</UiBadge></div>
              <div><strong>Người tạo:</strong> {selectedReceipt.createdByName}</div>
              <div><strong>Ngày tạo:</strong> {new Date(selectedReceipt.createdAt).toLocaleString('vi-VN')}</div>
              {['Approved', 'ReadyToPost', 'Posted'].includes(selectedReceipt.status) && selectedReceipt.approvedAt && (
                <>
                  <div><strong>Người duyệt:</strong> {selectedReceipt.approvedByName}</div>
                  <div><strong>Ngày duyệt:</strong> {new Date(selectedReceipt.approvedAt).toLocaleString('vi-VN')}</div>
                </>
              )}
              <div><strong>Ghi chú:</strong> {selectedReceipt.note || '—'}</div>
            </div>

            <UiTableScroll>
              <table aria-label={'Chi tiết phiếu nhập ' + selectedReceipt.code}>
                <thead>
                  <tr>
                    <th>Mã SP</th>
                    <th>Tên SP</th>
                    <th className="import-numeric">Dự kiến</th>
                    <th className="import-numeric">Nhận / Chấp nhận</th>
                    <th className="import-numeric">Đơn vị cơ sở</th>
                    <th>Ghi chú</th>
                  </tr>
                </thead>
                <tbody>
                  {selectedReceipt.details.map(d => (
                    <tr key={d.id}>
                      <td>{d.productCode}</td>
                      <td>{d.productName}<br /><small>Kiểm tra chất lượng: {d.requiresQc ? 'Bắt buộc · ' + statusLabel(d.qcState) + ' · chính sách ' + (d.qcPolicyId ?? '—') + ' phiên bản ' + (d.qcPolicyVersion ?? '—') : 'Không yêu cầu'}</small></td>
                      <td className="import-numeric">{d.expectedQuantity} {d.operationUnitCode}</td>
                      <td className="import-numeric">
                        {selectedReceipt.status === 'Draft' && (canReceive || canObserve) ? (
                          <div className="import-quantity-editor">
                            <label>Số lượng nhận <input aria-label={'Số lượng nhận ' + d.productCode} type="number" min="0" step="any" value={receiveLines[d.id]?.receivedQuantity ?? d.expectedQuantity} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...(x[d.id] || { receivedQuantity: d.expectedQuantity, acceptedQuantity: d.expectedQuantity, damagedQuantity: 0, rejectedQuantity: 0 }), receivedQuantity: Number(e.target.value) } }))} /></label>
                            {!d.requiresQc && <label>Số lượng chấp nhận <input aria-label={'Số lượng chấp nhận ' + d.productCode} type="number" min="0" step="any" value={receiveLines[d.id]?.acceptedQuantity ?? d.expectedQuantity} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...(x[d.id] || { receivedQuantity: d.expectedQuantity, acceptedQuantity: d.expectedQuantity, damagedQuantity: 0, rejectedQuantity: 0 }), acceptedQuantity: Number(e.target.value) } }))} /></label>}
                            <span>{d.requiresQc ? 'Kết quả kiểm tra được nhập sau khi nhận; chưa ghi tồn.' : 'Hư hỏng: 0 · Từ chối: 0 (không kiểm tra chất lượng)'}</span>
                          </div>
                        ) : selectedReceipt.status === 'QcPending' && canQc && d.requiresQc && d.qcState === 'QcPending' ? (
                          <div className="import-quantity-editor">
                            <label className="import-resolution-check">
                              <input
                                type="checkbox"
                                aria-label={'Ghi kết quả kiểm tra ' + d.productCode}
                                checked={!qcExcludedLines.includes(d.id)}
                                onChange={e => setQcExcludedLines(ids => e.target.checked ? ids.filter(id => id !== d.id) : [...ids, d.id])}
                              />
                              Ghi kết quả dòng này
                            </label>
                            <label>Chấp nhận <input aria-label={'Kiểm tra chất lượng: chấp nhận ' + d.productCode} type="number" min="0" step="any" value={receiveLines[d.id]?.acceptedQuantity ?? 0} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...x[d.id], acceptedQuantity: Number(e.target.value) } }))} /></label>
                            <label>Hư hỏng <input aria-label={'Kiểm tra chất lượng: hư hỏng ' + d.productCode} type="number" min="0" step="any" value={receiveLines[d.id]?.damagedQuantity ?? 0} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...x[d.id], damagedQuantity: Number(e.target.value) } }))} /></label>
                            <label>Từ chối giữ tại kho <input aria-label={'Kiểm tra chất lượng: từ chối ' + d.productCode} type="number" min="0" step="any" value={receiveLines[d.id]?.rejectedQuantity ?? 0} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...x[d.id], rejectedQuantity: Number(e.target.value) } }))} /></label>
                            <label>Mã lý do <input aria-label={'Lý do kiểm tra ' + d.productCode} value={receiveLines[d.id]?.reasonCode ?? ''} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...x[d.id], reasonCode: e.target.value } }))} /></label>
                            <small>Tổng phải bằng {d.receivedQuantity} {d.operationUnitCode}</small>
                          </div>
                        ) : (
                          <>{d.receivedQuantity} / {d.acceptedQuantity} {d.operationUnitCode}<br /><small>Hư hỏng: {d.damagedQuantity}; Từ chối: {d.rejectedQuantity}</small></>
                        )}
                      </td>
                      <td className="import-numeric">× {d.conversionFactor} (v{d.conversionVersion})<br />{selectedReceipt.status === 'Draft' ? (receiveLines[d.id]?.acceptedQuantity ?? d.expectedQuantity) * d.conversionFactor : d.baseAcceptedQuantity || d.baseExpectedQuantity} {d.baseUnitCode}</td>
                      <td>{d.note}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>

            {selectedReceipt.status !== 'Draft' && (
              <ReceiptInventoryIdentityEditor
                receiptId={selectedReceipt.id}
                status={selectedReceipt.status}
                canUpdate={canUpdate}
                lines={selectedReceipt.details.map(d => ({
                  id: d.id,
                  productId: d.productId,
                  productCode: d.productCode,
                  productName: d.productName,
                  trackingType: d.trackingType || 'None',
                  expiryControl: d.expiryControl || false,
                  shelfLifeDays: d.shelfLifeDays,
                  baseAcceptedQuantity: d.baseAcceptedQuantity || 0,
                  baseDamagedQuantity: d.baseDamagedQuantity || 0,
                  baseRejectedQuantity: d.baseRejectedQuantity || 0,
                  baseUnitCode: d.baseUnitCode,
                }))}
              />
            )}

            {selectedReceipt.status === 'Draft' && canObserve && (
              <div className="import-section-actions">
                <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void observeReceipt(selectedReceipt.id, selectedReceipt)}>
                  {workflowId === selectedReceipt.id ? 'Đang ghi nhận...' : 'Ghi nhận số lượng thực tế'}
                </button>
              </div>
            )}

            {canReadDiscrepancy && ['DiscrepancyPending', 'DiscrepancyRejected'].includes(selectedReceipt.status) && (
              <section aria-labelledby="discrepancy-heading" className="import-workflow-panel warning">
                <h4 id="discrepancy-heading">Xử lý sai lệch nhận hàng</h4>
                <p>Xử lý sai lệch không ghi tồn kho. Ghi nhận tồn kho là bước duy nhất tạo tồn kho và sổ kho.</p>
                {discrepancies.filter(d => d.status === 'Pending' || d.status === 'Rejected').map(d => {
                  const line = selectedReceipt.details.find(x => x.id === d.receiptLineId);
                  const form = resolutionForms[d.id] || { action: 'ACCEPT_OBSERVED', doorRejectedQuantity: 0, reasonCode: '', responsibleParty: 'UNKNOWN', supplierClaimRequired: false, note: '', evidenceReference: '', confirmNormalizedObservation: false };
                  const set = (next: Partial<ResolutionForm>) => setResolutionForms(x => ({ ...x, [d.id]: { ...form, ...next } }));
                  return (
                    <fieldset key={d.id} className="import-resolution">
                      <legend>{line?.productCode}: dự kiến {d.expectedQuantity} {d.operationUnitCode}, quan sát {d.observedQuantity} {d.observedUnitCode}, chênh lệch chuẩn hóa {d.differenceQuantity} {d.operationUnitCode}</legend>
                      <label>Hành động
                        <select value={form.action} onChange={e => set({ action: e.target.value })}>
                          <option value="ACCEPT_OBSERVED">Chấp nhận số lượng quan sát</option>
                          <option value="ACCEPT_EXPECTED_REJECT_EXCESS">Nhận dự kiến, từ chối phần thừa</option>
                          <option value="REJECT_AT_DOOR">Từ chối tại cửa</option>
                          <option value="REJECT_WRONG_PRODUCT">Từ chối sai sản phẩm</option>
                          <option value="ROUTE_TO_QC">Chuyển sang kiểm tra chất lượng</option>
                        </select>
                      </label>
                      <label>Từ chối tại cửa <input aria-label={'Từ chối tại cửa ' + line?.productCode} type="number" min="0" step="any" value={form.doorRejectedQuantity} onChange={e => set({ doorRejectedQuantity: Number(e.target.value) })} /></label>
                      <label>Lý do
                        <select value={form.reasonCode} onChange={e => set({ reasonCode: e.target.value })}>
                          <option value="">-- Chọn lý do --</option>
                          {reasonCodes.map(x => <option key={x.code} value={x.code}>{x.code} — {x.name} (v{x.version})</option>)}
                        </select>
                      </label>
                      <label>Chịu trách nhiệm
                        <select value={form.responsibleParty} onChange={e => set({ responsibleParty: e.target.value })}>
                          <option value="SUPPLIER">Nhà cung cấp</option>
                          <option value="CARRIER">Đơn vị vận chuyển</option>
                          <option value="WAREHOUSE">Kho</option>
                          <option value="CUSTOMER_RETURN">Khách trả hàng</option>
                          <option value="UNKNOWN">Chưa xác định</option>
                        </select>
                      </label>
                      <label>Ghi chú <input value={form.note} onChange={e => set({ note: e.target.value })} /></label>
                      <label>Tham chiếu bằng chứng <input value={form.evidenceReference} onChange={e => set({ evidenceReference: e.target.value })} /></label>
                      {d.observedUnitId !== d.operationUnitId && (
                        <label className="import-resolution-check">
                          <input type="checkbox" checked={form.confirmNormalizedObservation} onChange={e => set({ confirmNormalizedObservation: e.target.checked })} />
                          Xác nhận số lượng quan sát đã quy đổi × {d.conversionFactor} v{d.conversionVersion}
                        </label>
                      )}
                      <div>Số lượng cuối dự kiến: {Math.max(0, d.normalizedObservedQuantity - form.doorRejectedQuantity)} {d.operationUnitCode}; phần từ chối tại cửa không tạo tồn kho.</div>
                      <label>Kiểm đếm lại <input aria-label={'Kiểm đếm lại ' + line?.productCode} type="number" min="0" step="any" value={receiveLines[d.receiptLineId]?.receivedQuantity ?? d.observedQuantity} onChange={e => setReceiveLines(x => ({ ...x, [d.receiptLineId]: { ...(x[d.receiptLineId] || { acceptedQuantity: 0, damagedQuantity: 0, rejectedQuantity: 0 }), receivedQuantity: Number(e.target.value) } }))} /></label>
                      <div className="import-row-actions">
                        {canResolve && <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void recountDiscrepancy(d)}>Lưu phiên bản kiểm đếm mới</button>}
                        {canSubmit && <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void submitResolution(d)}>{workflowId === selectedReceipt.id ? 'Đang gửi...' : 'Gửi phương án xử lý'}</button>}
                      </div>
                    </fieldset>
                  );
                })}
              </section>
            )}

            {canReadDiscrepancy && selectedReceipt.status === 'DiscrepancyPendingApproval' && (
              <section aria-labelledby="discrepancy-approval-heading" className="import-workflow-panel review">
                <h4 id="discrepancy-approval-heading">Duyệt phương án xử lý sai lệch</h4>
                {discrepancies.filter(d => d.status === 'PendingApproval').map(d => (
                  <div key={d.id} className="import-row-actions">
                    <span>Dòng {d.receiptLineId}: {d.differenceQuantity} {d.operationUnitCode}</span>
                    {canApproveDiscrepancy && <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void reviewResolution(d, 'approve')}>Duyệt phương án xử lý</button>}
                    {canRejectDiscrepancy && <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void reviewResolution(d, 'reject')}>Trả lại để kiểm đếm</button>}
                  </div>
                ))}
              </section>
            )}

            <div className="import-section-actions">
              {selectedReceipt.status === 'QcPending' && canQc && (
                <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void runWorkflow(selectedReceipt.id, 'qc-disposition', selectedReceipt)}>
                  {workflowId === selectedReceipt.id ? 'Đang lưu kết quả kiểm tra chất lượng...' : 'Ghi nhận kết quả kiểm tra chất lượng'}
                </button>
              )}
              <button type="button" onClick={() => setSelectedReceipt(null)}>Đóng chi tiết</button>
            </div>
          </UiCard>
        )}

        {printReceipt && printFetchedAt && (
          <ReceiptPrintPreview
            kind="import"
            receipt={{ ...printReceipt, details: printReceipt.details.map(d => ({ ...d, unitName: d.operationUnitCode, quantity: d.postedQuantity || d.receivedQuantity || d.expectedQuantity })) }}
            fetchedAt={printFetchedAt}
            onClose={closePrintPreview}
          />
        )}
      </div>
    </UiPage>
  );
};

export default ImportReceipts;
