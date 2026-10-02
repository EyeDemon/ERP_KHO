export interface MockScenarioStep {
  label: string;
  state: string;
  inventoryEffect: string;
}

export interface MockGoldenScenario {
  id: string;
  title: string;
  spec: string;
  purpose: string;
  precondition: string;
  steps: MockScenarioStep[];
  assertions: string[];
  metrics: Record<string, number | string | boolean>;
}

export const mockGoldenScenarios: MockGoldenScenario[] = [
  {
    id: 'GS-01',
    title: 'Inbound Receipt → Post → Putaway',
    spec: '05 §146 / 34 / 35',
    purpose: 'Chứng minh receipt chỉ tăng inventory tại POST và putaway chỉ đổi location.',
    precondition: 'WH-HCM-01, SKU-1001, OnHand = 0; receiving location RECV-01.',
    steps: [
      { label: 'Receive 100', state: 'RECEIVING → READY_TO_POST', inventoryEffect: 'Không tăng warehouse OnHand.' },
      { label: 'Post Receipt', state: 'READY_TO_POST → POSTED', inventoryEffect: 'RECEIPT ledger +100; RECV-01 +100.' },
      { label: 'Putaway 40', state: 'PUTAWAY IN_PROGRESS', inventoryEffect: 'RECV-01 -40; A01-R02-L03-B04 +40; warehouse total giữ 100.' },
      { label: 'Putaway 60', state: 'PUTAWAY COMPLETED', inventoryEffect: 'RECV-01 0; storage +100; warehouse total giữ 100.' },
    ],
    assertions: ['Receipt ledger exactly once', 'Warehouse OnHand = 100', 'Putaway không đổi warehouse total', 'Audit + Outbox tồn tại'],
    metrics: { initialOnHand: 0, postedQuantity: 100, finalOnHand: 100, finalReceivingLocation: 0, finalStorageLocation: 100 },
  },
  {
    id: 'GS-02',
    title: 'Outbound Reserve → Allocate → Pick → Dispatch',
    spec: '05 §147 / 30 / 36-38',
    purpose: 'Chứng minh reservation làm giảm Available; allocation là tập con; dispatch mới giảm OnHand.',
    precondition: 'SKU-1001 OnHand = 100, Available = 100.',
    steps: [
      { label: 'Reserve 30', state: 'Reservation ACTIVE', inventoryEffect: 'OnHand 100; Reserved 30; Available 70.' },
      { label: 'Allocate 20', state: 'Allocation ACTIVE', inventoryEffect: 'Allocated 20 ≤ Reserved 30; Available vẫn 70.' },
      { label: 'Pick 20', state: 'Picking COMPLETE', inventoryEffect: 'Physical warehouse OnHand vẫn 100.' },
      { label: 'Dispatch 20', state: 'Shipment DISPATCHED', inventoryEffect: 'SHIP ledger -20; OnHand 80; consume commitment tương ứng.' },
    ],
    assertions: ['Allocated <= Reserved', 'Pick không giảm warehouse OnHand', 'Dispatch ledger exactly once', 'Final OnHand = 80'],
    metrics: { initialOnHand: 100, reserved: 30, allocated: 20, availableAfterReserve: 70, dispatchQuantity: 20, finalOnHand: 80 },
  },
  {
    id: 'GS-03',
    title: 'Warehouse Transfer Conservation',
    spec: '05 §148 / 39',
    purpose: 'Chứng minh quantity được bảo toàn qua Source + Transit + Destination.',
    precondition: 'WH-HCM-01 có 100 SKU-1002; WH-DN-01 có 0.',
    steps: [
      { label: 'Create/Approve', state: 'APPROVED', inventoryEffect: 'Source 100; Transit 0; Destination 0.' },
      { label: 'Dispatch 100', state: 'IN_TRANSIT', inventoryEffect: 'Source 0; Transit 100; Destination 0.' },
      { label: 'Receive 100', state: 'RECEIVED', inventoryEffect: 'Source 0; Transit 0; Destination 100.' },
    ],
    assertions: ['Source + Transit + Destination luôn = 100', 'Dispatch/Receive idempotent', 'Transit có source/destination/reference rõ'],
    metrics: { requested: 100, afterDispatchSource: 0, afterDispatchTransit: 100, afterReceiveTransit: 0, finalDestination: 100 },
  },
  {
    id: 'GS-04',
    title: 'Cycle Count → Recount → Adjustment',
    spec: '05 §149 / 40',
    purpose: 'Chứng minh count không đóng trước REVIEW/APPROVAL/POST và mỗi recount attempt được giữ lại.',
    precondition: 'System quantity SKU-2001 = 84.',
    steps: [
      { label: 'Blind Count', state: 'COUNTING → REVIEW', inventoryEffect: 'Counted = 83; inventory chưa đổi.' },
      { label: 'Recount', state: 'RECOUNT → REVIEW', inventoryEffect: 'Attempt 2 = 82; inventory chưa đổi.' },
      { label: 'Final Recount', state: 'RECOUNT → PENDING_APPROVAL', inventoryEffect: 'Attempt 3 = 82 accepted; inventory chưa đổi.' },
      { label: 'Approve + Post', state: 'APPROVED → POSTED → COMPLETED', inventoryEffect: 'ADJUSTMENT -2; final OnHand = 82.' },
    ],
    assertions: ['Attempts immutable', 'Không COMPLETED trước POST', 'Adjustment tạo ledger riêng', 'Final OnHand = 82'],
    metrics: { systemQty: 84, attempt1: 83, attempt2: 82, attempt3: 82, finalAccepted: 82, adjustment: -2, finalOnHand: 82 },
  },
  {
    id: 'GS-05',
    title: 'Inbound QC → Disposition → Receipt Post',
    spec: '05 §150 / 41 canonical override / 228',
    purpose: 'Chứng minh Receive/QC không mutate inventory; POST là boundary duy nhất và ghi quantity theo disposition.',
    precondition: 'Receipt 100 SKU-1002 yêu cầu QC; initial warehouse OnHand = 0.',
    steps: [
      { label: 'Receive 100', state: 'RECEIVING → QC_PENDING', inventoryEffect: 'Không ledger; warehouse OnHand vẫn 0.' },
      { label: 'QC + disposition', state: 'QC_PENDING → READY_TO_POST', inventoryEffect: '80 accepted, 15 damaged, 5 rejected-at-door; vẫn chưa mutate inventory.' },
      { label: 'Post receipt', state: 'READY_TO_POST → POSTED', inventoryEffect: 'AVAILABLE +80; DAMAGED +15; rejected-at-door +0 OnHand.' },
    ],
    assertions: ['Receive/QC không tạo inventory ledger', 'POST là inventory boundary duy nhất', 'Available = 80', 'Damaged = 15', 'Physical OnHand = 95', 'Rejected-at-door không vào OnHand'],
    metrics: { received: 100, accepted: 80, damaged: 15, rejectedAtDoor: 5, available: 80, physicalOnHand: 95, prePostOnHand: 0 },
  },
  {
    id: 'GS-06',
    title: 'Reversal & Corrective Receipt',
    spec: '05 §151 / 32',
    purpose: 'Chứng minh ledger bất biến; sửa sai bằng reversal và corrective transaction.',
    precondition: 'Receipt đã post sai +100.',
    steps: [
      { label: 'Original Receipt', state: 'POSTED', inventoryEffect: 'RECEIPT +100.' },
      { label: 'Reverse', state: 'REVERSED', inventoryEffect: 'REVERSAL -100; original vẫn giữ.' },
      { label: 'Correct Receipt', state: 'POSTED', inventoryEffect: 'RECEIPT +80.' },
    ],
    assertions: ['Giữ đủ 3 transactions', 'Reversal reference original', 'Không double reversal', 'Net = 80'],
    metrics: { original: 100, reversal: -100, correction: 80, net: 80, ledgerTransactions: 3 },
  },
  {
    id: 'GS-07',
    title: 'Concurrent Reservation',
    spec: '05 §152 / 31',
    purpose: 'Chứng minh hai request đồng thời không thể over-reserve.',
    precondition: 'Available = 10; request A = 8; request B = 8.',
    steps: [
      { label: 'Request A + B', state: 'PARALLEL', inventoryEffect: 'Cùng cạnh tranh 10 available.' },
      { label: 'One succeeds', state: 'SUCCESS', inventoryEffect: 'Reserved = 8.' },
      { label: 'One fails', state: '409 INV_INSUFFICIENT_AVAILABLE', inventoryEffect: 'Không mutation lần hai.' },
    ],
    assertions: ['Exactly one success', 'Exactly one conflict', 'Reserved = 8', 'Available = 2', 'Không negative stock'],
    metrics: { initialAvailable: 10, requestA: 8, requestB: 8, successCount: 1, conflictCount: 1, finalReserved: 8, finalAvailable: 2 },
  },
  {
    id: 'GS-08',
    title: 'Idempotent Shipment Dispatch',
    spec: '05 §153 / 31',
    purpose: 'Chứng minh retry cùng Idempotency-Key không dispatch/post/event nhiều lần.',
    precondition: 'Shipment SHP-5001 = LOADED; quantity 20; Idempotency-Key = DEMO-ABC.',
    steps: [
      { label: 'Dispatch request', state: 'COMMIT', inventoryEffect: 'SHIP -20; shipment DISPATCHED.' },
      { label: 'Lost response', state: 'UNKNOWN TO CLIENT', inventoryEffect: 'Server commit vẫn tồn tại.' },
      { label: 'Retry x4', state: 'REPLAY COMPLETED RESULT', inventoryEffect: 'Không thêm ledger/balance/outbox.' },
    ],
    assertions: ['5 requests → 1 dispatch', 'SHIP ledger = 1', 'Balance deduction = 1', 'Outbox event = 1'],
    metrics: { requests: 5, committedDispatches: 1, ledgerRows: 1, balanceMutations: 1, outboxRows: 1, dispatchQuantity: 20 },
  },
  {
    id: 'GS-09',
    title: 'Customer Return Inspection & Posting',
    spec: '43 / 228',
    purpose: 'Chứng minh inspection/disposition không tự tăng stock trước return posting.',
    precondition: 'RMA trả 6 SKU-2001 từ CUS-001.',
    steps: [
      { label: 'Receive physical return', state: 'RECEIVED', inventoryEffect: 'Chưa tăng available inventory.' },
      { label: 'Inspect', state: 'INSPECTION', inventoryEffect: '4 RESTOCK, 2 QUARANTINE; chưa post.' },
      { label: 'Post disposition', state: 'POSTED', inventoryEffect: 'AVAILABLE +4; QUARANTINE +2.' },
    ],
    assertions: ['Return posting là inventory boundary', 'RESTOCK + QUARANTINE = 6', 'Audit + event tồn tại'],
    metrics: { returned: 6, restock: 4, quarantine: 2, posted: 6 },
  },
  {
    id: 'GS-10',
    title: 'Mobile Offline Deferred Sync',
    spec: '146 / 223',
    purpose: 'Chứng minh offline chỉ cho phép command low-risk theo policy và giữ dữ liệu khi sync fail.',
    precondition: 'Mobile mất mạng sau khi tải task snapshot hợp lệ.',
    steps: [
      { label: 'Low-risk confirm', state: 'PENDING_SYNC', inventoryEffect: 'Local queue only; chưa đổi server inventory.' },
      { label: 'Critical post attempt', state: 'BLOCKED_OFFLINE', inventoryEffect: 'Không cho Receipt Post/Dispatch offline.' },
      { label: 'Reconnect + sync', state: 'SYNCED', inventoryEffect: 'Server revalidates auth/state/version trước commit.' },
    ],
    assertions: ['High-risk command bị chặn offline', 'Pending data không mất', 'Sync conflict không silently overwrite', 'Retry có clientCommandId'],
    metrics: { lowRiskQueued: 2, highRiskBlocked: 1, dataLoss: false, serverRevalidation: true },
  },
];

export const getGoldenScenario = (id: string) =>
  mockGoldenScenarios.find((scenario) => scenario.id === id);
