# WH-02 — Warehouse Structure Hierarchy

Status: **FOUNDATION**. Promotion evidence is the final PR CI batch plus Vercel runtime verification described in the Definition of Done below.

## Canonical Notion references reviewed

Implementation was checked against the ERP WMS Notion workspace before code was written:

- **02 — Database Specification & DBML**: `warehouse_zones → aisles → racks → rack_levels → locations`.
- **03 — REST API & Backend Contract**: `GET /warehouses/{id}/zones`, `GET /warehouses/{id}/locations`, canonical `/locations` read/create/detail/PATCH and exact barcode resolver.
- **16 — Data Dictionary & Table Catalog**: physical hierarchy, warehouse/location invariant and master-data ownership.
- **17 — Permission Registry & Authorization Matrix**: `warehouse_zone.manage`, `location.read`, `location.manage`; `location_capacity.manage` belongs to WH-03.
- **67 — Warehouse Map, Heatmap & Spatial Visualization**: canonical spatial hierarchy `Warehouse → Zone → Aisle → Rack → Level → Bin`; map/heatmap itself remains WH-04.
- **84 — Master Data Governance & Stewardship** (2026-09-24 canonical Location governance): Location code is stable, case-insensitively unique inside a Warehouse; system-managed RECEIVING/LEGACY locations retain historical compatibility; inactive/blocked locations are preserved.
- **216 — UX Information Architecture**: `Kho & Vị trí → Zone / Aisle / Rack / Level / Bin`.
- **282 — UX Governance / Screen Matrix Handoff**: permission-driven UI, Vietnamese user-visible copy, safe stale-data handling and traceability from requirement → screen → API → permission → QA.

## Supersession decision

The older DBML models `location_types` as a separate master and contains broader capacity fields. The later 2026-09-24 Location governance used by the existing Putaway implementation defines the currently required Location attributes and explicitly defers weight/volume capacity and advanced slotting.

WH-02 therefore:

- keeps the existing `WarehouseLocation` entity as the canonical Bin/Location leaf already referenced by Inventory, Stocktake and Putaway;
- adds the physical parent hierarchy `WarehouseZone → WarehouseAisle → WarehouseRack → WarehouseRackLevel`;
- attaches `WarehouseLocation.ZoneId` and optional `RackLevelId`;
- does **not** implement the WH-03 capacity/constraint engine or WH-04 map/heatmap.

This avoids replacing historical Location IDs or inventing movement history.

## Domain and database invariants

- Zone code is uppercase and unique per Warehouse, case-insensitively.
- Aisle code is uppercase and unique per Zone.
- Rack code is uppercase and unique per Aisle.
- Rack Level number is positive and unique per Rack.
- Location code remains immutable after creation and unique per Warehouse.
- A Location with `RackLevelId` must also have `ZoneId`.
- Application validation additionally verifies the selected Rack Level belongs to the selected Zone.
- A Zone cannot be deactivated while it has active Locations.
- Zone type cannot be changed after Locations reference the Zone; create a new Zone instead.
- System-managed RECEIVING/LEGACY Locations are not forced into invented physical hierarchy.
- Existing non-system Locations without Zone remain visible as **unmapped locations** and can be mapped once; after a Zone/RackLevel is assigned, the physical parent cannot be silently changed.
- Damaged/Rejected Locations are non-pickable.
- STORAGE `IsPickable` is independent; reserve storage may therefore exist without being a Putaway destination under the current simplified destination selector.
- Master mutations and their audit records commit in the same transaction.
- RowVersion mutation metadata is exposed according to effective permission grants, not role-name checks.

## API surface

Read:

- `GET /api/warehouses/{warehouseId}/structure`
- `GET /api/warehouses/{warehouseId}/zones`
- `GET /api/warehouses/{warehouseId}/locations`
- `GET /api/locations?warehouseId={warehouseId}`
- `GET /api/locations/{id}`
- `GET /api/locations/barcode/{barcode}?warehouseId={warehouseId?}`

Mutations:

- Zone/Aisle/Rack/Level create/update routes under `/api/warehouses/{warehouseId}`
- `POST /api/locations`
- `PATCH /api/locations/{id}`

The pre-existing Putaway location endpoints remain for compatibility.

## Permission model

- `location.read`: read hierarchy and Locations.
- `warehouse_zone.manage`: mutate Zone/Aisle/Rack/Level.
- `location.manage`: create/update/map Locations.
- Migration grants `warehouse_zone.manage` to seeded Admin/Manager roles, matching Notion 17.
- WH-02 does not grant or consume `location_capacity.manage`.

## Production UI

Route: `/warehouse-structure`

The work center contains:

- Warehouse selector.
- Hierarchy metrics.
- Zone/Aisle/Rack/Level/Bin table with full physical path.
- Separate system Location table.
- Separate unmapped legacy Location table.
- Permission-gated structure and Location forms.
- Immutable code fields on edit.
- Vietnamese loading/empty/error/mutation feedback.
- Responsive layout using the existing ERP production design tokens/components.

The Vercel blueprint demo maps the read endpoints only; all mutations remain fail-closed with HTTP 405.

## QA coverage added

Backend:

- parent-scoped uniqueness and normalization;
- cross-warehouse parent rejection;
- Zone deactivation guard;
- stale RowVersion rejection;
- permission metadata for new controllers;
- Putaway regression for hierarchy-aware Location master.

Frontend:

- Viewer/read-only hierarchy rendering;
- separate `warehouse_zone.manage` and `location.manage` surfaces;
- canonical warehouse-scoped Zone create endpoint;
- demo adapter hierarchy/location reads;
- production navigation gated by `location.read`.

## Known cross-cutting schema deviation

Notion's general database standard recommends `bigint` PKs, while the existing ERP_KHO domain uses `int` for Warehouse, Location and related operational FKs. WH-02 preserves the repository's existing key type to avoid a partial key-width migration that would make related tables inconsistent.

This is a **documented cross-cutting database-standard gap**, not a silent WH-02 exception. Any future PK widening must be planned across Warehouse/Location and all referencing inventory/document/task tables as one migration program.

## Definition of done for WH-02 foundation

WH-02 can be promoted from Foundation only after:

1. final GitHub PR CI is green, including SQL migration/integration, API tests, frontend lint/tests/build;
2. Vercel deployment for the final HEAD is READY;
3. `/warehouse-structure` opens successfully in the production UI demo;
4. the System Blueprint route for WH-02 links to `/warehouse-structure`;
5. no runtime/browser error is observed on the checked hierarchy view;
6. Notion/Screen Matrix status is updated only to the level supported by that evidence.
