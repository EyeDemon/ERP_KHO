# WH-02 — Warehouse Location Hierarchy Blueprint Mock

Status: **PLANNED / FRONTEND MOCK ONLY**

## Scope

This branch uses WH-02 only as an interactive frontend Blueprint. It is illustrative, read-only and intended to validate information architecture, hierarchy semantics, lifecycle states and UI/UX before production implementation.

The WH-02 production backend/database is **not implemented by this branch**.

No production claim is made for:
- EF entities/configuration;
- database migration or schema;
- API/controller/service;
- backend permission seed;
- mutation persistence;
- production QA.

## Target hierarchy

Future production design direction:

`Warehouse → WarehouseZone → WarehouseAisle → WarehouseRack → WarehouseRackLevel → WarehouseLocation`

Design notes:
- keep current `int` PK/FK types for WH-02; any bigint widening is separate cross-cutting work;
- keep `WarehouseLocation` as the canonical Bin/Location leaf;
- existing system/legacy/unmapped Locations may have nullable hierarchy links;
- new user-created Locations should belong to a Zone;
- capacity, weight and volume belong to WH-03.

These are design decisions for future production implementation, not evidence that the schema exists today.

## Frontend mock behavior

Blueprint workbench route: `/system-blueprint/warehouse-structure/WH-02/workbench`

There is intentionally **no production-style `/warehouse-structure` route** while WH-02 remains planned. The Blueprint mock is never exposed as a real-system destination.

The page reads fixtures directly from `frontend/src/mocks/demoApiData.ts` and does not depend on WH-02 production APIs.

The mock demonstrates:
- Warehouse → Zone → Aisle → Rack → Level → Bin / Location;
- full physical paths;
- normal storage and reserve non-pickable storage;
- blocked and inactive Locations;
- Damaged and Rejected non-pickable Locations;
- system-managed RECEIVING and LEGACY;
- legacy/non-system unmapped Location without invented historical hierarchy;
- local warehouse/status/type filters;
- local detail inspection.

There are no create/update persistence controls and no WH-02 POST/PUT/PATCH calls.

## Permission mock

Frontend-only permission definitions remain available to illustrate the target authorization matrix:
- `location.read`
- `location.manage`
- `warehouse_zone.manage`

These permissions are illustrative target semantics only. The WH-02 workbench is Blueprint-only and is intentionally excluded from production navigation; it does not imply production backend permission seeds exist.

## Demo adapter

WH-02 pseudo-backend handlers are intentionally absent. Unsupported writes remain fail-closed with HTTP 405 under the global Blueprint demo rule.

## UI/UX

WH-02 follows the repository's UI UX Pro Max master direction:
- shared `ProductionUi` primitives;
- flat/minimal operational styling;
- semantic status badges with text;
- accessible labels and keyboard-operable controls;
- responsive table containers;
- no page-level horizontal overflow.

## Production promotion

WH-02 must remain `planned` until a separate production implementation is intentionally designed, implemented and verified with its own backend/database/API/permission/QA evidence. A Blueprint `mockRoute` is not production evidence, and production `route` values must never point into `/system-blueprint`.


## Production implementation — 2026-10-04

WH-02 now has a separate production route: `/warehouse-structure`.

Production implementation uses the existing real `WarehouseLocations` aggregate and APIs under `/api/putaway-tasks/locations`, with warehouse-scope authorization and optimistic concurrency. A nullable `StructurePath` stores the physical hierarchy in canonical `ZONE/AISLE/RACK/LEVEL/BIN` form.

Safety rules:

- existing locations are not auto-mapped; null `StructurePath` remains visible as unmapped legacy data;
- `Code` stays the stable location identifier;
- once a `StructurePath` is assigned it is immutable;
- an unmapped location can only receive a structure path when it has no non-zero stock, no movement history and no active putaway source usage;
- system-managed RECEIVING/LEGACY locations cannot be assigned into the user-managed physical hierarchy;
- deactivation keeps the existing stock/task safety checks;
- production write controls remain permission-gated by `location.manage`.

The Blueprint workbench remains under `mockRoute` and is still demo-only. The production `route` is `/warehouse-structure`.
