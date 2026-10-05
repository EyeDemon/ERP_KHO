# WH-06 — Dock & Yard Control

Status: **LIVE / PRODUCTION**

Production route: `/dock-yard`

Canonical contracts: Spec 46 — Dock, Yard & Receiving Appointment Management; Spec 248 — Yard Gate, Driver, Vehicle & Check-In/Check-Out; Spec 17 — Permission Registry.

## Boundary

Dock/Yard coordinates vehicles, drivers, yard slots and dock doors. It does **not** post inventory.

- Gate arrival/check-in does not create On Hand.
- Dock assignment does not reserve or move stock.
- Loading/unloading completion is an operational milestone only.
- Inbound inventory remains owned by Goods Receipt POST.
- Outbound inventory remains owned by the dispatch contract.

## Appointment lifecycle

`DRAFT → CONFIRMED → ARRIVED → CHECKED_IN → DOCK_ASSIGNED → IN_SERVICE → COMPLETED`

Additional terminal paths:

- `DRAFT/CONFIRMED → CANCELLED`
- `CONFIRMED → NO_SHOW`
- non-terminal operational states → `EXCEPTION`; nếu xe đã check-in và có yard slot/dock thì occupancy vẫn được giữ cho tới gate checkout
- `COMPLETED → CHECKED_OUT` is represented by `CheckedOutAtUtc`; the appointment remains Completed for operational reporting.
- `EXCEPTION` sau check-in cũng được phép gate checkout; status vẫn là Exception nhưng `CheckedOutAtUtc` giải phóng yard/dock occupancy.

Every transition is Warehouse-scoped and protected by optimistic concurrency.

## Scheduling

Confirming an appointment validates the WH-05 warehouse operational baseline:

- configured warehouse timezone;
- open weekday;
- local opening/closing window, including overnight windows;
- inbound or outbound cutoff.

Dock assignment separately validates the physical dock constraint:

- inbound/outbound compatibility;
- vehicle type compatibility when configured;
- temperature-control requirement;
- hazardous-goods permission;
- no overlapping active appointment on the same dock.

Yard assignment prevents two active, non-checked-out appointments from occupying the same yard slot.

Assignment checks run under a serializable database transaction. The idempotency filter explicitly opens `CheckIn` and `AssignDock` commands at `Serializable` isolation so the outer command transaction does not weaken this occupancy boundary.

## Gate identity

Gate check-in requires actual vehicle and driver identity. If the appointment already contains a planned vehicle/trailer, a mismatch is rejected; the operator must record an exception instead of silently replacing the planned identity.

Optional gate evidence includes:

- driver phone;
- seal number;
- yard slot.

## Exceptions

The production UI supports the canonical operational reasons:

- `LATE_ARRIVAL`
- `EARLY_ARRIVAL`
- `VEHICLE_MISMATCH`
- `DOCK_UNAVAILABLE`
- `DAMAGED_SEAL`
- `CAPACITY_ISSUE`
- `WAITING_TIME_BREACH`

The appointment event stream and AuditLog remain separate evidence: the event stream describes the operational timeline; AuditLog records the security/business mutation evidence and inherits correlation/idempotency metadata from the request context.

## Permissions

Read:

- `dock.read`
- `dock_appointment.read`
- `yard.read`

Mutation:

- `dock.manage`
- `dock_appointment.manage`
- `yard.checkin`
- `yard.assign_dock`
- `yard.checkout`

Permission grants do not bypass Warehouse membership or valid business state.

The deployment seed gives Admin/Manager the full WH-06 bundle, WarehouseStaff operational yard permissions, and Viewer read-only Dock/Yard permissions. These are explicit grants, not role-name authorization in domain logic.

## API

Warehouse selector / scope:

- `GET /api/dock-yard/warehouses` — returns only accessible warehouses plus operational timezone/calendar-configured state under `dock_appointment.read`; the WH-06 UI does not depend on a separate `warehouse.read` grant just to resolve its own scope.

Master data:

- `GET /api/dock-yard/docks?warehouseId={id}`
- `POST /api/dock-yard/docks?warehouseId={id}`
- `PUT /api/dock-yard/docks/{dockId}?warehouseId={id}`
- `GET /api/dock-yard/yard-slots?warehouseId={id}`
- `POST /api/dock-yard/yard-slots?warehouseId={id}`
- `PUT /api/dock-yard/yard-slots/{yardSlotId}?warehouseId={id}`

Appointments:

- `GET /api/dock-yard/appointments`
- `GET /api/dock-yard/appointments/{id}`
- `POST /api/dock-yard/appointments`
- `PUT /api/dock-yard/appointments/{id}`

Commands:

- `POST /confirm`
- `POST /arrive`
- `POST /check-in`
- `POST /assign-dock`
- `POST /start-service`
- `POST /complete`
- `POST /checkout`
- `POST /cancel`
- `POST /no-show`
- `POST /exception`

Every command above is under `/api/dock-yard/appointments/{id}`. POST creates and state transitions require `Idempotency-Key`; the production UI retains the same key for an unchanged retry and also uses an in-flight guard against double-click duplicate requests.

## UI / UX

The production screen uses the same production design system as the warehouse structure/map/calendar screens:

- warehouse-scoped metrics and filters;
- appointment operation table with state-specific actions;
- dock and yard master tables;
- permission-gated forms/actions;
- gate check-in form;
- dock assignment/reassignment form;
- exception capture;
- immutable operational timeline;
- Vietnamese user-facing recovery messages.

The Vercel Blueprint runtime exposes the **production route** against a read-only mock API. It never pretends that POST/PUT succeeds; demo mutations fail closed with HTTP 405.

## Verification targets

Source and automated regression coverage must prove:

- appointment after cutoff cannot be confirmed;
- vehicle mismatch cannot check in;
- overlapping active appointment cannot claim the same dock;
- gate/dock operations do not change inventory;
- read-only demo serves Dock/Yard data and rejects writes;
- Blueprint registry points WH-06 to `/dock-yard`, not a mock production substitute.
