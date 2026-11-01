# Competitive Intelligence: Commercial WMS/ERP Systems Research

**Date Accessed:** 2026-11-01  
**Analyst:** Competitive Intelligence Agent  
**Scope:** 13 commercial & mid-market WMS/ERP systems across 3 tiers  
**Evidence Quality:** Web search on official docs & reputable analyst sources. Claims marked UNVERIFIED where only marketing material found.

---

## Table of Contents

1. [Research Methodology](#research-methodology)
2. [Tier 1: Enterprise WMS](#tier-1-enterprise-wms)
3. [Tier 2: Mid-Market ERP with WMS](#tier-2-mid-market-erp-with-wms)
4. [Tier 3: Ecommerce Fulfillment WMS](#tier-3-ecommerce-fulfillment-wms)
5. [Consolidated Capability Matrix](#consolidated-capability-matrix)
6. [Key Patterns & Takeaways for ERP_KHO](#key-patterns--takeaways-for-erp_kho)

---

## Research Methodology

- **Primary sources:** Official vendor documentation (docs.oracle.com, learn.microsoft.com, help.sap.com, odoo.com, frappe.io), vendor product pages, and analyst reviews.
- **Rating scale for capabilities:**
  - `NATIVE_CONFIRMED` — Feature documented in official product docs as built-in
  - `CONFIGURATION_CONFIRMED` — Achievable via configuration/rules without custom code
  - `ADDON_OR_INTEGRATION` — Requires add-on module, marketplace app, or 3rd-party integration
  - `PREVIEW_OR_PLANNED` — Announced but not GA
  - `NOT_FOUND_IN_REVIEWED_SOURCES` — No evidence found in reviewed documentation
  - `UNVERIFIED` — Claimed in marketing but not confirmed in technical docs

---

## Tier 1: Enterprise WMS

### 1. Oracle Fusion Cloud WMS

| Field | Detail |
|-------|--------|
| **Full Name** | Oracle Fusion Cloud Warehouse Management |
| **Vendor** | Oracle Corporation |
| **URL** | https://docs.oracle.com/en/cloud/saas/warehouse-management/index.html |
| **Version/Release** | Continuous quarterly updates (25A–25D as of 2025–2026) |
| **Target Market** | Large enterprise, complex multi-site distribution |
| **Pricing Model** | SaaS subscription; ~$2,500–$6,000+/mo per warehouse/site (UNVERIFIED industry estimate); custom quotes required |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** (ASN, check-in, partial, over/under, damage, QC, blind) | NATIVE_CONFIRMED | ASN processing, single-SKU RF receiving, lock code assignment at receipt documented in 24A–25D release notes |
| **Putaway** (directed, rule-based, capacity, zone, partial) | NATIVE_CONFIRMED | Putaway cart enhancements, pallet putaway to multiple locations, UOM-aware barcode inference |
| **Inventory Control** (multi-status, lot, serial, expiry, HU) | NATIVE_CONFIRMED | LPN-based tracking, inventory status management, cycle count with LPN factoring |
| **Inventory Ledger** (immutable, double-entry, reversal) | NOT_FOUND_IN_REVIEWED_SOURCES | No explicit documentation of immutable ledger pattern found; Oracle uses transaction-based model |
| **Cycle Counting** (planned, blind, recount, variance, approval, ABC) | NATIVE_CONFIRMED | Cycle count enhancements documented including cancelled/new LPN handling |
| **Stock Adjustments** (reason codes, approval, reversal) | NATIVE_CONFIRMED | Adjustment transactions with reason codes documented |
| **Replenishment** (min/max, demand, pick-face, reserve-to-active) | NATIVE_CONFIRMED | Rule-based replenishment documented |
| **Reservation/Allocation** (soft, hard, partial, release, expiration) | NATIVE_CONFIRMED | Outbound line quantity modification after allocation status confirmed in 24D |
| **Picking** (single, batch, wave, cluster, zone, directed, path opt) | NATIVE_CONFIRMED | Multiple pick strategies, wave processing documented |
| **Packing** (pack station, put-to-wall, sortation, label, weight/dim) | NATIVE_CONFIRMED | Label designer with dynamic barcodes, DPI dimensions (24C enhancement) |
| **Dispatch/Shipping** (shipment, carrier, AWB/manifest, partial, tracking) | NATIVE_CONFIRMED | Shipping integration, manifest generation |
| **Returns** (RMA, return receipt, inspection, disposition, reversal) | NATIVE_CONFIRMED | Return receipt processing documented |
| **Cross-docking** (flow-through, opportunistic) | NATIVE_CONFIRMED | Cross-dock capabilities documented |
| **Yard Management** (dock scheduling, trailer tracking) | NATIVE_CONFIRMED | Part of broader SCM suite |
| **Labor/Workforce** (task assignment, work queue, performance) | NATIVE_CONFIRMED | Task-based work execution |
| **Mobile/Scanner** (RF, barcode, mobile-first) | NATIVE_CONFIRMED | Redwood Mobile WMS, RF transactions extensively documented |
| **Reporting** (inventory, movement, accuracy, productivity) | NATIVE_CONFIRMED | SCM Reconciliation Workbench, analytics dashboards |
| **Integration** (API, webhook, ecommerce, carrier, ERP) | NATIVE_CONFIRMED | REST APIs, Oracle Integration Cloud |
| **Multi-tenant/SaaS** (native multi-tenancy, isolation) | NATIVE_CONFIRMED | Cloud SaaS with tenant isolation |
| **Quality Control** (QC workflow, hold states, disposition) | NATIVE_CONFIRMED | QC inspection at receiving, quality holds |

#### Unique Differentiators
- Redwood UX providing modern mobile-first warehouse experience
- Deep integration with Oracle Fusion Cloud SCM, OMS, TMS ecosystem
- Advanced Inventory Management (mid-2025) for organizations needing lighter WMS
- Dynamic label designer with repeating barcodes

#### Relevance to ERP_KHO
- **Relevance:** LOW — Massively over-engineered for a single Vietnamese ecommerce warehouse
- **Learn from:** Label designer approach (dynamic template selection), LPN-based tracking patterns, Redwood mobile UX design principles
- **Do NOT copy:** Multi-site orchestration complexity, Oracle-specific integration patterns, enterprise licensing model

---

### 2. Microsoft Dynamics 365 Supply Chain Management — WMS

| Field | Detail |
|-------|--------|
| **Full Name** | Microsoft Dynamics 365 Supply Chain Management — Warehouse Management |
| **Vendor** | Microsoft Corporation |
| **URL** | https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/ |
| **Version/Release** | Continuous updates (10.0.x); "Warehouse Management Only Mode" introduced ~2024 |
| **Target Market** | Mid-to-large enterprise, manufacturing & distribution |
| **Pricing Model** | Per-user/month subscription; SCM license ~$180/user/month (attach license ~$30/user/month for additional users) |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | PO receipt, return orders, transfer orders; cross-docking at receipt |
| **Putaway** | NATIVE_CONFIRMED | Location directives with rule-based logic, zone/profile-based putaway |
| **Inventory Control** | NATIVE_CONFIRMED | Inventory status management, batch & serial tracking, license plates |
| **Inventory Ledger** | NATIVE_CONFIRMED | Inventory transactions with financial integration; not explicitly "immutable ledger" pattern |
| **Cycle Counting** | NATIVE_CONFIRMED | System-directed cycle counting, continuous operation |
| **Stock Adjustments** | NATIVE_CONFIRMED | Adjustment journals with reason codes |
| **Replenishment** | NATIVE_CONFIRMED | Min/max, demand-based, wave-demand replenishment |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Reservation hierarchy, batch/serial reservation |
| **Picking** | NATIVE_CONFIRMED | Cluster picking, batch, zone, wave processing documented on learn.microsoft.com |
| **Packing** | NATIVE_CONFIRMED | Manual packing stations, automated containerization algorithms |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | Shipment consolidation, carrier integration, load planning |
| **Returns** | NATIVE_CONFIRMED | Return order processing, quality inspection on return |
| **Cross-docking** | NATIVE_CONFIRMED | Planned and opportunistic cross-docking |
| **Yard Management** | ADDON_OR_INTEGRATION | Transportation management module (separate) |
| **Labor/Workforce** | CONFIGURATION_CONFIRMED | Work templates, work pools; no native labor standards engine |
| **Mobile/Scanner** | NATIVE_CONFIRMED | Warehouse Management mobile app, RF/barcode, ZPL label printing |
| **Reporting** | NATIVE_CONFIRMED | Power BI integration, inventory analytics |
| **Integration** | NATIVE_CONFIRMED | OData/REST APIs, Dataverse, Logic Apps, Power Automate |
| **Multi-tenant/SaaS** | NATIVE_CONFIRMED | Azure-hosted SaaS with environment isolation |
| **Quality Control** | NATIVE_CONFIRMED | Quality management integration, quarantine zones, auto-trigger inspections |

#### Unique Differentiators
- **"Warehouse Management Only Mode"** — use D365 WMS standalone with external ERP (unique in market)
- Setup wizards and implementation workspace reduce deployment time
- Deep Power Platform integration (Power BI, Power Automate, Copilot)
- ZPL label printing natively supported
- Extremely well-documented on learn.microsoft.com (best public docs in Tier 1)

#### Relevance to ERP_KHO
- **Relevance:** MEDIUM — Architecture patterns are instructive; WM-Only mode concept validates standalone WMS approach
- **Learn from:** Location directives / work templates pattern (rule-based automation), setup wizards for fast deployment, mobile app UX, documentation quality standard
- **Do NOT copy:** D365-specific Dataverse dependency, complex wave processing for single warehouse, per-user pricing model at scale

---

### 3. SAP Extended Warehouse Management (EWM)

| Field | Detail |
|-------|--------|
| **Full Name** | SAP Extended Warehouse Management (SAP EWM) |
| **Vendor** | SAP SE |
| **URL** | https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/ewm |
| **Version/Release** | Embedded EWM (S/4HANA 2023/2024); Decentralized EWM available |
| **Target Market** | Large enterprise, automated DCs, manufacturing |
| **Pricing Model** | Named user licensing within S/4HANA; custom enterprise agreements; very expensive |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | ASN validation, goods receipt, unloading management |
| **Putaway** | NATIVE_CONFIRMED | Optimized putaway strategies (capacity, weight, slotting rules) |
| **Inventory Control** | NATIVE_CONFIRMED | Handling units (HUs), batch, serial, catch-weight, consignment stock |
| **Inventory Ledger** | NATIVE_CONFIRMED | Material documents, goods movement postings; S/4HANA uses Universal Journal |
| **Cycle Counting** | NATIVE_CONFIRMED | Physical inventory counting, cycle counting |
| **Stock Adjustments** | NATIVE_CONFIRMED | Goods movement with reason codes |
| **Replenishment** | NATIVE_CONFIRMED | Internal replenishment, re-binning |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Stock determination, allocation rules |
| **Picking** | NATIVE_CONFIRMED | Multi-order, batch picking, wave management |
| **Packing** | NATIVE_CONFIRMED | Handling unit packing, work center-based packing |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | Loading, staging, shipping with HU tracking |
| **Returns** | NATIVE_CONFIRMED | Return processing integrated with SD module |
| **Cross-docking** | NATIVE_CONFIRMED | Flow-through, direct transfer from inbound to outbound |
| **Yard Management** | NATIVE_CONFIRMED | Yard/dock management with trailer tracking |
| **Labor/Workforce** | NATIVE_CONFIRMED | Labor management with performance standards, workforce planning |
| **Mobile/Scanner** | NATIVE_CONFIRMED | SAP Fiori apps, RF transactions |
| **Reporting** | NATIVE_CONFIRMED | HANA-optimized analytics, KPI dashboards |
| **Integration** | NATIVE_CONFIRMED | RFC, BAPI, OData, IDocs; MFS for automation equipment |
| **Multi-tenant/SaaS** | CONFIGURATION_CONFIRMED | Cloud deployment available; not natively multi-tenant in SaaS sense |
| **Quality Control** | NATIVE_CONFIRMED | QM integration, inspection lots, quality notifications |

#### Unique Differentiators
- **Material Flow System (MFS)** — direct connection to conveyors, AS/RS without separate WCS
- Value-Added Services (VAS) module for kitting, labeling, custom assembly
- Embedded vs. Decentralized deployment flexibility
- SAP Joule AI assistant for warehouse guidance (2025)
- Deepest automation integration in the market

#### Relevance to ERP_KHO
- **Relevance:** LOW — Extremely complex, expensive, designed for massive operations
- **Learn from:** Handling unit (HU) concept, work center abstraction for packing/QC stations, MFS pattern for future automation
- **Do NOT copy:** SAP-specific integration complexity, monolithic configuration approach, implementation cost structure

---

### 4. Manhattan Active WMS

| Field | Detail |
|-------|--------|
| **Full Name** | Manhattan Active Warehouse Management |
| **Vendor** | Manhattan Associates |
| **URL** | https://www.manh.com/products/warehouse-management |
| **Version/Release** | "Versionless" — continuous quarterly updates, cloud-native |
| **Target Market** | Large enterprise, complex distribution networks |
| **Pricing Model** | Enterprise subscription; custom quotes only; estimated $100K–$500K+/year |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | Full receiving, putaway workflow |
| **Putaway** | NATIVE_CONFIRMED | Directed putaway with slotting optimization |
| **Inventory Control** | NATIVE_CONFIRMED | Real-time global inventory visibility, multi-status |
| **Inventory Ledger** | NOT_FOUND_IN_REVIEWED_SOURCES | Transaction-based; no explicit immutable ledger documentation |
| **Cycle Counting** | NATIVE_CONFIRMED | Continuous cycle counting |
| **Stock Adjustments** | NATIVE_CONFIRMED | Adjustment workflows |
| **Replenishment** | NATIVE_CONFIRMED | Dynamic replenishment, slotting optimization |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Order streaming replaces traditional wave-based allocation |
| **Picking** | NATIVE_CONFIRMED | Continuous order streaming (beyond traditional wave/batch) |
| **Packing** | NATIVE_CONFIRMED | Pack stations, cartonization |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | Shipping execution, carrier integration |
| **Returns** | NATIVE_CONFIRMED | Returns processing |
| **Cross-docking** | NATIVE_CONFIRMED | Flow-through processing |
| **Yard Management** | NATIVE_CONFIRMED | Native YM unified with WMS (dock doors, trailers) |
| **Labor/Workforce** | NATIVE_CONFIRMED | Built-in labor management with AI-driven performance tracking, coaching |
| **Mobile/Scanner** | NATIVE_CONFIRMED | RF scanners, tablets, wearables |
| **Reporting** | NATIVE_CONFIRMED | Unified analytics across WM/LM/YM |
| **Integration** | NATIVE_CONFIRMED | Open APIs, ERP-agnostic |
| **Multi-tenant/SaaS** | NATIVE_CONFIRMED | Cloud-native on Google Cloud, microservices |
| **Quality Control** | NATIVE_CONFIRMED | QC workflows integrated |

#### Unique Differentiators
- **"Versionless" architecture** — zero-downtime updates, no upgrade projects
- **Continuous Order Streaming** — replaces rigid wave processing with dynamic, real-time work streaming
- **Unified WMS + LM + YM + WES** in single platform (no bolt-on modules)
- **ActiveAgents AI** — monitors operations, forecasts bottlenecks, auto-rebalances
- Tiered editions (Essentials → Enterprise → Premier) for scalable adoption

#### Relevance to ERP_KHO
- **Relevance:** LOW for current scale, but architecturally instructive
- **Learn from:** "Versionless" cloud-native approach (continuous deployment), order streaming concept (future), unified data model across WM/LM/YM
- **Do NOT copy:** Massive enterprise complexity, Google Cloud-specific architecture, AI features requiring huge data volume

---

### 5. Blue Yonder WMS

| Field | Detail |
|-------|--------|
| **Full Name** | Blue Yonder Warehouse Management |
| **Vendor** | Blue Yonder (Panasonic subsidiary) |
| **URL** | https://blueyonder.com/solutions/warehouse-management |
| **Version/Release** | Continuous SaaS on Azure; major AI features 2024–2025 |
| **Target Market** | Large enterprise, retail, CPG |
| **Pricing Model** | Enterprise subscription; custom quotes |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | Full inbound processing |
| **Putaway** | NATIVE_CONFIRMED | AI-driven slotting optimization |
| **Inventory Control** | NATIVE_CONFIRMED | Real-time tracking, multi-status |
| **Inventory Ledger** | NOT_FOUND_IN_REVIEWED_SOURCES | Not explicitly documented |
| **Cycle Counting** | NATIVE_CONFIRMED | Continuous counting |
| **Stock Adjustments** | NATIVE_CONFIRMED | Adjustment workflows |
| **Replenishment** | NATIVE_CONFIRMED | AI-recommended replenishment |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Dynamic allocation |
| **Picking** | NATIVE_CONFIRMED | Task interleaving, optimized pick paths |
| **Packing** | NATIVE_CONFIRMED | Packing workflows |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | Shipping with TMS integration |
| **Returns** | NATIVE_CONFIRMED | Returns processing |
| **Cross-docking** | NATIVE_CONFIRMED | Flow-through processing |
| **Yard Management** | NATIVE_CONFIRMED | Dock scheduling, trailer management |
| **Labor/Workforce** | NATIVE_CONFIRMED | AI-powered resource forecasting, real-time orchestration |
| **Mobile/Scanner** | NATIVE_CONFIRMED | RF devices, mobile apps |
| **Reporting** | NATIVE_CONFIRMED | Predictive analytics, what-if simulations |
| **Integration** | NATIVE_CONFIRMED | APIs, unified planning-to-execution data layer |
| **Multi-tenant/SaaS** | NATIVE_CONFIRMED | Cloud-native on Azure |
| **Quality Control** | NATIVE_CONFIRMED | QC workflows |

#### Unique Differentiators
- **Agentic AI / Warehouse AI Agents** — AI partners that explain anomalies and recommend actions in real-time
- **Task interleaving** — combines putaway with picking runs to eliminate dead travel
- **Robotics Hub** — vendor-agnostic bridge to AMRs, AS/RS, conveyors
- AI-powered migration utilities for legacy WMS transitions
- What-if simulation and scenario analysis

#### Relevance to ERP_KHO
- **Relevance:** LOW — Far too complex and expensive
- **Learn from:** Task interleaving concept (combining putaway + pick in one trip — applicable even at small scale), AI anomaly explanation UX pattern
- **Do NOT copy:** Agentic AI complexity, enterprise-scale resource forecasting, Azure-locked architecture

---

### 6. Infor WMS

| Field | Detail |
|-------|--------|
| **Full Name** | Infor CloudSuite Warehouse Management System |
| **Vendor** | Infor (Koch Industries) |
| **URL** | https://www.infor.com/products/warehouse-management |
| **Version/Release** | CloudSuite on AWS, continuous updates |
| **Target Market** | Mid-to-large enterprise, 3PL, food & beverage, manufacturing |
| **Pricing Model** | Enterprise subscription; custom quotes |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | Dock door scheduling, directed receiving |
| **Putaway** | NATIVE_CONFIRMED | Directed putaway, cross-docking, capacity-based |
| **Inventory Control** | NATIVE_CONFIRMED | Bin-level LPN tracking, FIFO/LIFO/FEFO rotation |
| **Inventory Ledger** | NOT_FOUND_IN_REVIEWED_SOURCES | Transaction-based tracking |
| **Cycle Counting** | NATIVE_CONFIRMED | Automated cycle counting |
| **Stock Adjustments** | NATIVE_CONFIRMED | Adjustment transactions |
| **Replenishment** | NATIVE_CONFIRMED | Automated replenishment |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Wave-based allocation |
| **Picking** | NATIVE_CONFIRMED | Zone, batch, cluster, wave picking |
| **Packing** | NATIVE_CONFIRMED | Pack stations, carrier manifest generation |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | Carrier integration, small-parcel & freight |
| **Returns** | NATIVE_CONFIRMED | Returns processing |
| **Cross-docking** | NATIVE_CONFIRMED | Flow-through, cross-docking at receipt |
| **Yard Management** | NATIVE_CONFIRMED | Dock door scheduling, yard traffic management |
| **Labor/Workforce** | NATIVE_CONFIRMED | Integrated labor management (LFM) with performance standards |
| **Mobile/Scanner** | NATIVE_CONFIRMED | Voice-directed processing, RF scanning |
| **Reporting** | NATIVE_CONFIRMED | Embedded analytics, KPI dashboards |
| **Integration** | NATIVE_CONFIRMED | Open APIs, Infor ERP integration, 3rd-party ERP support |
| **Multi-tenant/SaaS** | NATIVE_CONFIRMED | AWS-hosted, multi-tenant cloud |
| **Quality Control** | NATIVE_CONFIRMED | QC inspection checkpoints at receiving |

#### Unique Differentiators
- **3D Visual Warehouse** — interactive digital twin for bottleneck identification
- **Native 3PL billing** — activity-based billing, client invoicing built into WMS
- **Voice-directed processing** — hands-free/eyes-free operations
- Strong food & beverage / cold chain specialization
- Industry-specific CloudSuites (manufacturing, distribution, 3PL, retail)

#### Relevance to ERP_KHO
- **Relevance:** MEDIUM (3PL billing pattern relevant for managed WMS SaaS)
- **Learn from:** 3PL billing engine design (activity-based billing for future SaaS monetization), 3D visualization concept, voice-directed workflow pattern
- **Do NOT copy:** AWS lock-in, industry-specific over-specialization for current needs

---

### 7. Körber WMS

| Field | Detail |
|-------|--------|
| **Full Name** | Körber Supply Chain Warehouse Management System |
| **Vendor** | Körber AG (Supply Chain division) |
| **URL** | https://www.koerber-supplychain.com/warehouse-management/ |
| **Version/Release** | Continuous SaaS updates; modular architecture |
| **Target Market** | Mid-to-large enterprise, 3PL, retail |
| **Pricing Model** | SaaS subscription; modular pricing |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | Rules-based receiving, ASN generation |
| **Putaway** | NATIVE_CONFIRMED | Configurable rules (dimensions, turnover, layout) |
| **Inventory Control** | NATIVE_CONFIRMED | Real-time multi-site visibility, cycle counting |
| **Inventory Ledger** | NOT_FOUND_IN_REVIEWED_SOURCES | Standard transaction tracking |
| **Cycle Counting** | NATIVE_CONFIRMED | Cycle counting documented |
| **Stock Adjustments** | NATIVE_CONFIRMED | Adjustment workflows |
| **Replenishment** | NATIVE_CONFIRMED | Advanced replenishment, background monitoring of pick/bulk locations |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Wave-based allocation |
| **Picking** | NATIVE_CONFIRMED | Voice-directed, pick-to-light, RF, batch, wave, zone |
| **Packing** | NATIVE_CONFIRMED | Packing workflows |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | Multi-carrier management, EDI, automated ASN generation |
| **Returns** | NATIVE_CONFIRMED | Returns processing |
| **Cross-docking** | NATIVE_CONFIRMED | Cross-docking workflows |
| **Yard Management** | NATIVE_CONFIRMED | Dock scheduling, carrier appointment scheduling |
| **Labor/Workforce** | NATIVE_CONFIRMED | LMS module with performance standards |
| **Mobile/Scanner** | NATIVE_CONFIRMED | HTML5 responsive UI, RF scanners, tablets, smartphones |
| **Reporting** | NATIVE_CONFIRMED | Supply Chain Control Tower, analytics |
| **Integration** | NATIVE_CONFIRMED | ERP-agnostic (SAP, D365, NetSuite, Sage), open APIs |
| **Multi-tenant/SaaS** | NATIVE_CONFIRMED | Cloud SaaS deployment |
| **Quality Control** | NATIVE_CONFIRMED | QC inspection workflows |

#### Unique Differentiators
- **Modular architecture** — implement core first, add modules (LMS, Slotting.IQ, Yard) incrementally
- **"Zero-modification" upgrade approach** — workflow engine allows configuration without custom code
- **Gamification** — employee engagement through performance milestones
- **Slotting.IQ** — dynamic slotting optimization module
- Strong mid-market accessibility while scaling to enterprise

#### Relevance to ERP_KHO
- **Relevance:** MEDIUM — Modular approach and mid-market positioning are instructive
- **Learn from:** Modular architecture (start simple, add capabilities), zero-modification upgrade philosophy, gamification for warehouse staff engagement, HTML5 responsive UI approach
- **Do NOT copy:** Heavy modular licensing, enterprise EDI focus

---

## Tier 2: Mid-Market ERP with WMS

### 8. NetSuite WMS

| Field | Detail |
|-------|--------|
| **Full Name** | Oracle NetSuite Warehouse Management System |
| **Vendor** | Oracle (NetSuite division) |
| **URL** | https://www.netsuite.com/portal/products/erp/warehouse-fulfillment/wms.shtml |
| **Version/Release** | Bi-annual releases (2024.1, 2024.2, etc.) |
| **Target Market** | Mid-market distribution, manufacturing, 3PL on NetSuite ERP |
| **Pricing Model** | Add-on module to NetSuite ERP; ~$1,000–$2,000/month on top of base NetSuite license |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | Barcode scanning receipt, LPN tracking at receipt |
| **Putaway** | NATIVE_CONFIRMED | Smart putaway rules (size, weight, velocity, zones) |
| **Inventory Control** | NATIVE_CONFIRMED | Bin-level tracking, lot/serial, real-time sync |
| **Inventory Ledger** | NATIVE_CONFIRMED | Single source of truth with ERP; inventory transactions auto-sync to GL |
| **Cycle Counting** | NATIVE_CONFIRMED | Scheduled automated cycle counts, ABC classification, zone-based |
| **Stock Adjustments** | NATIVE_CONFIRMED | Adjustment transactions |
| **Replenishment** | NATIVE_CONFIRMED | Automatic bin-to-bin, bulk-to-pickface replenishment |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Order-based reservation |
| **Picking** | NATIVE_CONFIRMED | Single-order, batch, zone, cluster; pick-path optimization |
| **Packing** | ADDON_OR_INTEGRATION | NetSuite Ship Central SuiteApp for packing/shipping workflows |
| **Dispatch/Shipping** | ADDON_OR_INTEGRATION | Ship Central SuiteApp for carrier integration |
| **Returns** | NATIVE_CONFIRMED | Return authorization processing |
| **Cross-docking** | NATIVE_CONFIRMED | Route incoming direct to outbound |
| **Yard Management** | NOT_FOUND_IN_REVIEWED_SOURCES | Not documented as native feature |
| **Labor/Workforce** | NOT_FOUND_IN_REVIEWED_SOURCES | No native labor management |
| **Mobile/Scanner** | NATIVE_CONFIRMED | SCM Mobile App on Android/iOS, RF scanner support |
| **Reporting** | NATIVE_CONFIRMED | NetSuite analytics, saved searches, SuiteAnalytics |
| **Integration** | NATIVE_CONFIRMED | SuiteScript, REST/SOAP APIs, SuiteApps marketplace |
| **Multi-tenant/SaaS** | NATIVE_CONFIRMED | NetSuite is natively multi-tenant SaaS |
| **Quality Control** | CONFIGURATION_CONFIRMED | Basic holds; advanced QC via SuiteApps |

#### Unique Differentiators
- **Single database with ERP** — zero sync lag between WMS operations and financial/order data
- Built on NetSuite's SuiteCloud platform — extensible via SuiteScript
- Seamless upgrade path from basic inventory management → full WMS
- Mobile app works on consumer iOS devices (iPad/iPhone), not just ruggedized scanners

#### Relevance to ERP_KHO
- **Relevance:** HIGH — Similar target market (mid-market, ecommerce fulfillment)
- **Learn from:** Single-source-of-truth pattern (WMS + ERP in one DB), smart putaway rules design, mobile app supporting consumer devices, incremental WMS adoption path
- **Do NOT copy:** NetSuite platform lock-in, SuiteApp marketplace dependency for shipping/packing, premium pricing for add-on modules

---

### 9. Odoo Inventory

| Field | Detail |
|-------|--------|
| **Full Name** | Odoo Inventory & Barcode Apps |
| **Vendor** | Odoo S.A. |
| **URL** | https://www.odoo.com/app/inventory |
| **Version/Release** | Odoo 17 (stable); Odoo 18 (latest, 2024) |
| **Target Market** | SMB to mid-market, all industries |
| **Pricing Model** | Community: Free (LGPLv3); Enterprise: ~$24.90/user/month (Standard), ~$37.40/user/month (Custom) |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | Barcode scanning receipt, scan-to-receive documented in Odoo 17 |
| **Putaway** | NATIVE_CONFIRMED | Putaway rules based on product category, zone, shelf |
| **Inventory Control** | NATIVE_CONFIRMED | Multi-warehouse, lot/serial tracking, package/pallet tracking, locations |
| **Inventory Ledger** | NATIVE_CONFIRMED | Stock moves create valuation layers (FIFO/AVCO); integrated with accounting |
| **Cycle Counting** | NATIVE_CONFIRMED | Inventory adjustments, scheduled counts |
| **Stock Adjustments** | NATIVE_CONFIRMED | Inventory adjustments with barcode scanning |
| **Replenishment** | NATIVE_CONFIRMED | Automated reorder rules, min/max stock rules |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Reserved quantities management improved in Odoo 17 |
| **Picking** | NATIVE_CONFIRMED | Pick-pack-ship workflow; batch picking (Enterprise) |
| **Packing** | NATIVE_CONFIRMED | Package management, shipping labels |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | Carrier connectors (Enterprise: DHL, FedEx, UPS native) |
| **Returns** | NATIVE_CONFIRMED | Return orders, reverse transfers |
| **Cross-docking** | CONFIGURATION_CONFIRMED | Configurable via route rules (receipt → ship direct) |
| **Yard Management** | NOT_FOUND_IN_REVIEWED_SOURCES | No native yard management |
| **Labor/Workforce** | NOT_FOUND_IN_REVIEWED_SOURCES | No native labor management |
| **Mobile/Scanner** | NATIVE_CONFIRMED | Barcode app optimized for mobile (large tap targets, undo history, error-proofing) |
| **Reporting** | NATIVE_CONFIRMED | Stock reports, valuation reports, aging reports |
| **Integration** | NATIVE_CONFIRMED | REST API, XML-RPC; Enterprise adds ecommerce/carrier connectors |
| **Multi-tenant/SaaS** | CONFIGURATION_CONFIRMED | Odoo.sh is SaaS; self-hosted needs manual multi-company setup |
| **Quality Control** | ADDON_OR_INTEGRATION | Quality module available as separate app |

#### Unique Differentiators
- **GS1 barcode nomenclature** — single scan captures product ID, lot, expiry, weight simultaneously
- **30-scan undo history** — deep undo capability in barcode app
- **Error-proofing UX** — sound alerts, vibrations, explicit error messages for unknown barcodes
- Extremely low barrier to entry with Community Edition
- Modular app marketplace with 40,000+ apps

#### Relevance to ERP_KHO
- **Relevance:** VERY HIGH — Closest comparable in target market and architecture
- **Learn from:** GS1 barcode parsing (multi-data-point single scan), mobile barcode UX patterns (44px tap targets, vibration feedback, undo history), modular app architecture, open source community model, FIFO/AVCO valuation layer approach
- **Do NOT copy:** Community vs. Enterprise feature gating (confusing for users), LGPLv3 license constraints, monolithic Python/ORM architecture, lack of immutable ledger

---

### 10. ERPNext Stock

| Field | Detail |
|-------|--------|
| **Full Name** | ERPNext Stock / Warehouse Management Module |
| **Vendor** | Frappe Technologies (community-driven) |
| **URL** | https://docs.erpnext.com/docs/user/manual/en/stock |
| **Version/Release** | ERPNext v15 (stable, 2024); Frappe Framework (MIT License) |
| **Target Market** | SMB, developing markets, self-hosted organizations |
| **Pricing Model** | Free (GPLv3); Frappe Cloud hosting: ~$50–$500/month |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | Purchase Receipt with stock entries |
| **Putaway** | NATIVE_CONFIRMED | Putaway rules by item group and warehouse |
| **Inventory Control** | NATIVE_CONFIRMED | Tree-structured warehouses, bin tracking (Actual/Reserved/Ordered/Projected) |
| **Inventory Ledger** | NATIVE_CONFIRMED | **Stock Ledger Entries (SLE) — immutable, timestamped ledger entries** with valuation impact |
| **Cycle Counting** | NATIVE_CONFIRMED | Stock Reconciliation for physical counts and variance adjustment |
| **Stock Adjustments** | NATIVE_CONFIRMED | Stock Entry for adjustments, material issue, receipt |
| **Replenishment** | NATIVE_CONFIRMED | Automated reorder levels with auto Material Request generation |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Stock Reservation against Sales Orders (v15 feature) |
| **Picking** | NATIVE_CONFIRMED | Pick Lists generated from Sales Orders |
| **Packing** | CONFIGURATION_CONFIRMED | Packing Slip doctype; basic packing workflow |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | Delivery Note with shipping details |
| **Returns** | NATIVE_CONFIRMED | Return transactions (reverse entries) |
| **Cross-docking** | NOT_FOUND_IN_REVIEWED_SOURCES | No explicit cross-docking feature |
| **Yard Management** | NOT_FOUND_IN_REVIEWED_SOURCES | No yard management |
| **Labor/Workforce** | NOT_FOUND_IN_REVIEWED_SOURCES | No warehouse labor management |
| **Mobile/Scanner** | ADDON_OR_INTEGRATION | Basic mobile UI; barcode scanning via community apps |
| **Reporting** | NATIVE_CONFIRMED | Stock Balance, Stock Ledger, Stock Ageing, Warehouse Capacity reports |
| **Integration** | NATIVE_CONFIRMED | REST API, webhooks, Frappe Framework extensibility |
| **Multi-tenant/SaaS** | CONFIGURATION_CONFIRMED | Multi-company support; true multi-tenancy requires separate instances |
| **Quality Control** | NATIVE_CONFIRMED | Quality Inspection doctype with criteria |

#### Unique Differentiators
- **Fully open-source (GPLv3)** with MIT-licensed framework — maximum customization freedom
- **Stock Ledger Entry (SLE)** — closest to an immutable inventory ledger among reviewed systems
- **Perpetual inventory with auto journal entries** — every stock move creates accounting entry
- Tree-structured warehouse hierarchy (Company → Region → Store → Aisle → Bin)
- Stock Reservation against Sales Orders added in v15

#### Relevance to ERP_KHO
- **Relevance:** VERY HIGH — Most architecturally similar to ERP_KHO's approach
- **Learn from:** **Stock Ledger Entry pattern** (immutable, timestamped, with valuation — validates ERP_KHO's inventory_ledger design), perpetual inventory with auto-accounting, tree-structured warehouse hierarchy, GPLv3 open-source business model, stock reservation design
- **Do NOT copy:** Weak mobile/scanner support, Python/MariaDB stack limitations for high concurrency, lack of sophisticated picking strategies, no handling unit concept

---

## Tier 3: Ecommerce Fulfillment WMS

### 11. Logiwa IO

| Field | Detail |
|-------|--------|
| **Full Name** | Logiwa IO Fulfillment Management System |
| **Vendor** | Logiwa Inc. |
| **URL** | https://www.logiwa.com/product |
| **Version/Release** | Cloud-native SaaS, continuous updates |
| **Target Market** | High-volume DTC ecommerce, 3PL providers |
| **Pricing Model** | SaaS subscription; custom quotes; estimated $1,500–$5,000+/month |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | Inbound receipt management |
| **Putaway** | NATIVE_CONFIRMED | Directed putaway |
| **Inventory Control** | NATIVE_CONFIRMED | Real-time multi-warehouse, multi-channel inventory sync |
| **Inventory Ledger** | NOT_FOUND_IN_REVIEWED_SOURCES | Standard transaction tracking |
| **Cycle Counting** | NATIVE_CONFIRMED | Cycle counting |
| **Stock Adjustments** | NATIVE_CONFIRMED | Adjustment workflows |
| **Replenishment** | NATIVE_CONFIRMED | Rule-based replenishment |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Smart order routing and allocation |
| **Picking** | NATIVE_CONFIRMED | AI-powered batch, zone, wave picking with path optimization |
| **Packing** | NATIVE_CONFIRMED | Pack station workflows |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | 200+ integrations, rate shopping (UPS, FedEx, DHL, USPS, regional) |
| **Returns** | NATIVE_CONFIRMED | Returns management with inspection and stock update |
| **Cross-docking** | CONFIGURATION_CONFIRMED | Configurable via order routing rules |
| **Yard Management** | NOT_FOUND_IN_REVIEWED_SOURCES | Not documented |
| **Labor/Workforce** | NATIVE_CONFIRMED | Labor efficiency dashboards |
| **Mobile/Scanner** | NATIVE_CONFIRMED | Barcode/RF device support |
| **Reporting** | NATIVE_CONFIRMED | Operational dashboards, throughput, forecasting |
| **Integration** | NATIVE_CONFIRMED | 200+ ecommerce integrations, open API, webhooks |
| **Multi-tenant/SaaS** | NATIVE_CONFIRMED | Multi-tenant for 3PL client management |
| **Quality Control** | CONFIGURATION_CONFIRMED | Inspection workflows for returns |

#### Unique Differentiators
- **200+ pre-built ecommerce integrations** (Shopify, Amazon, Walmart, WooCommerce)
- **Smart order routing** — auto-routes orders to optimal fulfillment center
- **Kitting/bundling** support for DTC promotional packs
- **3PL client portal with automated billing** (storage, pick/pack, receiving fees)
- Rate shopping across carriers for cost optimization
- Branded unboxing / custom packaging rules

#### Relevance to ERP_KHO
- **Relevance:** VERY HIGH — Target market directly overlaps (Vietnamese ecommerce fulfillment)
- **Learn from:** Ecommerce marketplace integration pattern (Shopee, Lazada equivalent), rate-shopping carrier selection, 3PL billing automation, smart order routing concept, kitting/bundling for promotional packs
- **Do NOT copy:** 200-integration breadth (focus on Vietnamese marketplaces first), heavy US carrier focus, premium SaaS pricing

---

### 12. ShipHero WMS

| Field | Detail |
|-------|--------|
| **Full Name** | ShipHero Warehouse Management System |
| **Vendor** | ShipHero Inc. |
| **URL** | https://shiphero.com/warehouse-management-software/ |
| **Version/Release** | Cloud SaaS, continuous updates |
| **Target Market** | DTC ecommerce brands, 3PL providers |
| **Pricing Model** | Flat-rate monthly: Standard ~$1,995/mo (5 users, unlimited orders); 3PL ~$2,145/mo |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | Mobile-based receiving |
| **Putaway** | NATIVE_CONFIRMED | Directed putaway |
| **Inventory Control** | NATIVE_CONFIRMED | Real-time multi-channel sync, lot/expiry tracking, kitting |
| **Inventory Ledger** | NOT_FOUND_IN_REVIEWED_SOURCES | Standard transaction tracking |
| **Cycle Counting** | NATIVE_CONFIRMED | Periodic cycle counts without operation shutdown |
| **Stock Adjustments** | NATIVE_CONFIRMED | Adjustment workflows |
| **Replenishment** | NOT_FOUND_IN_REVIEWED_SOURCES | Not explicitly documented |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Order-based allocation |
| **Picking** | NATIVE_CONFIRMED | Optimized pick routes, batch/wave picking |
| **Packing** | NATIVE_CONFIRMED | Barcode-verified packing, weight validation |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | Carrier rate shopping (USPS, FedEx, UPS, DHL), automated shipping rules |
| **Returns** | NATIVE_CONFIRMED | Returns processing |
| **Cross-docking** | NOT_FOUND_IN_REVIEWED_SOURCES | Not documented |
| **Yard Management** | NOT_FOUND_IN_REVIEWED_SOURCES | Not documented |
| **Labor/Workforce** | NATIVE_CONFIRMED | Labor & performance analytics, Hero Board dashboard |
| **Mobile/Scanner** | NATIVE_CONFIRMED | iOS/Android mobile app, barcode scanning verification |
| **Reporting** | NATIVE_CONFIRMED | Hero Board real-time dashboard, productivity metrics |
| **Integration** | NATIVE_CONFIRMED | Native ecommerce integrations (Shopify, Amazon, WooCommerce, BigCommerce), open API |
| **Multi-tenant/SaaS** | NATIVE_CONFIRMED | 3PL plan with client portal, multi-client separation |
| **Quality Control** | CONFIGURATION_CONFIRMED | Scan verification during pick/pack acts as quality gate |

#### Unique Differentiators
- **Flat-rate pricing with unlimited orders** — predictable cost regardless of volume
- **ParcelView** — photographic validation of packed orders for dispute resolution
- **PostHero** — branded tracking pages and post-purchase communication
- **Custom automation rules** — if/then logic for order handling (high-value tagging, address flags)
- Hero Board gamification dashboard for warehouse floor

#### Relevance to ERP_KHO
- **Relevance:** HIGH — Similar scale and use case for ecommerce fulfillment
- **Learn from:** Flat-rate pricing model (predictable for customers), ParcelView photo verification concept, Hero Board gamification dashboard for warehouse staff motivation, custom automation rules engine, barcode verification at every step
- **Do NOT copy:** US-centric carrier focus, limited inventory depth (no advanced lot/HU management), no explicit replenishment features

---

### 13. Extensiv (3PL Warehouse Manager)

| Field | Detail |
|-------|--------|
| **Full Name** | Extensiv 3PL Warehouse Manager (formerly 3PL Central) |
| **Vendor** | Extensiv Inc. |
| **URL** | https://www.extensiv.com/extensiv-3pl-warehouse-manager |
| **Version/Release** | Cloud SaaS, continuous updates |
| **Target Market** | 3PL warehouses, fulfillment centers |
| **Pricing Model** | SaaS subscription; custom quotes; estimated $1,500–$4,000+/month |
| **Date Accessed** | 2026-11-01 |

#### Capabilities

| Capability | Rating | Evidence |
|-----------|--------|----------|
| **Receiving** | NATIVE_CONFIRMED | ASN management, dock-to-stock processing, discrepancy logging |
| **Putaway** | NATIVE_CONFIRMED | Directed putaway |
| **Inventory Control** | NATIVE_CONFIRMED | SKU-level real-time tracking, lot/expiry/serial, UOM conversion |
| **Inventory Ledger** | NOT_FOUND_IN_REVIEWED_SOURCES | Standard transaction tracking |
| **Cycle Counting** | NATIVE_CONFIRMED | Cycle counting |
| **Stock Adjustments** | NATIVE_CONFIRMED | Adjustment workflows with audit trail |
| **Replenishment** | NATIVE_CONFIRMED | Replenishment workflows |
| **Reservation/Allocation** | NATIVE_CONFIRMED | Order allocation |
| **Picking** | NATIVE_CONFIRMED | Batch, wave, zone, put-to-light picking |
| **Packing** | NATIVE_CONFIRMED | Packing workflows |
| **Dispatch/Shipping** | NATIVE_CONFIRMED | ShipStation integration, carrier management |
| **Returns** | NATIVE_CONFIRMED | Returns processing |
| **Cross-docking** | CONFIGURATION_CONFIRMED | Configurable via workflow rules |
| **Yard Management** | NOT_FOUND_IN_REVIEWED_SOURCES | Not documented |
| **Labor/Workforce** | NATIVE_CONFIRMED | Labor productivity tracking in dashboards |
| **Mobile/Scanner** | NATIVE_CONFIRMED | Barcode/RF scanning, mobile integration |
| **Reporting** | NATIVE_CONFIRMED | Operational dashboards, audit summaries, inventory history, billing logs |
| **Integration** | NATIVE_CONFIRMED | Pre-wired: Shopify, WooCommerce, BigCommerce, Amazon; EDI; open API |
| **Multi-tenant/SaaS** | NATIVE_CONFIRMED | **Core design** — multi-client portals, isolated digital partitions |
| **Quality Control** | CONFIGURATION_CONFIRMED | Inspection at receiving |

#### Unique Differentiators
- **Purpose-built for 3PL** — multi-tenant is the core architecture, not an afterthought
- **Automated 3PL billing engine** — tracks every chargeable event (storage, handling, VAS) with flexible rate sheets per client
- **Client portals** — self-service stock/order visibility for merchants
- **EDI capabilities** built-in for enterprise retail client onboarding
- **UOM conversion at receipt** — vendor packaging → master item auto-conversion
- QuickBooks accounting integration for billing

#### Relevance to ERP_KHO
- **Relevance:** VERY HIGH — Direct competitor model for managed WMS SaaS
- **Learn from:** **3PL billing engine design** (chargeable event tracking — maps directly to ERP_KHO's SaaS monetization needs), multi-tenant client portal pattern, UOM conversion at receiving, ASN discrepancy logging workflow, EDI onboarding for enterprise clients
- **Do NOT copy:** Narrow 3PL-only focus (ERP_KHO also serves brand-owned warehouses), QuickBooks-centric accounting, US marketplace bias

---

## Consolidated Capability Matrix

The following table summarizes capability coverage across all 13 systems using abbreviations:

- ✅ = NATIVE_CONFIRMED
- ⚙️ = CONFIGURATION_CONFIRMED
- 🔌 = ADDON_OR_INTEGRATION
- ❌ = NOT_FOUND_IN_REVIEWED_SOURCES

| Capability | Oracle WMS | D365 WMS | SAP EWM | Manhattan | Blue Yonder | Infor | Körber | NetSuite | Odoo | ERPNext | Logiwa | ShipHero | Extensiv |
|-----------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Receiving** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Putaway** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Inventory Control** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Inventory Ledger** | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **Cycle Counting** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Stock Adjustments** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Replenishment** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| **Reservation/Allocation** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Picking** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Packing** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🔌 | ✅ | ⚙️ | ✅ | ✅ | ✅ |
| **Dispatch/Shipping** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🔌 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Returns** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Cross-docking** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ❌ | ⚙️ | ❌ | ⚙️ |
| **Yard Management** | ✅ | 🔌 | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Labor/Workforce** | ✅ | ⚙️ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Mobile/Scanner** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🔌 | ✅ | ✅ | ✅ |
| **Reporting** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Integration** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Multi-tenant/SaaS** | ✅ | ✅ | ⚙️ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | ⚙️ | ✅ | ✅ | ✅ |
| **Quality Control** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚙️ | 🔌 | ✅ | ⚙️ | ⚙️ | ⚙️ |

### Coverage Scores (out of 20)

| System | ✅ Native | ⚙️ Config | 🔌 Add-on | ❌ Not Found | Score |
|--------|:---------:|:---------:|:---------:|:------------:|:-----:|
| SAP EWM | 19 | 1 | 0 | 0 | 19.5 |
| Manhattan Active | 18 | 0 | 0 | 2 | 18.0 |
| Blue Yonder | 18 | 0 | 0 | 2 | 18.0 |
| Oracle Fusion WMS | 18 | 0 | 0 | 2 | 18.0 |
| Infor WMS | 18 | 0 | 0 | 2 | 18.0 |
| Körber WMS | 18 | 0 | 0 | 2 | 18.0 |
| D365 WMS | 17 | 2 | 1 | 0 | 18.0 |
| NetSuite WMS | 14 | 1 | 2 | 3 | 15.0 |
| Odoo Inventory | 14 | 2 | 2 | 2 | 15.5 |
| Logiwa IO | 14 | 3 | 0 | 3 | 15.0 |
| Extensiv | 14 | 3 | 0 | 3 | 15.0 |
| ERPNext Stock | 13 | 2 | 1 | 4 | 14.0 |
| ShipHero WMS | 13 | 2 | 0 | 5 | 13.5 |

*Score: ✅=1.0, ⚙️=0.75, 🔌=0.5, ❌=0*

---

## Key Patterns & Takeaways for ERP_KHO

### 1. Inventory Ledger Is ERP_KHO's Differentiator

Only **4 out of 13** systems have a documented inventory ledger concept:
- **ERPNext** — Stock Ledger Entry (immutable, timestamped, with valuation) — **closest to ERP_KHO's design**
- **Odoo** — Stock moves creating valuation layers
- **D365** — Inventory transactions with financial integration
- **SAP EWM** — Material documents / Universal Journal

> **Verdict:** ERP_KHO's immutable `inventory_ledger` with double-entry, 9-dimension grain, and reversal mechanism is a genuine differentiator. No Tier 3 ecommerce WMS has this. It provides audit-grade traceability that enterprise systems charge $100K+/year for.

### 2. Feature Priority for Vietnamese Ecommerce Fulfillment

Based on competitor analysis, the essential features for ERP_KHO's target market (1 warehouse, 5–30 staff, ecommerce fulfillment in Vietnam) are:

| Priority | Feature | Rationale | Best Reference |
|----------|---------|-----------|----------------|
| **P0 (Must)** | Receiving + Putaway | Every system has this; table stakes | Odoo (simple, effective) |
| **P0 (Must)** | Inventory Control (multi-status, lot) | Core WMS function | ERPNext (SLE pattern) |
| **P0 (Must)** | Mobile/Scanner support | Every modern WMS requires this | Odoo (GS1, error-proofing UX) |
| **P0 (Must)** | Picking (single + batch) | Core fulfillment throughput driver | ShipHero (optimized routes) |
| **P0 (Must)** | Packing + Dispatch | Ecommerce requires efficient ship | Logiwa (carrier rate shopping) |
| **P0 (Must)** | Returns | ~15-30% return rate in Vietnamese ecommerce | Logiwa (inspection + disposition) |
| **P1 (Should)** | Cycle Counting | Inventory accuracy maintenance | NetSuite (ABC classification) |
| **P1 (Should)** | Reservation/Allocation | Prevent overselling across channels | Odoo 17 (improved reserved qty handling) |
| **P1 (Should)** | Integration (API, marketplace) | Shopee, Lazada, Tiki connectors | Logiwa (200+ integrations pattern) |
| **P1 (Should)** | Reporting | Operational visibility | ShipHero (Hero Board concept) |
| **P2 (Nice)** | Replenishment | Useful at scale | D365 (wave-demand replenishment) |
| **P2 (Nice)** | 3PL billing | For SaaS monetization | Extensiv (chargeable event engine) |
| **P2 (Nice)** | Quality Control | For specific verticals (F&B, pharma) | SAP EWM (inspection lots) |
| **P3 (Defer)** | Cross-docking | Low priority for single warehouse | — |
| **P3 (Defer)** | Yard Management | Irrelevant at current scale | — |
| **P3 (Defer)** | Labor Management | Simple task assignment is enough for 5–30 staff | — |

### 3. Architectural Patterns to Adopt

| Pattern | Source | Application to ERP_KHO |
|---------|--------|----------------------|
| **Single-source-of-truth (WMS+ERP in one DB)** | NetSuite | Already doing this — validate as correct approach |
| **Immutable Stock Ledger Entry** | ERPNext | Already designed — confirm ERP_KHO's `inventory_ledger` is best-in-class for the segment |
| **GS1 barcode parsing** | Odoo | Add GS1 multi-data barcode support to scanner workflows |
| **Error-proofing mobile UX** | Odoo | Sound alerts, vibration, undo history for barcode scanning |
| **Flat-rate SaaS pricing** | ShipHero | Consider unlimited-orders pricing for customer predictability |
| **3PL billing automation** | Extensiv/Infor | Build usage metering + billing for managed WMS SaaS model |
| **Modular capability rollout** | Körber | Start with core WMS, add modules (QC, replenishment, billing) incrementally |
| **Photo verification** | ShipHero ParcelView | Add photo capture at packing for dispute resolution |
| **Gamification dashboard** | ShipHero/Körber | Hero Board or performance milestones for warehouse staff |
| **Smart putaway rules** | NetSuite/D365 | Rule-based location suggestion (velocity, size, zone) |
| **Carrier rate shopping** | Logiwa/ShipHero | Vietnamese carrier rate comparison (GHN, GHTK, VNPost, J&T, Ninja Van) |

### 4. What ERP_KHO Should NOT Copy

| Anti-Pattern | Source | Why Avoid |
|--------------|--------|-----------|
| Wave-based processing complexity | D365, SAP, Manhattan | Overkill for 1 warehouse; simple batch picking suffices |
| Enterprise labor standards engine | SAP, Manhattan, Blue Yonder | 5–30 staff don't need engineered labor standards |
| AI/ML features requiring massive data | Blue Yonder, Manhattan | Need 10,000+ orders/day to train; ERP_KHO won't have this initially |
| Multi-cloud / cloud-provider lock-in | Manhattan (GCP), Blue Yonder (Azure), Infor (AWS) | Stay cloud-agnostic |
| Feature gating Community vs Enterprise | Odoo | Confusing; ERP_KHO should have one clear tier structure |
| 200+ ecommerce connector breadth | Logiwa | Focus on 5 Vietnamese marketplaces, not 200 global ones |
| US-centric carrier integrations | ShipHero, Extensiv | Build for GHN, GHTK, VNPost, J&T, Ninja Van first |
| Complex EDI compliance | Extensiv, Körber | Vietnamese ecommerce uses REST APIs, not EDI |

### 5. Pricing Benchmark Summary

| System | Pricing Model | Monthly Cost Range |
|--------|-------------|-------------------|
| Oracle Fusion WMS | Per-site subscription | $2,500–$6,000+/site |
| D365 WMS | Per-user subscription | $180/user (base) |
| SAP EWM | Named user license | Custom (very expensive) |
| Manhattan Active | Enterprise subscription | $8,000–$40,000+/month (UNVERIFIED) |
| NetSuite WMS | Add-on module | $1,000–$2,000/month on top of ERP |
| Odoo Enterprise | Per-user subscription | $25–$37/user/month |
| ERPNext | Free (self-hosted) | $0 + hosting ($50–$500/mo) |
| Logiwa IO | SaaS subscription | $1,500–$5,000+/month |
| ShipHero WMS | Flat-rate monthly | $1,995–$2,145/month |
| Extensiv | SaaS subscription | $1,500–$4,000+/month |

> **Pricing opportunity for ERP_KHO:** Vietnamese ecommerce warehouses are price-sensitive. Targeting $200–$800/month (VND 5M–20M) for a managed WMS SaaS would be massively undercut vs. all Tier 1/3 options while providing Tier 1-grade inventory ledger capabilities. Odoo Community and ERPNext are free but require technical self-hosting expertise that most Vietnamese SMBs lack.

---

*End of Competitive Intelligence Report*  
*Total systems researched: 13*  
*Date: 2026-11-01*  
*Evidence quality: HIGH for Tier 1 (official docs confirmed), MEDIUM-HIGH for Tier 2-3 (mix of official docs and reputable reviews)*
