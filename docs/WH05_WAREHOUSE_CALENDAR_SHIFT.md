# WH-05 — Warehouse Calendar & Shift

Status: **LIVE / PRODUCTION**

Production route: `/warehouse-calendar`

Source contract: Spec 60 — Warehouse Calendar, Shift Capacity & Operational Planning.

## Scope

WH-05 owns the recurring operational baseline for each warehouse:

- warehouse timezone;
- seven-day working calendar;
- opening and closing hours, including overnight windows;
- inbound and outbound operational cutoff;
- shift start/end and break policy;
- planned headcount;
- planning capacity dimensions: inbound pallets/hour, outbound orders/hour, dock slots, labor hours, staging capacity, packing stations and equipment availability.

Holiday, maintenance, emergency and other dated overrides are intentionally not mixed into this aggregate. Those belong to WH-07 Operational Calendar Exceptions.

## API

Read:

`GET /api/warehouses/{warehouseId}/calendar`

- requires `warehouse.read`;
- applies warehouse scope;
- returns local warehouse time, current open/closed state, current active shift and optimistic-concurrency tokens.

Calendar mutation:

`PUT /api/warehouses/{warehouseId}/calendar`

- requires `warehouse_calendar.manage`;
- applies warehouse scope;
- requires exactly seven weekday rows;
- validates timezone, overnight hours and cutoff placement;
- uses rowversion after initial configuration;
- writes immutable audit evidence.

Shift mutation:

`POST /api/warehouses/{warehouseId}/calendar/shifts`

`PUT /api/warehouses/{warehouseId}/calendar/shifts/{shiftId}`

- requires `warehouse_calendar.manage`;
- shift code is immutable after creation;
- cross-midnight shifts are first-class;
- break duration must remain inside the shift;
- capacity dimensions are non-negative;
- update uses rowversion;
- deactivation preserves history.

## Time semantics

Opening hours, cutoff and shift clock values are local warehouse wall-clock values. Business state is evaluated with the configured warehouse timezone. Audit timestamps stay UTC.

An overnight opening window is attributed to the day on which it starts. For example, Monday 22:00–06:00 remains open until Tuesday 06:00.

## UI / UX

The production screen follows the shared production design system:

- warehouse selector with visible label;
- operational status metrics before configuration detail;
- weekly schedule table;
- shift/capacity table;
- permission-gated mutation controls;
- inline Vietnamese validation and recovery messaging;
- responsive forms with no hidden hover-only controls.

The Vercel Blueprint runtime exposes the production screen against read-only mock API data. Blueprint capability simulation remains separate from the production route.
