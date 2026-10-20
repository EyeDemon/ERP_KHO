import type { InternalAxiosRequestConfig } from 'axios';
import {
  demoApprovalQueue,
  demoCategories,
  demoExportReceipts,
  demoImportReceipts,
  demoImportReceiptInventoryIdentities,
  demoPurchaseOrders,
  demoAsns,
  demoInOut,
  demoInventoryStocks,
  demoInventoryStatuses,
  demoInventoryBuckets,
  demoInventoryLocks,
  demoInventoryTransactions,
  demoPartners,
  demoPermissionCatalog,
  demoProducts,
  demoPutawayTasks,
  demoReservations,
  demoAllocations,
  demoPickingTasks,
  demoPackingSessions,
  demoHandlingUnits,
  demoShipments,
  demoSalesOrders,
  demoBackorders,
  demoAllocatableReservations,
  demoAllocationCandidates,
  demoStocktakes,
  demoTransfers,
  demoUnits,
  demoWarehouses,
  demoWarehouseStructures,
  demoWarehouseCalendars,
  demoDockYardWarehouses,
  demoDocks,
  demoYardSlots,
  demoDockAppointments,
} from '../mocks/demoApiData';
import { demoReconciliationRows, demoReconciliationWarehouses } from '../mocks/inventoryReconciliationDemo';

const ok = (config: InternalAxiosRequestConfig, data: unknown) => Promise.resolve({
  data,
  status: 200,
  statusText: 'OK',
  headers: {},
  config,
});

const fail = (config: InternalAxiosRequestConfig, status: number, message: string) => Promise.reject(Object.assign(new Error(message), {
  response: { status, data: { message }, headers: {}, config },
  config,
}));

const normalizePath = (url?: string) => {
  const raw = url ?? '';
  const withoutOrigin = raw.replace(/^https?:\/\/[^/]+/i, '');
  return withoutOrigin.split('?')[0].replace(/\/+$/, '').toLowerCase();
};

const urlParams = (config: InternalAxiosRequestConfig) => {
  const params = new URLSearchParams((config.url ?? '').split('?')[1] ?? '');
  const configured = config.params as Record<string, unknown> | undefined;
  for (const [key, value] of Object.entries(configured ?? {})) {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  }
  return params;
};

const paged = <T,>(items: T[], page: number, pageSize: number) => {
  const safePage = Math.max(1, page || 1);
  const safeSize = Math.max(1, pageSize || 20);
  const start = (safePage - 1) * safeSize;
  return {
    items: items.slice(start, start + safeSize),
    totalRecords: items.length,
    pageIndex: safePage,
    pageSize: safeSize,
    totalPages: Math.ceil(items.length / safeSize),
  };
};

const findNumericId = (path: string) => Number(path.match(/\/(\d+)(?:\/|$)/)?.[1] ?? 0);

export const createReadOnlyDemoApiAdapter = (request: InternalAxiosRequestConfig) => {
  return async (config: InternalAxiosRequestConfig = request) => {
    const method = (config.method ?? 'get').toLowerCase();
    const path = normalizePath(config.url);
    const params = urlParams(config);

    if (method === 'post') {
      if (path === '/api/warehouses/verify-topology-capacity') {
        return ok(config, { verificationId: 'TOP-CAP-DEMO-01', zoneBinHierarchyStatus: 'CANONICAL_VALIDATED', capacityGuardrailActive: true });
      }
      if (path === '/api/warehouses/verify-virtual-locations') {
        return ok(config, { verificationId: 'VLOC-AUD-DEMO', status: 'ALL_9_VIRTUAL_LOCATIONS_READY' });
      }
      if (path === '/api/warehouses/evaluate-network-mhe') {
        return ok(config, {
          evaluationId: 'NET-MHE-DEMO-01',
          networkTopologyMode: 'HUB_AND_SPOKE_BALANCED',
          capacityForecastStatus: 'OPTIMAL_HEADROOM_82PCT',
          mheFleetReadiness: 'FORKLIFT_CONVEYOR_SORTER_READY',
        });
      }
      if (/^\/api\/warehouses\/\d+\/calendar\/verify-shift-coverage$/.test(path)) {
        return ok(config, { verificationId: 'CAL-VER-DEMO-01', shiftCoverageStatus: 'FULL_COVERAGE_VERIFIED' });
      }
      if (/^\/api\/warehouses\/\d+\/calendar\/optimize-labor-interleaving$/.test(path)) {
        return ok(config, {
          optimizationId: 'WLM-INT-DEMO-01',
          interleavingMode: 'PUTAWAY_REPLENISH_PICK_CYCLE_COUNT_INTERLEAVED',
          ergonomicSafetyStatus: 'LIFT_LIMIT_AND_FATIGUE_ROTATION_COMPLIANT',
        });
      }
      if (path === '/api/reports/sync-bi-snapshot') {
        return ok(config, { snapshotId: 'BI-SNAP-DEMO', status: 'BI_HEATMAP_SYNCED' });
      }
      if (path === '/api/reports/operational-exceptions/acknowledge') {
        return ok(config, { status: 'ACKNOWLEDGED' });
      }
      if (path === '/api/units/verify-uom-integrity') {
        return ok(config, { verificationId: 'UOM-INT-DEMO-01', status: 'CANONICAL_UOM_VERIFIED' });
      }
      if (path === '/api/units/verify-catch-weight-coldchain') {
        return ok(config, { verificationId: 'CW-CC-HAZ-DEMO', status: 'SPECIALIZED_INVENTORY_RULES_VERIFIED' });
      }
      if (path === '/api/products/auto-audit-governance') {
        return ok(config, { auditId: 'PRD-GOV-DEMO', status: 'MASTER_DATA_GOVERNANCE_VERIFIED' });
      }
      if (path === '/api/productcategories/verify-storage-policy' || path === '/api/product-categories/verify-storage-policy') {
        return ok(config, { verificationId: 'CAT-POL-DEMO', status: 'STORAGE_TEMP_HAZARD_FEFO_VERIFIED' });
      }
      if (/^\/api\/business-partners\/\d+\/verify-sla-contract$/.test(path)) {
        return ok(config, { partnerId: findNumericId(path), slaContractStatus: 'VERIFIED_ACTIVE' });
      }
      if (path === '/api/business-partners/calculate-3pl-billing') {
        return ok(config, { statementId: '3PL-BILL-DEMO', status: '3PL_BILLING_RECONCILED' });
      }
      if (path === '/api/purchase-orders/expedite-overdue') {
        return ok(config, { expediteId: 'PO-EXP-DEMO', vendorNotificationStatus: 'DISPATCHED_TO_SUPPLIER' });
      }
      if (path === '/api/purchase-orders/generate-procurement-recommendations') {
        return ok(config, {
          recommendationId: 'PROC-REC-DEMO',
          procurementSuggestionStatus: 'REORDER_THRESHOLD_PROPOSALS_READY',
          supplierLeadTimePercentile: 'P90_LEAD_TIME_CALIBRATED',
          expediteDeferSignal: 'EXPEDITE_AND_DEFER_BALANCED',
          status: 'PROCUREMENT_INTELLIGENCE_READY',
        });
      }
      if (path === '/api/asns/pre-allocate-staging') {
        return ok(config, { stagingReservationId: 'ASN-STG-DEMO', stagingLaneCode: 'STAGE-IN-01' });
      }
      if (path === '/api/dock-yard/dispatch-crossdock') {
        return ok(config, { dispatchId: 'XDOCK-DEMO-01', dockDoorCode: 'D-02', stagedHuCount: 4, status: 'CROSSDOCK_ROUTED' });
      }
      if (path === '/api/importreceipts/initiate-rma-inspection' || path === '/api/importreceipts/rma-intake-check') {
        return ok(config, { rmaInspectionCode: 'RMA-QC-DEMO', rmaCode: 'RMA-QC-DEMO', status: 'INBOUND_QC_INSPECTION' });
      }
      if (path === '/api/importreceipts/execute-rtv-refurbish') {
        return ok(config, { dispositionCode: 'RTV-REF-DEMO', executionId: 'RTV-REF-DEMO', status: 'REFURBISH_AND_RTV_SPLIT' });
      }
      if (path === '/api/putaway-tasks/evaluate-slotting') {
        return ok(config, { evaluationId: 'SLOT-EVAL-DEMO', optimalBinMatchRatePct: 96.4, fefoHazardCompliance: 'VERIFIED' });
      }
      if (path === '/api/exportreceipts/verify-dispatch-cutoff') {
        return ok(config, { verificationId: 'EXP-CUT-DEMO', cutoffComplianceStatus: 'DISPATCH_CUTOFF_VERIFIED' });
      }
      if (path === '/api/sales-orders/expedite-backorder') {
        return ok(config, { expediteId: 'EXP-BO-DEMO', orderNumber: 'SO-2026-041', allocationWaveQueued: true });
      }
      if (path === '/api/sales-orders/evaluate-atp-routing') {
        return ok(config, {
          evaluationId: 'ATP-ROUTE-DEMO',
          atpCtpPromiseStatus: 'PROMISE_DATE_VERIFIED_READONLY',
          fulfillmentNodeRouting: 'OPTIMAL_WAREHOUSE_NODE_SELECTED',
          allocationFairnessPolicy: 'PROPORTIONAL_SERVICE_TIER_FAIRNESS',
          serviceSegmentation: 'TIER_1_VIP_AND_STANDARD_SLA_ACTIVE',
          status: 'ATP_ROUTING_FAIRNESS_READY',
        });
      }
      if (path === '/api/stock-reservations/sweep-expired-preview') {
        return ok(config, { sweepId: 'RSV-SWP-DEMO', status: 'COMMITMENT_TTL_VERIFIED' });
      }
      if (path === '/api/inventory/allocations/verify-fefo-compliance') {
        return ok(config, { verificationId: 'ALC-FEFO-DEMO', fefoComplianceStatus: 'STRICT_FEFO_FIFO_ALIGNED' });
      }
      if (path === '/api/picking-tasks/mobile-scan-verify') {
        return ok(config, { verified: true, status: 'SCAN_VERIFIED' });
      }
      if (path === '/api/picking-tasks/sync-automation-telemetry') {
        return ok(config, {
          telemetrySyncId: 'WCS-IOT-DEMO',
          wcsRoboticsState: 'AMR_ASRS_CONVEYOR_SYNCHRONIZED',
          rfidIotTelemetry: 'RFID_GATE_AND_COLD_CHAIN_SENSORS_ONLINE',
          voicePickToLight: 'VOICE_PTL_HANDS_FREE_DIRECTED_ACTIVE',
        });
      }
      if (path === '/api/packing-sessions/verify-packaging-profile') {
        return ok(config, { verificationId: 'PKG-VER-DEMO', cartonType: 'STD-CARTON-M', gs1SsccReady: true });
      }
      if (path === '/api/packing-sessions/execute-kitting-vas') {
        return ok(config, { workOrderId: 'KIT-VAS-DEMO', status: 'KITTING_VAS_READY', vasServiceType: 'LABELING_SHRINKWRAP_BUNDLE' });
      }
      if (path === '/api/shipments/sync-tms-manifest') {
        return ok(config, { manifestId: 'TMS-MNF-DEMO', carrierCode: 'VTP-EXPRESS', syncedShipmentCount: 6, ediStatus: 'ACK_200_SYNCED' });
      }
      if (path === '/api/handling-units/verify-intransit-seal') {
        return ok(config, { sealVerificationId: 'HU-SEAL-DEMO', sealStatus: 'SEAL_INTACT_VERIFIED', transferCode: 'TRF-2026-0001' });
      }
      if (path === '/api/stock-transfers/simulate-network-rebalancing') {
        return ok(config, {
          simulationId: 'NET-BAL-DEMO',
          networkSurplusShortageStatus: 'SURPLUS_TO_SHORTAGE_LANES_MAPPED',
          interWarehouseReplenishment: 'RESPECTS_RESERVATION_AND_TRANSFER_SEMANTICS',
          replenishmentExceptionTriage: 'ZERO_SILENT_STOCKOUT_CONFLICTS',
          status: 'NETWORK_REBALANCING_SIMULATED',
        });
      }
      if (path === '/api/stocktakes/adjustment-proposals') {
        return ok(config, { proposalCode: 'ADJ-DEMO-001', status: 'PENDING_SOD_APPROVAL' });
      }
      if (path === '/api/inventory/verify-bucket-invariants') {
        return ok(config, {
          verificationId: 'BCK-INV-DEMO',
          warehouseId: 1,
          fiveBucketEquationStatus: 'BALANCED_EXACT',
          scrapApprovalGuard: 'SOD_ENFORCED',
          verifiedAtUtc: '2026-10-17T08:00:00Z',
        });
      }
      if (path === '/api/inventory/verify-lock-precedence') {
        return ok(config, {
          verificationId: 'LCK-PRC-DEMO',
          warehouseId: 1,
          overlappingHoldPrecedence: 'RECALL_GT_QC_GT_COUNT',
          leakCount: 0,
          status: 'LOCK_PRECEDENCE_VERIFIED',
          verifiedAtUtc: '2026-10-17T08:00:00Z',
        });
      }
      if (path === '/api/inventory/reversals/verify-chain-integrity') {
        return ok(config, {
          verificationId: 'REV-INT-DEMO',
          warehouseId: 1,
          duplicateReversalBlocked: true,
          appendOnlyLedgerStatus: 'IMMUTABLE_VERIFIED',
          verifiedAtUtc: '2026-10-17T08:00:00Z',
        });
      }
      if (path === '/api/inventory/traceability/simulate-recall-tree') {
        return ok(config, {
          simulationId: 'TRC-RCL-DEMO',
          query: 'LOT-ARABICA-2609',
          upstreamPoLinked: true,
          downstreamShipmentLinked: true,
          recallContainmentReady: true,
          simulatedAtUtc: '2026-10-17T08:00:00Z',
        });
      }
      if (path === '/api/inventory/what-if-simulation' || path === '/api/inventory/run-what-if-simulation') {
        return ok(config, {
          demandMultiplier: 1.5,
          totalAvailable: 420,
          simulatedDemand: 630,
          projectedNetBalance: -210,
          scenarioStatus: 'PROJECTED_SHORTAGE',
          immutableSnapshotGuarantee: true,
        });
      }
      if (path === '/api/inventory/evaluate-decision-policy-governance') {
        return ok(config, {
          policyRegistryId: 'DEC-POL-DEMO',
          forecastBiasMapeStatus: 'MAPE_WITHIN_TOLERANCE_BAND',
          demandAnomalyDetector: 'SPIKE_AND_DROP_GUARD_ACTIVE',
          inventoryRiskScoreStatus: 'EXPLAINABLE_RISK_SCORING_READY',
          humanInTheLoopBoundary: 'APPROVAL_REQUIRED_BEFORE_LEDGER_MUTATION',
          status: 'DECISION_POLICY_GOVERNANCE_VERIFIED',
        });
      }
      if (path === '/api/inventoryreconciliation/export-erp-gl-journal' || path === '/api/inventoryreconciliation/verify-site-readiness') {
        const mismatchCount = demoReconciliationRows.filter(r => r.status !== 'Match').length;
        return ok(config, {
          readinessVerdict: mismatchCount === 0 ? 'APPROVED_FOR_OPEN_CLOSE' : 'BLOCKED_BY_MISMATCH',
          journalBatchId: 'GL-JRN-DEMO-01',
          checkedRows: demoReconciliationRows.length,
          mismatchCount,
          outboxSynced: true,
          verifiedAtUtc: '2026-10-17T08:00:00Z',
        });
      }
      if (path === '/api/approvals/escalate-overdue') {
        return ok(config, { escalationBatchId: 'ESC-SLA-DEMO', escalatedStatus: 'ESCALATED_TO_SUPERVISOR' });
      }
      if (path === '/api/user-warehouse-access/verify-sod-isolation') {
        return ok(config, {
          auditId: 'RBAC-SOD-DEMO-01',
          warehouseId: 1,
          activeMemberships: 4,
          crossSiteLeakageDetected: false,
          sodPolicyStatus: 'ZERO_PRIVILEGE_ESCALATION_VERIFIED',
          status: 'ZERO_PRIVILEGE_ESCALATION_VERIFIED',
          sodConflictCount: 0,
          multiWarehouseIsolationEnforced: true,
          auditedAtUtc: '2026-10-17T08:00:00Z',
        });
      }
    }

    if (method !== 'get') {
      return fail(config, 405, 'Vercel Demo là môi trường chỉ đọc. Thao tác ghi đã bị chặn.');
    }

    if (path === '/api/auth/me') {
      return ok(config, {
        userId: 1,
        username: 'Demo Viewer',
        role: 'Viewer',
        permissions: ['product.read', 'product_category.read', 'warehouse.read', 'location.read', 'dock.read', 'dock_appointment.read', 'yard.read', 'uom.read', 'partner.read', 'purchase_order.read', 'asn.read', 'receipt.read', 'export_receipt.read', 'allocation.read', 'picking.read', 'packing.read', 'handling_unit.read', 'shipment.read', 'sales_order.read', 'backorder.read', 'inventory.read', 'inventory_availability.read', 'inventory_ledger.read', 'inventory_traceability.read', 'inventory_lock.read', 'receiving_discrepancy.read', 'reason_code.read', 'putaway.read', 'permission.read', 'role.read'],
      });
    }

    if (path === '/api/warehouses') return ok(config, demoWarehouses);
    if (/^\/api\/warehouses\/\d+\/calendar$/.test(path)) {
      const warehouseId = findNumericId(path);
      const item = demoWarehouseCalendars.find(calendar => calendar.warehouseId === warehouseId);
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy lịch vận hành kho demo.');
    }
    if (path === '/api/dock-yard/warehouses') return ok(config, demoDockYardWarehouses);
    if (path === '/api/dock-yard/docks') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      return ok(config, demoDocks.filter(item => !warehouseId || item.warehouseId === warehouseId));
    }
    if (path === '/api/dock-yard/yard-slots') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      return ok(config, demoYardSlots.filter(item => !warehouseId || item.warehouseId === warehouseId));
    }
    if (path === '/api/dock-yard/appointments') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      const direction = params.get('direction');
      const search = (params.get('search') ?? '').toLowerCase();
      return ok(config, demoDockAppointments.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!status || String(item.status) === status)
        && (!direction || String(item.direction) === direction)
        && (!search || [item.code, item.carrierName ?? '', item.vehiclePlate ?? '', item.driverName ?? '', item.dockCode ?? '', item.yardSlotCode ?? ''].join(' ').toLowerCase().includes(search))
      ));
    }
    if (/^\/api\/dock-yard\/appointments\/\d+$/.test(path)) {
      const item = demoDockAppointments.find(appointment => appointment.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy appointment Dock/Yard demo.');
    }

    if (path === '/api/units') return ok(config, demoUnits);
    if (path === '/api/product-categories') return ok(config, demoCategories);

    if (path === '/api/products') return ok(config, demoProducts);
    if (/^\/api\/products\/\d+$/.test(path)) {
      const item = demoProducts.find(product => product.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy sản phẩm demo.');
    }
    if (path === '/api/product-barcodes/lookup') {
      const value = params.get('value') ?? '';
      const item = demoProducts.find(product => product.barcodes.some(barcode => barcode.value === value));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy mã vạch demo.');
    }

    if (path === '/api/business-partners') {
      const role = (params.get('role') ?? '').toLowerCase();
      const search = (params.get('search') ?? '').toLowerCase();
      const active = params.get('active');
      let items = demoPartners.filter(item =>
        (!role || (role === 'supplier' ? item.isSupplier : role === 'customer' ? item.isCustomer : true))
        && (!search || item.code.toLowerCase().includes(search) || item.name.toLowerCase().includes(search))
        && (active === null || String(item.isActive) === active)
      );
      const page = Number(params.get('page') ?? 1);
      const pageSize = Number(params.get('pageSize') ?? 10);
      return ok(config, paged(items, page, pageSize));
    }

    if (path === '/api/approvals/queue') {
      const type = params.get('documentType');
      const keyword = (params.get('keyword') ?? '').toLowerCase();
      const sla = params.get('slaStatus');
      const items = demoApprovalQueue.filter(item =>
        (!type || item.documentType === type)
        && (!keyword || item.documentCode.toLowerCase().includes(keyword))
        && (!sla || item.slaStatus === sla)
      );
      return ok(config, paged(items, Number(params.get('pageIndex') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (/^\/api\/approvals\/[^/]+\/\d+$/.test(path)) {
      const parts = path.split('/');
      const documentType = parts[3];
      const id = Number(parts[4]);
      const item = demoApprovalQueue.find(entry => entry.documentType.toLowerCase() === documentType && entry.documentId === id);
      return item ? ok(config, {
        summary: item,
        note: 'Dữ liệu demo read-only',
        lines: [{ productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', quantity: item.totalQuantity }],
        history: [{ id: 1, timestampUtc: item.requestedAtUtc, actorName: item.creatorName, displayAction: 'Submitted', newState: item.pendingState, result: 'Success', correlationId: 'DEMO-APPROVAL-001' }],
      }) : fail(config, 404, 'Không tìm thấy approval demo.');
    }

    if (path === '/api/stock-transfers') {
      const search = (params.get('search') ?? '').toLowerCase();
      const status = params.get('status') ?? '';
      const items = demoTransfers.filter(item =>
        (!search || item.code.toLowerCase().includes(search))
        && (!status || String(item.status) === status)
      );
      return ok(config, paged(items, Number(params.get('pageIndex') ?? 1), Number(params.get('pageSize') ?? 15)));
    }
    if (/^\/api\/stock-transfers\/\d+$/.test(path)) {
      const item = demoTransfers.find(transfer => transfer.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy transfer demo.');
    }

    if (path === '/api/putaway-tasks/location-warehouses') return ok(config, demoWarehouses);
    if (path === '/api/putaway-tasks/locations') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const structure = demoWarehouseStructures.find(item => item.warehouseId === warehouseId);
      if (!structure) return ok(config, []);
      const physical = structure.zones.flatMap(zone =>
        zone.aisles.flatMap(aisle =>
          aisle.racks.flatMap(rack =>
            rack.levels.flatMap(level =>
              level.locations.map(location => ({
                ...location,
                structurePath: [zone.code, aisle.code, rack.code, 'L' + String(level.levelNo).padStart(2, '0'), location.code.split('-').slice(-1)[0]].join('/'),
                rowVersion: null,
              }))
            )
          )
        )
      );
      return ok(config, [
        ...physical,
        ...structure.unmappedLocations.map(location => ({ ...location, structurePath: null, rowVersion: null })),
        ...structure.systemLocations.map(location => ({ ...location, structurePath: null, rowVersion: null })),
      ]);
    }

    if (path === '/api/putaway-tasks/location-capacity') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const structure = demoWarehouseStructures.find(item => item.warehouseId === warehouseId);
      if (!structure) return ok(config, []);
      const physical = structure.zones.flatMap(zone => zone.aisles.flatMap(aisle => aisle.racks.flatMap(rack => rack.levels.flatMap(level =>
        level.locations.map(location => {
          const usage = location.code.endsWith('B04')
            ? { usedWeightKg: 1300, usedVolumeM3: 8.2, usedPalletEquivalent: 4 }
            : location.code.endsWith('B05')
              ? { usedWeightKg: 620, usedVolumeM3: 4.1, usedPalletEquivalent: 2 }
              : { usedWeightKg: 0, usedVolumeM3: 0, usedPalletEquivalent: 0 };
          const ratios = [
            location.maxWeightKg ? usage.usedWeightKg / location.maxWeightKg : 0,
            location.maxVolumeM3 ? usage.usedVolumeM3 / location.maxVolumeM3 : 0,
            location.maxPalletEquivalent ? usage.usedPalletEquivalent / location.maxPalletEquivalent : 0,
          ];
          const state = !location.isActive ? 'Inactive' : location.isBlocked ? 'Blocked' : Math.max(...ratios) >= 0.85 ? 'NearCapacity' : 'Available';
          return {
            locationId: location.id,
            code: location.code,
            name: location.name,
            structurePath: [zone.code, aisle.code, rack.code, 'L' + String(level.levelNo).padStart(2, '0'), location.code.split('-').slice(-1)[0]].join('/'),
            storageClass: location.storageClass,
            maxWeightKg: location.maxWeightKg,
            usedWeightKg: usage.usedWeightKg,
            maxVolumeM3: location.maxVolumeM3,
            usedVolumeM3: usage.usedVolumeM3,
            maxPalletEquivalent: location.maxPalletEquivalent,
            usedPalletEquivalent: usage.usedPalletEquivalent,
            profileIncomplete: false,
            compatibilityConflict: false,
            isActive: location.isActive,
            isBlocked: location.isBlocked,
            state,
          };
        })
      ))));
      return ok(config, physical);
    }

    if (path === '/api/putaway-tasks/location-map') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const structure = demoWarehouseStructures.find(item => item.warehouseId === warehouseId);
      if (!structure) return ok(config, { warehouseId, generatedAtUtc: new Date().toISOString(), items: [] });
      const items = structure.zones.flatMap(zone => zone.aisles.flatMap(aisle => aisle.racks.flatMap(rack => rack.levels.flatMap(level =>
        level.locations.map((location, index) => {
          const utilization = location.code.endsWith('B04') ? 86.67 : location.code.endsWith('B05') ? 41.33 : 0;
          const capacityState = !location.isActive ? 'Inactive' : location.isBlocked ? 'Blocked' : utilization >= 85 ? 'NearCapacity' : 'Available';
          const recentMovementCount = index === 0 ? 12 : index === 1 ? 5 : index === 2 ? 2 : 0;
          const activePutawayCount = index === 0 ? 5 : index === 1 ? 2 : 0;
          const activityLevel = recentMovementCount >= 10 || activePutawayCount >= 5 ? 'High' : recentMovementCount >= 4 || activePutawayCount >= 2 ? 'Medium' : 'Low';
          return {
            locationId: location.id,
            code: location.code,
            name: location.name,
            structurePath: [zone.code, aisle.code, rack.code, 'L' + String(level.levelNo).padStart(2, '0'), location.code.split('-').slice(-1)[0]].join('/'),
            storageClass: location.storageClass,
            mapX: location.mapX,
            mapY: location.mapY,
            mapWidth: location.mapWidth,
            mapHeight: location.mapHeight,
            utilizationPercent: utilization,
            capacityState,
            recentMovementCount,
            activePutawayCount,
            activityLevel,
            isActive: location.isActive,
            isBlocked: location.isBlocked,
            rowVersion: null,
          };
        })
      ))));
      return ok(config, { warehouseId, generatedAtUtc: new Date().toISOString(), items });
    }

    if (path === '/api/putaway-tasks') return ok(config, demoPutawayTasks);
    if (/^\/api\/putaway-tasks\/\d+$/.test(path)) {
      const item = demoPutawayTasks.find(task => task.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy nhiệm vụ cất hàng demo.');
    }
    if (/^\/api\/putaway-tasks\/\d+\/items\/\d+\/destinations$/.test(path)) {
      return ok(config, [
        { id: 101, code: 'A01-R02-L03-B04', name: 'Bin A01-R02-L03-B04', locationType: 'Bin', isPickable: true },
        { id: 102, code: 'A01-R02-L03-B05', name: 'Bin A01-R02-L03-B05', locationType: 'Bin', isPickable: true },
      ]);
    }

    if (path === '/api/inventory/reversal-reasons') {
      return ok(config, [
        { code:'LOCATION_ERROR', name:'Sai vị trí lưu kho', transactionType:'Move' },
        { code:'STATUS_ERROR', name:'Sai trạng thái tồn kho', transactionType:'StatusChange' },
        { code:'OPERATION_CORRECTION', name:'Hiệu chỉnh nghiệp vụ sau kiểm tra', transactionType:null },
        { code:'DATA_ENTRY_ERROR', name:'Sai sót nhập liệu', transactionType:null },
      ]);
    }
    if (path === '/api/inventory/reversal-warehouses') {
      return ok(config, demoWarehouses.map(item => ({ id: item.id, code: item.code, name: item.name })));
    }
    if (path === '/api/inventory/traceability-warehouses') {
      // Demo only: read-only example data. Real mode uses the protected
      // inventory_traceability.read API + current user's warehouse membership.
      return ok(config, demoWarehouses.map(item => ({ id: item.id, code: item.code, name: item.name })));
    }
    if (path === '/api/inventory/reversal-candidates') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const transactionId = Number(params.get('transactionId') ?? 0);
      const reversedFilter = params.get('isReversed');
      const codePrefix = params.get('productCode')?.trim().toLocaleLowerCase() ?? '';
      const reversalMarkers = demoInventoryTransactions
        .filter(item => item.transactionType === 'Reversal' && item.referenceType === 'InventoryReversal');
      const reversedIds = new Set(reversalMarkers.map(item => item.referenceId));
      const correctionIds = new Set(reversalMarkers.map(item => item.correctiveTransactionId));
      const candidates = demoInventoryTransactions
        .filter(item => (item.transactionType === 'Move' || item.transactionType === 'StatusChange')
          && !correctionIds.has(item.id)
          && (!warehouseId || item.warehouseId === warehouseId)
          && (!transactionId || item.id === transactionId)
          && (!codePrefix || (demoProducts.find(p => p.id === item.productId)?.code?.toLocaleLowerCase().startsWith(codePrefix) ?? false)))
        .map(item => ({ ...item, isReversed: reversedIds.has(item.id) }))
        .filter(item => reversedFilter === null || item.isReversed === (reversedFilter === 'true'))
        .sort((a, b) => new Date(b.transactionDate).getTime() - new Date(a.transactionDate).getTime() || b.id - a.id);
      return ok(config, paged(candidates, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (path === '/api/inventory/statuses') return ok(config, demoInventoryStatuses);
    if (path === '/api/inventory/locks') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = (params.get('status') ?? '').toLowerCase();
      return ok(config, demoInventoryLocks.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!status || item.status.toLowerCase() === status)
      ));
    }
    if (/^\/api\/inventory\/locks\/\d+$/.test(path)) {
      const item = demoInventoryLocks.find(lock => lock.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Inventory Lock demo.');
    }

    if (path === '/api/inventory/buckets') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const status = (params.get('status') ?? '').toUpperCase();
      const lotNumber = (params.get('lotNumber') ?? '').toLowerCase();
      const serialNumber = (params.get('serialNumber') ?? '').toLowerCase();
      return ok(config, demoInventoryBuckets.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!status || item.status === status)
        && (!lotNumber || (item.lotNumber ?? '').toLowerCase().includes(lotNumber))
        && (!serialNumber || (item.serialNumber ?? '').toLowerCase().includes(serialNumber))
      ));
    }

    if (path === '/api/inventorystocks/current') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const keyword = (params.get('keyword') ?? '').toLowerCase();
      const items = demoInventoryStocks.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!keyword || item.productCode.toLowerCase().includes(keyword) || item.productName.toLowerCase().includes(keyword))
      );
      return ok(config, items);
    }
    if (path === '/api/inventorytransactions') {
      const transactionType = params.get('transactionType');
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const keyword = (params.get('keyword') ?? '').toLowerCase();
      const items = demoInventoryTransactions.filter(item =>
        (!transactionType || item.transactionType.toLowerCase() === transactionType.toLowerCase())
        && (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!keyword || item.productCode.toLowerCase().includes(keyword) || item.productName.toLowerCase().includes(keyword))
      );
      return ok(config, paged(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }

    if (path === '/api/inventory/traceability') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const lotNumber = (params.get('lotNumber') ?? '').trim().toLowerCase();
      const serialNumber = (params.get('serialNumber') ?? '').trim().toLowerCase();
      const referenceType = params.get('referenceType')?.trim() ?? '';
      const referenceId = Number(params.get('referenceId') ?? 0);
      const limit = Math.min(500, Math.max(1, Number(params.get('limit') ?? 200)));
      const bucketOffset = Number(params.get('bucketOffset') ?? 0);
      const eventOffset = Number(params.get('eventOffset') ?? 0);
      const hasReference = Boolean(referenceType && referenceId);
      if (!Number.isSafeInteger(bucketOffset) || bucketOffset < 0 ||
          bucketOffset > 50000 || bucketOffset % 500 !== 0)
        return fail(config, 400, 'Trang nhóm tồn không hợp lệ.');
      if (!Number.isSafeInteger(eventOffset) || eventOffset < 0 ||
          eventOffset > 50000 || eventOffset % limit !== 0)
        return fail(config, 400, 'Trang sự kiện sổ cái không hợp lệ.');
      if (Boolean(referenceType) !== Boolean(referenceId))
        return fail(config, 400, 'Loại tham chiếu và ID tham chiếu phải được nhập cùng nhau.');
      if (!warehouseId && !productId && !lotNumber && !serialNumber && !hasReference)
        return fail(config, 400, 'Cần ít nhất Kho, Sản phẩm, Lô, Sê-ri hoặc Tham chiếu.');
      if (warehouseId && !demoWarehouses.some(item=>item.id===warehouseId))
        return fail(config, 404, 'Không tìm thấy kho trong dữ liệu mô phỏng.');

      const matchingEvents = demoInventoryTransactions.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!lotNumber || (item.lotNumber ?? '').toLowerCase() === lotNumber)
        && (!serialNumber || (item.serialNumber ?? '').toLowerCase() === serialNumber)
        && (!hasReference || (item.referenceType === referenceType && item.referenceId === referenceId))
      ).sort((a,b)=>new Date(b.transactionDate).getTime()-new Date(a.transactionDate).getTime()||b.id-a.id);
      const eventsTruncated = matchingEvents.length > eventOffset + limit;
      const events = matchingEvents.slice(eventOffset, eventOffset + limit);
      const reversed = new Set(demoInventoryTransactions
        .filter(item => item.transactionType === 'Reversal' && item.referenceType === 'InventoryReversal' && typeof item.referenceId === 'number')
        .map(item => item.referenceId as number));
      // Document scope intersects every stock filter. Use all direct matching
      // document events, never just the paged ledger window.
      const matchedIdentity = (bucket: (typeof demoInventoryBuckets)[number]) =>
        matchingEvents.some(event =>
          event.productId===bucket.productId && event.warehouseId===bucket.warehouseId
          && (event.lotId??null)===(bucket.lotId??null)
          && (event.serialId??null)===(bucket.serialId??null));
      const matchedBuckets = demoInventoryBuckets.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!lotNumber || (item.lotNumber ?? '').toLowerCase() === lotNumber)
        && (!serialNumber || (item.serialNumber ?? '').toLowerCase() === serialNumber)
        && (!hasReference || matchedIdentity(item))
      ).sort((a,b)=>
        a.productCode.localeCompare(b.productCode) ||
        a.warehouseId-b.warehouseId ||
        (a.locationId??0)-(b.locationId??0) ||
        a.status.localeCompare(b.status) ||
        (a.lotId??0)-(b.lotId??0) ||
        (a.serialId??0)-(b.serialId??0) ||
        a.inventoryStockId-b.inventoryStockId);
      const currentPage=matchedBuckets.slice(bucketOffset,bucketOffset+501);
      return ok(config, {
        currentBuckets:currentPage.slice(0,500).map(item=>({
          ...item,inventoryStatus:item.status,
        })),
        bucketsTruncated:currentPage.length>500,
        eventsTruncated,
        events:events.map(item=>({
          ...item,
          transactionId:item.id,
          reversalOfTransactionId:item.transactionType==='Reversal' && item.referenceType==='InventoryReversal'
            ? item.referenceId : null,
          isReversed:reversed.has(item.id),
        })),
      });
    }
    if (path === '/api/reports/inventory-in-out-stock') return ok(config, demoInOut);
    if (path === '/api/reports/inventory/export' || path === '/api/reports/inventory-in-out-stock/export') {
      return ok(config, new Blob(['Read-only demo export - no production data'], { type: 'text/plain' }));
    }

    if (path === '/api/purchase-orders') {
      const status = params.get('status');
      return ok(config, demoPurchaseOrders
        .filter(item => !status || item.status === status)
        .map(item => ({
          ...item,
          lines: (item.lines ?? []).map(line => ({
            ...line,
            lineNumber: line.lineNo,
            unitName: line.operationUnitCode,
            receivedQuantity: item.status === 'PartiallyReceived' ? Math.floor(line.orderedQuantity / 2) : 0,
            remainingQuantity: item.status === 'PartiallyReceived' ? line.orderedQuantity - Math.floor(line.orderedQuantity / 2) : line.orderedQuantity,
          })),
        })));
    }
    if (/^\/api\/purchase-orders\/\d+$/.test(path)) {
      const item = demoPurchaseOrders.find(order => order.id === findNumericId(path));
      return item ? ok(config, {
        ...item,
        lines: (item.lines ?? []).map(line => ({
          ...line,
          lineNumber: line.lineNo,
          unitName: line.operationUnitCode,
          receivedQuantity: item.status === 'PartiallyReceived' ? Math.floor(line.orderedQuantity / 2) : 0,
          remainingQuantity: item.status === 'PartiallyReceived' ? line.orderedQuantity - Math.floor(line.orderedQuantity / 2) : line.orderedQuantity,
        })),
      }) : fail(config, 404, 'Không tìm thấy đơn mua demo.');
    }

    if (path === '/api/asns') {
      const status = params.get('status');
      return ok(config, demoAsns
        .filter(item => !status || item.status === status)
        .map(item => ({
          ...item,
          lines: (item.lines ?? []).map(line => ({
            ...line,
            lineNumber: line.lineNo,
            unitName: line.operationUnitCode,
            receivedQuantity: 0,
            remainingQuantity: line.expectedQuantity,
          })),
        })));
    }
    if (/^\/api\/asns\/\d+$/.test(path)) {
      const item = demoAsns.find(asn => asn.id === findNumericId(path));
      return item ? ok(config, {
        ...item,
        lines: (item.lines ?? []).map(line => ({
          ...line,
          lineNumber: line.lineNo,
          unitName: line.operationUnitCode,
          receivedQuantity: 0,
          remainingQuantity: line.expectedQuantity,
        })),
      }) : fail(config, 404, 'Không tìm thấy ASN demo.');
    }

    if (path === '/api/importreceipts') return ok(config, demoImportReceipts);
    if (/^\/api\/importreceipts\/\d+$/.test(path)) {
      const item = demoImportReceipts.find(receipt => receipt.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy phiếu nhập demo.');
    }
    if (/^\/api\/importreceipts\/\d+\/inventory-identities$/.test(path)) {
      const id = findNumericId(path);
      return ok(config, demoImportReceiptInventoryIdentities[id] ?? []);
    }
    if (/^\/api\/importreceipts\/\d+\/discrepancies$/.test(path)) return ok(config, []);
    if (path === '/api/importreceipts/discrepancy-reasons') {
      return ok(config, [{ code: 'COUNT_VARIANCE', name: 'Chênh lệch kiểm đếm', version: 1, requiresNote: true, requiresAttachment: false, requiresApproval: false }]);
    }

    if (path === '/api/exportreceipts') return ok(config, demoExportReceipts);
    if (/^\/api\/exportreceipts\/\d+$/.test(path)) {
      const item = demoExportReceipts.find(receipt => receipt.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy phiếu xuất demo.');
    }

    if (path === '/api/stock-reservations') {
      const status = params.get('status');
      const items = demoReservations.filter(item => !status || item.status === status);
      return ok(config, paged(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }

    if (path === '/api/inventory/allocations/reservations') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      return ok(config, demoAllocatableReservations.filter(item => !warehouseId || item.warehouseId === warehouseId));
    }
    if (path === '/api/inventory/allocations/candidates') {
      const reservationId = Number(params.get('reservationId') ?? 0);
      return ok(config, demoAllocationCandidates
        .filter(item => !reservationId || item.reservationId === reservationId)
        .filter(item => item.allocatableQuantity > 0)
        .map(({ reservationId: _reservationId, ...item }) => item));
    }
    if (path === '/api/inventory/allocations') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const reservationId = Number(params.get('reservationId') ?? 0);
      const status = params.get('status');
      const items = demoAllocations.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!reservationId || item.reservationId === reservationId)
        && (!status || item.status === status)
      );
      return ok(config, paged(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (/^\/api\/inventory\/allocations\/\d+$/.test(path)) {
      const item = demoAllocations.find(allocation => allocation.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Allocation demo.');
    }

    if (path === '/api/picking-tasks') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoPickingTasks
        .filter(item => (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status))
        .map(({ lines: _lines, shortPicks: _shortPicks, rowVersion: _rowVersion, ...item }) => item));
    }
    if (/^\/api\/picking-tasks\/\d+$/.test(path)) {
      const item = demoPickingTasks.find(task => task.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy nhiệm vụ Picking demo.');
    }

    if (path === '/api/packing-sessions') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoPackingSessions
        .filter(item => (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status))
        .map(({ lines: _lines, handlingUnits: _handlingUnits, rowVersion: _rowVersion, ...item }) => item));
    }
    if (/^\/api\/packing-sessions\/\d+$/.test(path)) {
      const item = demoPackingSessions.find(session => session.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Packing session demo.');
    }
    if (path === '/api/handling-units') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      return ok(config, demoHandlingUnits.filter(item => !warehouseId || item.warehouseId === warehouseId));
    }
    if (/^\/api\/handling-units\/\d+$/.test(path)) {
      const item = demoHandlingUnits.find(hu => hu.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Handling Unit demo.');
    }

    if (path === '/api/shipments') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoShipments
        .filter(item => (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status))
        .map(({ handlingUnits: _handlingUnits, proofOfDelivery: _proof, trackingEvents: _events, rowVersion: _rowVersion, ...item }) => item));
    }
    if (/^\/api\/shipments\/\d+\/tracking$/.test(path)) {
      const item = demoShipments.find(shipment => shipment.id === findNumericId(path));
      return item ? ok(config, {
        shipmentId: item.id,
        shipmentCode: item.shipmentCode,
        status: item.status,
        dispatchedAt: item.dispatchedAt ?? null,
        inTransitAt: item.inTransitAt ?? null,
        deliveryFailedAt: item.deliveryFailedAt ?? null,
        returnInitiatedAt: item.returnInitiatedAt ?? null,
        deliveredAt: item.deliveredAt ?? null,
        completedAt: item.completedAt ?? null,
        proofOfDelivery: item.proofOfDelivery ?? null,
        events: item.trackingEvents ?? [],
      }) : fail(config, 404, 'Không tìm thấy tracking Shipment demo.');
    }
    if (/^\/api\/shipments\/\d+$/.test(path)) {
      const item = demoShipments.find(shipment => shipment.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Shipment demo.');
    }

    if (path === '/api/sales-orders') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoSalesOrders
        .filter(item => (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status))
        .map(({ lines: _lines, rowVersion: _rowVersion, ...item }) => item));
    }
    if (/^\/api\/sales-orders\/\d+$/.test(path)) {
      const item = demoSalesOrders.find(order => order.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Sales Order demo.');
    }
    if (path === '/api/backorders') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoBackorders.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status)
      ));
    }
    if (/^\/api\/backorders\/\d+$/.test(path)) {
      const item = demoBackorders.find(row => row.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Backorder demo.');
    }

    if (path === '/api/stocktakes') return ok(config, demoStocktakes.map(item => ({
      id: item.id,
      code: item.code,
      warehouseId: item.warehouseId,
      warehouseName: item.warehouseName,
      status: item.status,
      note: item.note,
      createdBy: item.createdBy,
      createdByName: item.createdByName,
      createdAt: item.createdAt,
      detailCount: item.detailCount,
    })));
    if (/^\/api\/stocktakes\/\d+$/.test(path)) {
      const item = demoStocktakes.find(stocktake => stocktake.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy phiếu kiểm kê demo.');
    }

    if (path === '/api/permissions') return ok(config, demoPermissionCatalog);
    if (path === '/api/permissions/roles') {
      return ok(config, [
        { id: 1, roleName: 'Admin', rowVersion: 'AAAAAAAAROLE1', permissions: demoPermissionCatalog.map(item => item.code) },
        { id: 2, roleName: 'Viewer', rowVersion: 'AAAAAAAAROLE2', permissions: demoPermissionCatalog.map(item => item.code) },
      ]);
    }

    if (path === '/api/inventoryreconciliation') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const keyword = (params.get('keyword') ?? '').toLowerCase();
      const items = demoReconciliationRows.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!keyword || item.productCode.toLowerCase().includes(keyword) || item.productName.toLowerCase().includes(keyword))
      );
      return ok(config, paged(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (path === '/api/inventoryreconciliation/warehouses') return ok(config, demoReconciliationWarehouses);
    if (path === '/api/inventoryreconciliation/investigation') {
      const warehouseId = Number(params.get('warehouseId') ?? 1);
      const productId = Number(params.get('productId') ?? 1);
      const row = demoReconciliationRows.find(r => r.warehouseId === warehouseId && r.productId === productId) ?? demoReconciliationRows[0];
      return ok(config, {
        warehouseId: row.warehouseId,
        warehouseName: row.warehouseName,
        productId: row.productId,
        productCode: row.productCode,
        productName: row.productName,
        unitName: 'EA',
        stockRowExists: true,
        currentQuantity: row.currentQuantity,
        reservedQuantity: 0,
        bucketTotalQuantity: row.currentQuantity,
        ledgerMovementNetQuantity: row.expectedQuantity ?? row.currentQuantity,
        allStatusBucketQuantity: row.currentQuantity,
        allStatusEventDeltaQuantity: row.expectedQuantity ?? row.currentQuantity,
        allStatusBucketCount: 1,
        allStatusBucketsTruncated: false,
        bucketsTruncated: false,
        eventsTruncated: false,
        buckets: [],
        events: [],
      });
    }

    if (path === '/api/approvals/queue') {
      return ok(config, paged(demoApprovalQueue, Number(params.get('pageIndex') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (path === '/api/approvals/history') return ok(config, []);

    if (path === '/api/stock-transfers') {
      return ok(config, paged(demoTransfers, Number(params.get('pageIndex') ?? 1), Number(params.get('pageSize') ?? 15)));
    }
    if (/^\/api\/stock-transfers\/\d+$/.test(path)) {
      const item = demoTransfers.find(transfer => transfer.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy phiếu điều chuyển demo.');
    }

    if (path === '/api/reports/expiry-aging') {
      const items = demoInventoryBuckets.map(item => ({
        inventoryStockId: item.inventoryStockId,
        bucketId: item.inventoryStockId,
        warehouseId: item.warehouseId,
        warehouseName: item.warehouseName,
        locationId: item.locationId,
        locationCode: item.locationCode,
        productId: item.productId,
        productCode: item.productCode,
        productName: item.productName,
        unitName: 'EA',
        status: item.status,
        inventoryStatus: item.status,
        lotId: item.lotId,
        lotNumber: item.lotNumber,
        manufactureDate: item.manufactureDate,
        expiryDate: item.expiryDate,
        daysToExpiry: item.expiryDate ? 45 : null,
        expiryBucket: item.expiryDate ? '31_TO_90_DAYS' : 'NO_EXPIRY',
        expiryStatus: item.expiryDate ? 'GOOD' : 'NO_EXPIRY',
        receivedAt: item.lastUpdated,
        ageDays: 14,
        ageInDays: 14,
        agingBucket: '0_30_DAYS',
        isSlowMoving: false,
        onHandQuantity: item.onHandQuantity,
        quantity: item.onHandQuantity,
        reservedQuantity: item.reservedQuantity,
        availableQuantity: item.availableQuantity,
      }));
      const summary = {
        expiredLotsCount: 0,
        expiringIn7DaysCount: 0,
        expiringIn30DaysCount: 0,
        expiringIn90DaysCount: items.filter(x => x.expiryDate !== null).length,
        slowMovingLotsCount: 0,
        totalTrackedLots: items.filter(x => x.lotNumber !== null).length,
      };
      return ok(config, Object.assign(items, { summary, items }));
    }
    if (path === '/api/reports/analytics-summary') {
      return ok(config, {
        windowDays: Number(params.get('days') ?? 30) || 30,
        inboundOutboundSla: {
          completedReceiptsCount: 12,
          avgInboundLeadTimeHours: 4.2,
          openPutawayBacklogCount: 1,
          dispatchedExportsCount: 18,
          avgOutboundLeadTimeHours: 3.5,
          openPickingBacklogCount: 1,
        },
        stocktakeAccuracy: {
          approvedStocktakesCount: 4,
          totalLinesCounted: 32,
          exactMatchLinesCount: 31,
          varianceLinesCount: 1,
          accuracyRatePercent: 96.88,
          totalAbsoluteVarianceQty: 2,
        },
        turnoverAndDaysOnHand: demoProducts.map(item => ({
          productId: item.id,
          productCode: item.code,
          productName: item.name,
          unitName: item.unitName,
          onHandQuantity: 120,
          periodInQuantity: 60,
          periodOutQuantity: 45,
          turnoverRate: 1.5,
          daysOnHand: 20,
          velocityClass: 'FAST_MOVING',
        })),
      });
    }
    if (path === '/api/reports/productivity-and-bi-snapshot') {
      return ok(config, {
        warehouseId: Number(params.get('warehouseId') ?? 1),
        activeTasksCount: 3,
        completedTodayCount: 12,
        heatmapHotspotsCount: 2,
        biExportStatus: 'READY',
        completedPutawaysCount: 5,
        completedPicksCount: 4,
        completedPacksCount: 3,
        totalMovesCount: 12,
        stockSnapshotRowCount: demoInventoryBuckets.length,
        ledgerWatermarkId: demoInventoryTransactions.length,
        auditWatermarkId: 108,
        readyForBiExport: true,
        generatedAtUtc: '2026-10-17T08:00:00Z',
      });
    }
    if (path === '/api/reports/operational-dashboard') {
      return ok(config, {
        warehouseCount: demoWarehouses.length,
        productCount: demoProducts.length,
        inventoryBucketCount: demoInventoryBuckets.length,
        openTaskCount: 4,
        invalidStockCount: demoReconciliationRows.filter(r => r.status !== 'Match').length,
        openInboundCount: 1,
        openPutawayCount: 1,
        openOutboundCount: 1,
        openTransferCount: 1,
        recentActivities: [],
      });
    }
    if (path === '/api/reports/warehouse-tasks' || path === '/api/reports/operational-exceptions') {
      return ok(config, []);
    }
    if (path === '/api/reports/multi-warehouse-control-tower') {
      return ok(config, {
        totalWarehouses: demoWarehouses.length,
        healthyWarehousesCount: demoWarehouses.length,
        attentionWarehousesCount: 0,
        warehouses: [],
        taskEngineQueues: [],
      });
    }
    if (path === '/api/reports/global-search') {
      const q = (params.get('q') ?? '').toLowerCase();
      return ok(
        config,
        demoProducts
          .filter(p => !q || p.code.toLowerCase().includes(q) || p.name.toLowerCase().includes(q))
          .map(p => ({ entityType: 'Product', code: p.code, title: p.name, subtitle: p.categoryName, route: '/products' })),
      );
    }

    if (path === '/api/stock-transfers/in-transit') {
      const items = demoTransfers
        .filter(t => t.status === 'InTransit')
        .flatMap(t =>
          t.details.map(d => ({
            transferId: t.id,
            transferCode: t.code,
            sourceWarehouseName: t.sourceWarehouseName,
            destinationWarehouseName: t.destinationWarehouseName,
            productId: d.productId,
            productCode: d.productCode,
            productName: d.productName,
            unitName: 'EA',
            inTransitQuantity: d.dispatchedQuantity,
            dispatchedAt: t.dispatchedAt,
          })),
        );
      return ok(config, { items });
    }
    if (path === '/api/stock-transfers/return-reasons') {
      return ok(config, [
        { code: 'DAMAGED_IN_TRANSIT', name: 'Hàng hư hỏng khi vận chuyển' },
        { code: 'WRONG_DESTINATION', name: 'Điều chuyển nhầm kho nhận' },
      ]);
    }

    if (path === '/api/permissions/audit-logs') {
      return ok(config, [
        {
          id: 1,
          userId: 1,
          action: 'Permission.CatalogView',
          entityName: 'RolePermission',
          entityId: 1,
          newValues: 'Demo read-only audit',
          timestamp: '2026-10-17T08:00:00Z',
        },
      ]);
    }
    if (path === '/api/permissions/compliance-evidence-bundle') {
      return ok(config, {
        bundleId: 'EVD-DEMO-20261017',
        policyVersion: 'v2026.10-CANONICAL',
        legalHoldActive: true,
        appendOnlyAuditVerified: true,
        generatedAtUtc: '2026-10-17T08:00:00Z',
      });
    }
    if (path === '/api/auth/sessions') {
      return ok(config, [
        {
          sessionId: '00000000-0000-0000-0000-000000000001',
          deviceInfo: 'Demo Browser Session',
          ipAddress: '127.0.0.1',
          isCurrent: true,
          createdAtUtc: '2026-10-17T08:00:00Z',
        },
      ]);
    }
    if (/^\/api\/users\/\d+\/security$/.test(path)) {
      return ok(config, {
        userId: findNumericId(path),
        username: 'demo.operator',
        roleName: 'WarehouseStaff',
        isActive: true,
        isTemporarilyLocked: false,
        failedLoginCount: 0,
        activeSessionCount: 1,
      });
    }
    if (/^\/api\/users\/\d+\/warehouse-access$/.test(path)) {
      return ok(config, {
        userId: findNumericId(path),
        username: 'demo.operator',
        fullName: 'Nhân viên vận hành Demo',
        roleName: 'WarehouseStaff',
        rowVersion: 'AAAAAAAADEMOWH',
        warehouses: demoWarehouses.map(w => ({
          warehouseId: w.id,
          warehouseCode: w.code,
          warehouseName: w.name,
          warehouseIsActive: w.isActive,
          grantedAt: '2026-10-01T08:00:00Z',
        })),
      });
    }

    if (
      path === '/api/dock-yard/dwell-monitor'
      || path === '/api/inventory/replenishment-suggestions'
      || path === '/api/picking-tasks/wave-summary'
      || /^\/api\/warehouses\/\d+\/calendar\/exceptions$/.test(path)
    ) {
      return ok(config, []);
    }

    return fail(config, 404, 'Endpoint demo chưa được ánh xạ: ' + (config.url ?? 'unknown'));
  };
};
