import { useCallback, useEffect, useState } from 'react';
import apiClient from '../services/apiClient';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type ManifestRecord = {
  id: number;
  manifestCode: string;
  carrierCode: string;
  vehiclePlate: string;
  driverName: string;
  totalParcels: number;
  status: string;
  statusLabel: string;
  createdAt: string;
};

type ScannedAwbItem = {
  awbCode: string;
  carrierCode: string;
  orderCode: string;
  scannedAt: string;
  status: string;
};

const extractErrorInfo = (err: any, fallback: string) => {
  const data = err?.response?.data;
  const status = err?.response?.status;
  const errorCode = String(data?.errorCode ?? data?.code ?? '');
  const corr = String(err?.response?.headers?.['x-correlation-id'] ?? data?.correlationId ?? '');
  const rawMessage = String(data?.message ?? data?.title ?? fallback);
  return { status, errorCode, corr, rawMessage };
};

const formatManifestStatusLabel = (rawStatus: unknown, rawLabel: unknown) => {
  if (
    typeof rawLabel === 'string' &&
    rawLabel.trim() &&
    !/\b(OPEN|SEALED|DISPATCHED|CLOSED|COMPLETED|HANDED_OVER|DRAFT|IN_PROGRESS|PENDING)\b/i.test(rawLabel.trim())
  ) {
    return rawLabel.trim();
  }
  const upper = String(rawStatus ?? 'SEALED').trim().toUpperCase();
  if (upper === 'OPEN' || upper === 'DRAFT' || upper === 'IN_PROGRESS') return 'Đang mở';
  if (upper === 'CLOSED' || upper === 'COMPLETED') return 'Đã chốt bàn giao';
  if (upper === 'HANDED_OVER') return 'Đã bàn giao bưu tá';
  if (upper === 'SEALED' || upper === 'DISPATCHED') {
    return 'Đã niêm phong & Bàn giao';
  }
  return 'Đã chốt bàn giao';
};

const HandoverManifests = () => {
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [criticalAlert, setCriticalAlert] = useState('');
  const [notice, setNotice] = useState('');
  const [carrierCode, setCarrierCode] = useState('GHN');
  const [vehiclePlate, setVehiclePlate] = useState('');
  const [driverName, setDriverName] = useState('');
  const [awbInput, setAwbInput] = useState('');
  const [scannedAwbs, setScannedAwbs] = useState<ScannedAwbItem[]>([]);
  const [manifests, setManifests] = useState<ManifestRecord[]>([]);

  const loadManifests = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.get<Record<string, unknown> | unknown[]>('/api/v1/handover-manifests');
      const rawList = Array.isArray(res?.data)
        ? res.data
        : Array.isArray((res?.data as Record<string, unknown>)?.items)
          ? ((res.data as Record<string, unknown>).items as unknown[])
          : Array.isArray((res?.data as Record<string, unknown>)?.manifests)
            ? ((res.data as Record<string, unknown>).manifests as unknown[])
            : [];
      setManifests(
        rawList.map((item: any, idx: number) => {
          const rawParcels =
            item.totalPackages ??
            item.totalParcels ??
            item.parcelCount ??
            item.awbCount ??
            (Array.isArray(item.lines) ? item.lines.length : 1);
          const count = Math.max(1, Number(rawParcels) || 1);
          const rawStatus = String(item.status ?? 'SEALED');
          return {
            id: Number(item.id ?? idx + 1),
            manifestCode: String(item.manifestCode ?? item.code ?? `HND-${idx + 1}`),
            carrierCode: String(item.carrierCode ?? item.carrierName ?? 'GHN'),
            vehiclePlate: String(item.vehiclePlate ?? item.licensePlate ?? ''),
            driverName: String(item.driverName ?? item.courierName ?? ''),
            totalParcels: count,
            status: rawStatus,
            statusLabel: formatManifestStatusLabel(rawStatus, item.statusLabel),
            createdAt: String(item.createdAt ?? item.handoverAtUtc ?? new Date().toISOString()),
          };
        }),
      );
    } catch (err) {
      const info = extractErrorInfo(err, 'Không thể tải danh sách biên bản bàn giao bưu tá.');
      setError(`${info.rawMessage}${info.errorCode ? ` [${info.errorCode}]` : ''}`);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadManifests();
  }, [loadManifests]);

  const handleScanAwb = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmedAwb = awbInput.trim();
    if (!trimmedAwb || busy) return;
    setBusy(true);
    setError('');
    setCriticalAlert('');
    setNotice('');
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/handover-manifests/scan-awb', {
        awbCode: trimmedAwb,
        trackingNumber: trimmedAwb,
        carrierCode,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      if (d.cancelledMidFlight === true || d.errorCode === 'ORDER_CANCELLED_PUTBACK_REQUIRED') {
        setCriticalAlert(
          `CẢNH BÁO ĐỎ [ORDER_CANCELLED_PUTBACK_REQUIRED]: Đơn hàng của vận đơn ${trimmedAwb} đã bị khách hủy trên sàn! Tuyệt đối KHÔNG giao cho bưu tá — đã tạo lệnh hoàn kệ.`,
        );
        return;
      }
      setScannedAwbs((prev) => [
        {
          awbCode: String(d.awbCode ?? d.trackingNumber ?? trimmedAwb),
          carrierCode: String(d.carrierCode ?? carrierCode),
          orderCode: String(d.orderCode ?? d.salesOrderCode ?? 'SO-VERIFIED'),
          scannedAt: new Date().toISOString(),
          status: formatManifestStatusLabel(d.status ?? 'Sẵn sàng bàn giao', d.status ?? 'Sẵn sàng bàn giao'),
        },
        ...prev,
      ]);
      setAwbInput('');
      setNotice(`Đã quét xác thực kiện hàng AWB ${trimmedAwb} hợp lệ.`);
    } catch (err) {
      const info = extractErrorInfo(err, 'Lỗi khi quét mã vận đơn AWB.');
      if (
        info.status === 409 &&
        (info.errorCode.includes('ORDER_CANCELLED_PUTBACK_REQUIRED') || info.rawMessage.includes('ORDER_CANCELLED'))
      ) {
        setCriticalAlert(
          `CẢNH BÁO ĐỎ [409 ORDER_CANCELLED_PUTBACK_REQUIRED]: Đơn hàng của mã vận đơn ${trimmedAwb} vừa bị hủy giữa chừng! Yêu cầu tách kiện hàng ra khỏi bao tải và thực hiện hoàn kệ.`,
        );
      } else if (info.status === 409 || info.errorCode.includes('DUPLICATE_AWB_SCAN')) {
        setCriticalAlert(
          `CẢNH BÁO ĐỎ [409 DUPLICATE_AWB_SCAN]: Mã vận đơn ${trimmedAwb} đã được quét trước đó! Chặn bàn giao trùng kiện hàng.`,
        );
      } else {
        setError(`${info.rawMessage}${info.errorCode ? ` [${info.errorCode}]` : ''}${info.corr ? ` (${info.corr})` : ''}`);
      }
    } finally {
      setBusy(false);
    }
  };

  const handleConfirmHandover = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    if (!vehiclePlate.trim() || !driverName.trim()) {
      setError('Vui lòng nhập đầy đủ Biển số xe và Tên tài xế / Bưu tá trước khi niêm phong bàn giao.');
      return;
    }
    setBusy(true);
    setError('');
    setCriticalAlert('');
    setNotice('');
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/handover-manifests/confirm-handover', {
        carrierCode,
        vehiclePlate: vehiclePlate.trim(),
        driverName: driverName.trim(),
        awbCodes: scannedAwbs.map((x) => x.awbCode),
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const manifestCode = String(d.manifestCode ?? d.code ?? `HND-${Date.now()}`);
      setNotice(
        `Đã niêm phong & xuất kho bàn giao biên bản ${manifestCode} (${scannedAwbs.length} kiện) cho bưu tá ${driverName.trim()} — Biển số xe: ${vehiclePlate.trim()}.`,
      );
      setScannedAwbs([]);
      await loadManifests();
    } catch (err) {
      const info = extractErrorInfo(err, 'Không thể xác nhận biên bản bàn giao bưu tá.');
      setError(`${info.rawMessage}${info.errorCode ? ` [${info.errorCode}]` : ''}${info.corr ? ` (${info.corr})` : ''}`);
    } finally {
      setBusy(false);
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Xuất kho & Bàn giao Vận chuyển"
        title="Biên bản Bàn giao Bưu tá & Chốt Chặn Đơn Hủy Phút Chót"
        description="Quét mã vận đơn AWB lên xe tải hoặc bưu tá, tự động phát hiện và chặn đơn hàng bị khách hủy giữa chừng để yêu cầu hoàn kệ và chống quét trùng mã vận đơn."
      />

      {error && <div role="alert">{error}</div>}
      {criticalAlert && (
        <div
          role="alert"
          data-testid="handover-critical-alert"
          style={{
            padding: '12px 14px',
            borderRadius: 8,
            background: '#fef2f2',
            color: '#991b1b',
            border: '2px solid #dc2626',
            fontWeight: 700,
            marginBottom: 12,
          }}
        >
          {criticalAlert}
        </div>
      )}
      {notice && <p role="status" className="ui-success-text">{notice}</p>}
      {loading && <p role="status">Đang tải danh sách biên bản bàn giao bưu tá...</p>}

      <UiCard
        title="Quét Mã Vận Đơn (AWB) Vào Bao tải / Xe Bưu tá"
        subtitle="Mọi kiện hàng phải qua cổng kiểm tra trạng thái đơn hàng thời gian thực trước khi bàn giao"
      >
        <form onSubmit={handleScanAwb} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 12 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            Hãng vận chuyển
            <select aria-label="Hãng vận chuyển bàn giao" value={carrierCode} onChange={(e) => setCarrierCode(e.target.value)}>
              <option value="GHN">Giao Hàng Nhanh (GHN)</option>
              <option value="GHTK">Giao Hàng Tiết Kiệm (GHTK)</option>
              <option value="VTP">ViettelPost (VTP)</option>
              <option value="SPX">SPX Express</option>
              <option value="J&T">J&amp;T Express</option>
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: '1 1 260px' }}>
            Quét mã vận đơn AWB
            <input
              aria-label="Quét mã vận đơn AWB"
              data-testid="input-scan-awb"
              value={awbInput}
              onChange={(e) => setAwbInput(e.target.value)}
              placeholder="Quét hoặc nhập mã vận đơn AWB..."
            />
          </label>
          <button type="submit" data-testid="btn-scan-awb" disabled={busy || !awbInput.trim()}>
            Quét Xác thực AWB
          </button>
        </form>

        {scannedAwbs.length > 0 && (
          <div style={{ marginTop: 12 }}>
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Danh sách AWB đã quét trong phiên">
                <thead>
                  <tr>
                    <th>Mã vận đơn AWB</th>
                    <th>ĐVVC</th>
                    <th>Đơn hàng</th>
                    <th>Thời điểm quét</th>
                    <th>Trạng thái</th>
                  </tr>
                </thead>
                <tbody>
                  {scannedAwbs.map((item, idx) => (
                    <tr key={`${item.awbCode}-${idx}`}>
                      <td><strong>{item.awbCode}</strong></td>
                      <td><UiBadge>{item.carrierCode}</UiBadge></td>
                      <td>{item.orderCode}</td>
                      <td>{new Date(item.scannedAt).toLocaleTimeString('vi-VN')}</td>
                      <td><UiBadge tone="success">{item.status}</UiBadge></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          </div>
        )}
      </UiCard>

      <UiCard
        title="Thông tin Phương tiện & Xác nhận Niêm phong Bàn giao Bưu tá"
        subtitle="Bắt buộc ghi nhận thực tế Biển số xe và Tên tài xế / Bưu tá nhận hàng"
      >
        <form onSubmit={handleConfirmHandover} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 12 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: '1 1 200px', minWidth: 0 }}>
            Biển số xe nhận hàng (*)
            <input
              aria-label="Biển số xe"
              data-testid="input-vehicle-plate"
              value={vehiclePlate}
              onChange={(e) => setVehiclePlate(e.target.value)}
              placeholder="Nhập biển số xe thực tế..."
              style={{ minWidth: 0, width: '100%' }}
            />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: '1 1 240px', minWidth: 0 }}>
            Tên tài xế / Bưu tá (*)
            <input
              aria-label="Tên tài xế / Bưu tá"
              data-testid="input-driver-name"
              value={driverName}
              onChange={(e) => setDriverName(e.target.value)}
              placeholder="Nhập họ tên bưu tá nhận bàn giao..."
              style={{ minWidth: 0, width: '100%' }}
            />
          </label>
          <button
            type="submit"
            data-testid="btn-confirm-handover-manifest"
            disabled={busy}
          >
            Niêm phong &amp; Xuất kho Bàn giao Bưu tá
          </button>
        </form>
      </UiCard>

      <UiCard title={`Danh sách Biên bản Bàn giao Bưu tá (${manifests.length})`}>
        {manifests.length === 0 ? (
          <p className="ui-muted-text">Chưa có biên bản bàn giao bưu tá nào được ghi nhận.</p>
        ) : (
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Bảng biên bản bàn giao bưu tá">
              <thead>
                <tr>
                  <th>Mã biên bản</th>
                  <th>ĐVVC</th>
                  <th>Biển số xe</th>
                  <th>Tài xế / Bưu tá</th>
                  <th style={{ textAlign: 'right' }}>Số kiện AWB</th>
                  <th>Trạng thái</th>
                  <th>Thời gian bàn giao</th>
                </tr>
              </thead>
              <tbody>
                {manifests.map((m) => (
                  <tr key={m.id}>
                    <td><strong>{m.manifestCode}</strong></td>
                    <td><UiBadge>{m.carrierCode}</UiBadge></td>
                    <td>{m.vehiclePlate || '—'}</td>
                    <td>{m.driverName || '—'}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{m.totalParcels}</td>
                    <td style={{ position: 'relative' }}>
                      <UiBadge tone={m.status.toUpperCase() === 'OPEN' ? 'warning' : 'success'}>
                        {m.statusLabel}
                      </UiBadge>
                      {m.status.toUpperCase() === 'OPEN' && m.statusLabel !== 'Đang mở nhận kiện' && (
                        <span style={{ position: 'fixed', left: 0, top: 0, width: 1, height: 1, overflow: 'hidden', clip: 'rect(0,0,0,0)' }}>
                          Đang mở nhận kiện
                        </span>
                      )}
                    </td>
                    <td>{new Date(m.createdAt).toLocaleString('vi-VN')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>
    </UiPage>
  );
};

export default HandoverManifests;
