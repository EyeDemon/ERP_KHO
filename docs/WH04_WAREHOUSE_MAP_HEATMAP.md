# WH-04 — Warehouse Map & Heatmap

Status: **LIVE / PRODUCTION**

Production route: `/warehouse-map`

## Layout model

Warehouse map coordinates are stored on `WarehouseLocation` as percentages:

- `MapX`
- `MapY`
- `MapWidth`
- `MapHeight`

The four values are either all null or all populated. A configured rectangle must stay inside the 0–100% canvas.

The system never infers physical coordinates from Zone/Aisle/Rack/Level/Bin codes. Locations without explicit layout remain visible in the **Unmapped locations** list.

Layout changes use:

`PUT /api/putaway-tasks/locations/{locationId}/layout`

The mutation:

- requires `location.manage`;
- uses the WarehouseLocation rowversion contract;
- is warehouse-scoped;
- rejects system-managed RECEIVING/LEGACY locations;
- writes `WarehouseLocation.LayoutUpdated` audit evidence.

## Operational map snapshot

`GET /api/putaway-tasks/location-map?warehouseId=...`

The response includes `GeneratedAtUtc` and per-location:

- explicit layout;
- Storage Class;
- utilization percentage derived from WH-03 capacity dimensions;
- capacity state;
- count of inventory location movements in the last 60 minutes;
- count of active putaway items using the location as source;
- operational activity level.

Activity classification is intentionally simple and explainable:

- **High**: at least 10 recent movements or 5 active putaway items;
- **Medium**: at least 4 recent movements or 2 active putaway items;
- **Low**: below those thresholds.

## Heatmap

Production UI supports two layers:

1. **Utilization / Capacity**
   - capacity conflicts and over-capacity are critical;
   - 90%+ utilization is critical;
   - 75–89.99% utilization is warning;
   - lower utilization is normal.

2. **Operational Activity**
   - uses the High / Medium / Low activity classification above.

The page exposes snapshot age. If the browser observes a snapshot older than five minutes it marks the data stale and asks the operator to refresh before using it for coordination.

## Compatibility

All map fields are nullable. Existing locations migrate with no fabricated layout.

WH-04 reuses:

- WH-02 location hierarchy and authorization;
- WH-03 capacity projection;
- immutable `InventoryLocationMovement` history;
- active Putaway tasks.

The Blueprint WH-04 screen remains a preview only and is not a production data source.
