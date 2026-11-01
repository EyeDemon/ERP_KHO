import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  locationCodeLabel,
  referenceTypeLabel,
  shipmentStatusLabel,
  type Result,
} from './inventoryTraceabilityShared';

export function InventoryTraceabilityExposureCards({ result }: { result: Result }) {
  return (
    <>
      <UiCard title="Chứng từ có giao dịch cùng lô / sê-ri">
        <p className="ui-muted-text">
          Tổng hợp chứng từ có phát sinh sổ cái của đúng sản phẩm và lô/sê-ri trong các kho được cấp quyền, tại mốc lịch sử đang xem.
          Đây là liên hệ theo danh tính hàng hóa, chưa chứng minh quan hệ giao nhận, chủ sở hữu hoặc luồng thu hồi.
        </p>
        {result.relatedDocumentsTruncated && (
          <p role="status" className="ui-muted-text">
            Chỉ hiển thị 100 chứng từ mới nhất trong phạm vi truy vết. Hãy chọn một kho hoặc lọc thêm sê-ri để thu hẹp kết quả.
          </p>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Chứng từ liên quan cùng lô hoặc sê-ri">
            <thead>
              <tr>
                <th>Chứng từ</th>
                <th>Kho</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Sự kiện</th>
                <th>Phát sinh đầu</th>
                <th>Phát sinh cuối</th>
              </tr>
            </thead>
            <tbody>
              {!result.relatedDocuments?.length ? (
                <tr><td colSpan={5} className="ui-empty-cell">Chưa có chứng từ có giao dịch sổ cái khớp lô hoặc sê-ri trong phạm vi được phép.</td></tr>
              ) : (
                result.relatedDocuments.map((doc) => (
                  <tr key={doc.warehouseId + '-' + doc.referenceType + '-' + doc.referenceId}>
                    <td><strong>{referenceTypeLabel(doc.referenceType)}</strong><br /><small>Mã tham chiếu #{doc.referenceId}</small></td>
                    <td>{doc.warehouseName}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{doc.eventCount}</td>
                    <td>{new Date(doc.firstTransactionDate).toLocaleString('vi-VN')}</td>
                    <td>{new Date(doc.lastTransactionDate).toLocaleString('vi-VN')}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>

      <UiCard title="Phiếu nhập đã ghi sổ cùng lô / sê-ri">
        <p className="ui-muted-text">
          Chỉ hiển thị phiếu nhập đã ghi sổ thực tế khớp sản phẩm và lô/sê-ri trong kho được cấp quyền.
          Số lượng là tổng nhập theo sổ cái tại mốc lịch sử, không chứng minh QC riêng từng lô hoặc nguồn trực tiếp của chuyến giao.
        </p>
        {result.receiptExposuresTruncated && (
          <p role="status" className="ui-muted-text">
            Đã đạt giới hạn 100 phiếu nhập; hãy chọn một kho hoặc thu hẹp lô/sê-ri để xem đầy đủ.
          </p>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Phiếu nhập đã ghi sổ theo lô hoặc sê-ri">
            <thead>
              <tr>
                <th>Mã phiếu nhập</th>
                <th>Trạng thái</th>
                <th>Lần ghi sổ cuối</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã nhập</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Giao dịch sổ cái</th>
              </tr>
            </thead>
            <tbody>
              {!result.receiptExposures?.length ? (
                <tr><td colSpan={5} className="ui-empty-cell">Không có phiếu nhập đã ghi sổ khớp danh tính hàng hóa trong phạm vi được phép.</td></tr>
              ) : (
                result.receiptExposures.map((receipt) => (
                  <tr key={receipt.warehouseId + '-' + receipt.receiptId}>
                    <td><strong>{receipt.receiptCode}</strong><br /><small>#{receipt.receiptId} • Kho #{receipt.warehouseId}</small></td>
                    <td><UiBadge>Đã ghi sổ</UiBadge></td>
                    <td>{new Date(receipt.lastPostedAt).toLocaleString('vi-VN')}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{receipt.postedQuantity}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{receipt.ledgerEventCount}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>

      <UiCard title="Chuyến giao hàng cần rà soát khi thu hồi">
        <p className="ui-muted-text">
          Chỉ hiển thị chuyến hàng có giao dịch xuất giao thực tế khớp sản phẩm và lô/sê-ri.
          Số lượng là tổng đã xuất, chưa trừ hàng hoàn; trạng thái giao hàng có thể thay đổi sau mốc lịch sử.
          Danh sách này là dữ liệu hỗ trợ rà soát, không tự động thu hồi hoặc xác định người nhận cuối.
        </p>
        {result.shipmentExposuresTruncated && (
          <p role="status" className="ui-muted-text">
            Đã đạt giới hạn 100 Shipment; hãy chọn một kho hoặc thu hẹp điều kiện để xem đầy đủ.
          </p>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Shipment có giao dịch xuất giao cần rà soát">
            <thead>
              <tr>
                <th>Mã chuyến giao</th>
                <th>Trạng thái hiện tại</th>
                <th>Xuất giao lúc</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã xuất</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Giao dịch sổ cái</th>
              </tr>
            </thead>
            <tbody>
              {!result.shipmentExposures?.length ? (
                <tr><td colSpan={5} className="ui-empty-cell">Không tìm thấy chuyến giao hàng đã ghi nhận xuất giao trong phạm vi truy vết.</td></tr>
              ) : (
                result.shipmentExposures.map((shipment) => (
                  <tr key={shipment.warehouseId + '-' + shipment.shipmentId}>
                    <td><strong>{shipment.shipmentCode}</strong><br /><small>#{shipment.shipmentId} • Kho #{shipment.warehouseId}</small></td>
                    <td><UiBadge>{shipmentStatusLabel(shipment.shipmentStatus)}</UiBadge></td>
                    <td>{shipment.dispatchedAt ? new Date(shipment.dispatchedAt).toLocaleString('vi-VN') : '—'}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shipment.dispatchedQuantity}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shipment.ledgerEventCount}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>

      <UiCard title="Liên kết Chuyến giao → Đóng gói → Lấy hàng đã ghi nhận">
        <p className="ui-muted-text">
          Liên kết được đối chiếu trực tiếp theo chuyến giao, phiên đóng gói, dòng lấy hàng và phân bổ tồn,
          đồng thời bắt buộc có sự kiện xuất giao khớp vị trí, trạng thái và lô/sê-ri.
          Số lượng bên dưới là đã lấy hàng, không phải lượng đã giao; chưa chứng minh quan hệ đến phiếu nhập hoặc kiện hàng riêng.
        </p>
        {result.shipmentPickingEvidenceTruncated && (
          <p role="status" className="ui-muted-text">
            Chỉ hiển thị 100 dòng Picking liên kết đầu tiên; hãy giới hạn thêm kho hoặc lô/sê-ri để xem đầy đủ.
          </p>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Liên kết Shipment Packing Picking theo lô hoặc sê-ri">
            <thead>
              <tr>
                <th>Chuyến giao</th>
                <th>Đóng gói</th>
                <th>Lấy hàng</th>
                <th>Phân bổ / Vị trí</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã lấy hàng</th>
              </tr>
            </thead>
            <tbody>
              {!result.shipmentPickingEvidence?.length ? (
                <tr><td colSpan={5} className="ui-empty-cell">Chưa có dòng lấy hàng được xác thực bằng sự kiện xuất giao trong phạm vi truy vết.</td></tr>
              ) : (
                result.shipmentPickingEvidence.map((link) => (
                  <tr key={link.shipmentId + '-' + link.pickingTaskLineId}>
                    <td><strong>{link.shipmentCode}</strong><small> #{link.shipmentId}</small></td>
                    <td>{link.packingSessionCode}<br /><small>#{link.packingSessionId}</small></td>
                    <td>{link.pickingTaskCode}<br /><small>Dòng #{link.pickingTaskLineId}</small></td>
                    <td>#{link.allocationId}<br /><small>{locationCodeLabel(link.sourceLocationCode)}</small></td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{link.pickedQuantity}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>

      <UiCard title="Chuỗi kiện hàng của chuyến giao đã xuất">
        <p className="ui-muted-text">
          Chỉ hiển thị kiện đã đóng hàng theo dòng lấy hàng và nối tới kiện gốc thực sự gắn chuyến giao.
          Mỗi chuỗi được đối chiếu cùng giao dịch xuất giao, sản phẩm, vị trí, trạng thái và lô/sê-ri trong kho được phép.
          Cấu trúc kiện là trạng thái hiện tại, còn giao dịch xuất giao được cố định theo mốc lịch sử.
          Số lượng đã đóng không phải số lượng giao thành công hoặc hoàn trả.
        </p>
        {result.shipmentHuEvidenceTruncated && (
          <p role="status" className="ui-muted-text">
            Dữ liệu kiện HU có giới hạn truy vết (100 dòng, tối đa 16 cấp cha). Hãy thu hẹp kho/lô/sê-ri; không coi danh sách này là đầy đủ để ra quyết định thu hồi.
          </p>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Chuỗi Handling Unit gốc con đã xuất theo lô hoặc sê-ri">
            <thead>
              <tr>
                <th>Chuyến giao</th>
                <th>Kiện gốc</th>
                <th>Chuỗi kiện</th>
                <th>Mã quét kiện chứa hàng</th>
                <th>Dòng lấy hàng</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã đóng gói</th>
              </tr>
            </thead>
            <tbody>
              {!result.shipmentHuEvidence?.length ? (
                <tr><td colSpan={6} className="ui-empty-cell">Chưa xác minh được kiện hàng gắn chuyến giao với sản phẩm/lô/sê-ri này.</td></tr>
              ) : (
                result.shipmentHuEvidence.map((hu) => (
                  <tr key={hu.shipmentId + '-' + hu.contentHandlingUnitId + '-' + hu.pickingTaskLineId}>
                    <td>#{hu.shipmentId}</td>
                    <td><strong>{hu.rootHandlingUnitCode}</strong><br /><small>#{hu.rootHandlingUnitId}</small></td>
                    <td>{hu.hierarchyPath}<br /><small>Kiện chứa hàng: #{hu.contentHandlingUnitId}</small></td>
                    <td>{hu.contentHandlingUnitBarcode}</td>
                    <td>#{hu.pickingTaskLineId}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{hu.packedQuantity}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>

      <UiCard title="Phiếu xuất kho nguồn của chuyến giao">
        <p className="ui-muted-text">
          Liên kết chứng từ thật: chuyến giao và tác vụ lấy hàng cùng tham chiếu phiếu xuất đã chuyển trạng thái xuất giao,
          có dòng sản phẩm khớp và giao dịch xuất giao trong kho được phép.
          Số lượng trên phiếu xuất tính theo sản phẩm, không phân chia theo lô/sê-ri;
          đây không phải bằng chứng lô hàng bắt nguồn từ phiếu nhập nào hoặc đã giao thành công.
        </p>
        {result.shipmentExportSourcesTruncated && (
          <p role="status" className="ui-muted-text">
            Danh sách chứng từ xuất nguồn chưa đầy đủ vì giới hạn 100 Shipment; hãy thu hẹp kho hoặc mã lô/sê-ri.
          </p>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Liên kết Shipment Picking với phiếu xuất kho nguồn">
            <thead>
              <tr>
                <th>Chuyến giao</th>
                <th>Phiếu xuất nguồn</th>
                <th>Tác vụ lấy hàng</th>
                <th>Phiếu đã xuất lúc</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Số lượng sản phẩm trên phiếu</th>
              </tr>
            </thead>
            <tbody>
              {!result.shipmentExportSources?.length ? (
                <tr><td colSpan={5} className="ui-empty-cell">Chưa xác minh được phiếu xuất kho nguồn phù hợp trong phạm vi truy vết.</td></tr>
              ) : (
                result.shipmentExportSources.map((source) => (
                  <tr key={source.warehouseId + '-' + source.shipmentId + '-' + source.exportReceiptId}>
                    <td><strong>{source.shipmentCode}</strong><br /><small>#{source.shipmentId} • Kho #{source.warehouseId}</small></td>
                    <td><strong>{source.exportReceiptCode}</strong><br /><small>#{source.exportReceiptId}</small></td>
                    <td>#{source.pickingTaskId}</td>
                    <td>{source.exportReceiptDispatchedAt ? new Date(source.exportReceiptDispatchedAt).toLocaleString('vi-VN') : '—'}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{source.exportReceiptProductQuantity}</td>
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
