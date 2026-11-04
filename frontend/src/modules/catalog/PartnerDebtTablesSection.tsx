import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  formatCurrency,
  formatNumberCompact,
  formatPaymentMethod,
  formatSourceType,
  type PartnerDebtInvoice,
  type PartnerDebtLedger,
  renderNoteWithNowrapCodes,
  STATUS_LABELS,
  statusTone,
  TX_LABELS,
} from './PartnerDebtTypes';

interface PartnerDebtTablesSectionProps {
  loading: boolean;
  invoices: PartnerDebtInvoice[];
  selectedInvoice: PartnerDebtInvoice | null;
  displayedLedger: PartnerDebtLedger[];
  canManage: boolean;
  openPaymentModal: (inv: PartnerDebtInvoice) => void;
}

export function PartnerDebtTablesSection({
  loading,
  invoices,
  selectedInvoice,
  displayedLedger,
  canManage,
  openPaymentModal,
}: PartnerDebtTablesSectionProps) {
  return (
    <>
      <UiCard title="Danh sách Hóa đơn Công nợ">
        {loading && invoices.length === 0 ? (
          <p role="status">Đang tải danh sách hóa đơn công nợ...</p>
        ) : (
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Danh sách hóa đơn công nợ" style={{ width: '100%' }}>
              <thead>
                <tr>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Mã hóa đơn</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Loại</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5, minWidth: 115, maxWidth: 155 }}>Đối tác</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Chứng từ kho</th>
                  <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5, textAlign: 'right' }}>Tổng cộng</th>
                  <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5, textAlign: 'right' }}>Đã trả</th>
                  <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5, textAlign: 'right' }}>Còn nợ</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Hạn TT</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Trạng thái</th>
                  <th style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 10.5 }}>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {invoices.length === 0 ? (
                  <tr>
                    <td colSpan={10} className="ui-empty-cell">
                      Chưa có hóa đơn công nợ phù hợp với bộ lọc.
                    </td>
                  </tr>
                ) : (
                  invoices.map(inv => (
                    <tr key={inv.id}>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, fontVariantNumeric: 'tabular-nums' }}>
                        <strong style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{inv.code}</strong>
                      </td>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 11.5 }}>
                        <UiBadge tone={inv.debtType === 'AP' ? 'warning' : 'success'}>
                          {inv.debtType}
                        </UiBadge>
                        <small style={{ display: 'block', fontSize: 10.5, marginTop: 2 }}>
                          {inv.debtType === 'AP' ? 'Phải trả' : 'Phải thu'}
                        </small>
                      </td>
                      <td
                        className="ui-cell-partner"
                        style={{ minWidth: 110, maxWidth: 145, padding: '6px 5px', fontSize: 12, overflowWrap: 'break-word', wordBreak: 'normal' }}
                      >
                        <div style={{ whiteSpace: 'nowrap', fontWeight: 600 }}>{inv.partnerCode}</div>
                        <small style={{ overflowWrap: 'break-word', wordBreak: 'normal', fontSize: 11, lineHeight: 1.25, display: 'block' }}>{inv.partnerName}</small>
                      </td>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, fontVariantNumeric: 'tabular-nums' }}>
                        <strong style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{inv.sourceCode}</strong>
                        <small style={{ whiteSpace: 'nowrap', fontSize: 11, display: 'block' }}>{formatSourceType(inv.sourceType)}</small>
                      </td>
                      <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        <div><strong>{formatCurrency(inv.totalAmount, inv.currency)}</strong></div>
                        <div style={{ fontSize: 10, color: '#475569', fontWeight: 400, whiteSpace: 'pre-line', lineHeight: 1.25 }}>
                          {`Trước thuế: ${formatNumberCompact(inv.subTotalAmount)}\n• VAT: ${formatNumberCompact(inv.taxAmount)}`}
                        </div>
                      </td>
                      <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        {formatCurrency(inv.paidAmount, inv.currency)}
                      </td>
                      <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        <strong>{formatCurrency(inv.remainingAmount, inv.currency)}</strong>
                      </td>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12, fontVariantNumeric: 'tabular-nums' }}>
                        {inv.dueDateUtc ? new Date(inv.dueDateUtc).toLocaleDateString('vi-VN') : '—'}
                      </td>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 11.5 }}>
                        <UiBadge tone={statusTone(inv.status)}>
                          {STATUS_LABELS[inv.status] ?? inv.status}
                        </UiBadge>
                      </td>
                      <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 5px', fontSize: 12 }}>
                        <div className="ui-inline-actions" style={{ flexWrap: 'nowrap', gap: 4 }}>
                          <button type="button" style={{ padding: '4px 7px', minHeight: 28, fontSize: 11.5 }} onClick={() => openPaymentModal(inv)}>
                            Sổ phụ
                          </button>
                          {canManage && ['Open', 'PartiallyPaid'].includes(inv.status) && (
                            <button type="button" className="ui-primary-button" style={{ padding: '4px 7px', minHeight: 28, fontSize: 11.5 }} onClick={() => openPaymentModal(inv)}>
                              Thanh toán
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>

      <UiCard
        title={
          selectedInvoice
            ? `Sổ phụ & Lịch sử Thanh toán — Hóa đơn ${selectedInvoice.code}`
            : 'Sổ phụ & Lịch sử Thanh toán (Sổ cái Công nợ Bất biến)'
        }
      >
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Sổ cái biến động công nợ đối tác">
            <thead>
              <tr>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Thời gian</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Loại</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px', minWidth: 130, maxWidth: 180 }}>Đối tác</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Hóa đơn</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Nghiệp vụ</th>
                <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 8px', textAlign: 'right' }}>Biến động</th>
                <th className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 8px', textAlign: 'right' }}>Dư nợ sau GD</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px' }}>Tham chiếu</th>
                <th style={{ whiteSpace: 'nowrap', padding: '6px 8px', minWidth: 120, maxWidth: 170 }}>Ghi chú</th>
              </tr>
            </thead>
            <tbody>
              {displayedLedger.length === 0 ? (
                <tr>
                  <td colSpan={9} className="ui-empty-cell">
                    Chưa có bút toán sổ cái công nợ.
                  </td>
                </tr>
              ) : (
                displayedLedger.map(entry => (
                  <tr key={entry.id}>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>
                      {new Date(entry.createdAtUtc).toLocaleString('vi-VN')}
                    </td>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5 }}>
                      <UiBadge>{entry.debtType}</UiBadge>
                    </td>
                    <td
                      className="ui-cell-partner"
                      style={{ minWidth: 130, maxWidth: 180, padding: '6px 8px', fontSize: 12.5, overflowWrap: 'break-word', wordBreak: 'normal' }}
                    >
                      {entry.partnerCode} — {entry.partnerName}
                    </td>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>
                      <strong style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{entry.invoiceCode}</strong>
                    </td>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5 }}>
                      {TX_LABELS[entry.transactionType] ?? entry.transactionType}
                    </td>
                    <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      <strong style={{ color: entry.amountDelta < 0 ? '#15803d' : '#b45309' }}>
                        {entry.amountDelta > 0 ? `+${entry.amountDelta.toLocaleString('vi-VN')}` : entry.amountDelta.toLocaleString('vi-VN')}
                      </strong>
                    </td>
                    <td className="ui-cell-numeric" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      <strong>{entry.balanceAfter.toLocaleString('vi-VN')}</strong>
                    </td>
                    <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap', padding: '6px 8px', fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>
                      <strong style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{entry.referenceCode || `#${entry.referenceId ?? ''}`}</strong>
                      <small style={{ whiteSpace: 'nowrap', fontSize: 11.5, display: 'block' }}>
                        {entry.paymentMethod ? formatPaymentMethod(entry.paymentMethod) : formatSourceType(entry.referenceType)}
                      </small>
                    </td>
                    <td style={{ minWidth: 120, maxWidth: 170, padding: '6px 8px', fontSize: 12.5, overflowWrap: 'break-word', wordBreak: 'normal' }}>
                      {renderNoteWithNowrapCodes(entry.note)}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>
    </>
  );
}
