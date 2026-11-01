# Competitive Open-Source WMS/ERP Research

> **Research Date:** 2026-11-01  
> **Analyst:** Competitive Intelligence Agent  
> **Scope:** Open-source WMS and ERP platforms with warehouse management capabilities  
> **Evidence Quality:** Web search results cross-referenced across multiple sources. Items marked UNVERIFIED where single-source only.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Primary Repositories](#2-primary-repositories)
   - [2.1 OpenBoxes](#21-openboxes)
   - [2.2 OCA WMS (Odoo Community Association)](#22-oca-wms)
   - [2.3 ERPNext](#23-erpnext)
   - [2.4 OpenWMS.org](#24-openwmsorg)
   - [2.5 Odoo Community](#25-odoo-community)
3. [Additional Repositories](#3-additional-repositories)
   - [3.1 GreaterWMS](#31-greaterwms)
   - [3.2 ModernWMS](#32-modernwms)
   - [3.3 Indotalent Free WMS](#33-indotalent-free-wms)
   - [3.4 Sentry WMS](#34-sentry-wms)
   - [3.5 Fleetbase](#35-fleetbase)
4. [Capability Comparison Matrix](#4-capability-comparison-matrix)
5. [Architecture Comparison](#5-architecture-comparison)
6. [Lessons for ERP_KHO](#6-lessons-for-erp_kho)
7. [License Compatibility Summary](#7-license-compatibility-summary)

---

## 1. Executive Summary

This research evaluates **10 open-source repositories** with WMS/inventory management capabilities. The landscape breaks into three tiers:

| Tier | Repositories | Relevance to ERP_KHO |
|------|-------------|----------------------|
| **Enterprise-Grade Full ERP** | Odoo, ERPNext | Comprehensive stock modules, massive community. Architecture patterns (double-entry inventory, routes) highly instructive. |
| **Dedicated WMS** | OpenBoxes, GreaterWMS, ModernWMS, Sentry WMS, OpenWMS.org | Purpose-built for warehouse operations. Best sources for WMS-specific workflow patterns (putaway, picking, cycle counting). |
| **Specialized / Ecosystem** | OCA WMS, Indotalent, Fleetbase | Modular add-ons or logistics platforms. Useful for specific capability patterns. |

**Key Findings:**
- **Odoo's double-entry inventory model** (stock.move / stock.quant / routes) is the most architecturally sophisticated inventory pattern in open source
- **GreaterWMS** is the most feature-complete dedicated open-source WMS (~4.4k stars, active)
- **ModernWMS** is the closest tech-stack match (.NET + Vue3) but has limited activity and no formal releases
- **Sentry WMS** has the most modern scan-first architecture (Python/Flask + React Native mobile) with excellent putaway/picking patterns
- **OpenWMS.org** demonstrates the best microservices decomposition for warehouse domains

---

## 2. Primary Repositories

### 2.1 OpenBoxes

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/openboxes/openboxes |
| **Owner/Org** | OpenBoxes (originally PIH — Partners in Health) |
| **License** | Eclipse Public License 1.0 (EPL-1.0) |
| **Language/Framework** | Groovy/Java (Grails framework), React frontend, Hibernate/JPA |
| **Last Commit** | Active (commits through 2025-2026) — UNVERIFIED exact date |
| **Latest Release** | Multiple tagged releases; active release cycle |
| **GitHub Stars** | ~920 |
| **Contributors** | ~15-20 core + community (jmiranda, ewaterman, awalkowiak, etc.) |
| **CI/Test Evidence** | GitHub Actions CI, automated test suites |
| **Documentation** | Excellent — ReadTheDocs site, API docs (Swagger/OpenAPI), knowledge base |

#### B. Maturity: **MATURE_ACTIVE**

Long-running project (10+ years), professionally maintained, commercially deployed (OpenBoxes Cloud offering), used by major humanitarian organizations worldwide.

#### C. Architecture Assessment

- **Codebase:** Grails monolith with React frontend migration in progress. Modular domain separation (Inbound, Outbound, Inventory, Stock Movement, Reporting, Admin).
- **Domain Patterns:** Service-oriented within Grails (Controllers → Services → Domain Classes). Hibernate/JPA ORM with rich domain model.
- **Database:** Relational (MySQL/PostgreSQL). Transaction-based stock management with audit trails. Electronic stock cards per SKU.
- **API Design:** REST API with OpenAPI/Swagger documentation. Webhook support for integrations.

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ✅ Full | PO-based receiving, discrepancy handling, staged/direct put |
| 2 | Putaway | ✅ Full | Dedicated putaway dashboard, preferred bin mapping, line splitting |
| 3 | Inventory tracking | ✅ Full | Lot/expiry, multi-location, zones/bins, stock cards |
| 4 | Stock movements | ✅ Full | Inter-facility transfers, stock movement workflows |
| 5 | Picking | ⚠️ Basic | Pick lists available but not optimized walk-path picking |
| 6 | Packing | ⚠️ Basic | Part of outbound flow, not a dedicated packing station module |
| 7 | Shipping / Dispatch | ✅ Full | Outbound shipment workflow with carrier tracking |
| 8 | Cycle counting | ✅ Full | Cycle counts + ad-hoc adjustments with reason codes |
| 9 | Returns | ⚠️ Basic | Stock adjustments handle returns; no dedicated RMA module |
| 10 | Reservation / Allocation | ⚠️ Limited | Basic reservation through stock movement workflow |
| 11 | Replenishment | ❌ None | No automated replenishment rules |
| 12 | Quality control | ⚠️ Basic | Receiving inspection, no formal QC module |
| 13 | Barcode / Scanner | ✅ Full | Mobile app (openboxes-mobile), barcode scanning workflows |
| 14 | Reporting | ✅ Full | Comprehensive reports, dashboards, export capabilities |
| 15 | Multi-tenant | ❌ None | Single-organization, multi-facility model |

#### E. What ERP_KHO Can Learn

- **Putaway workflow design:** OpenBoxes has a clean putaway model with preferred bin mapping and line splitting — directly applicable to ERP_KHO's putaway module.
- **Stock movement as first-class entity:** Their "Stock Movement" concept wraps transfers, shipments, and receipts under a unified workflow model.
- **Lot/expiry tracking implementation:** Deeply integrated lot and expiration enforcement at the transaction level, with FIFO/FEFO enforcement.
- **Avoid:** Grails framework choice (aging technology); tight coupling between UI and backend in older modules.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable (ideas not copyrightable)
- **Code reuse:** ⚠️ EPL-1.0 is a weak copyleft. Modified files must remain EPL, but can link with proprietary code. Compatible with ERP_KHO's use case for pattern study, but direct code copying into a proprietary .NET codebase requires care.

---

### 2.2 OCA WMS

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/OCA/wms (+ OCA/stock-logistics-warehouse, OCA/stock-logistics-workflow, OCA/stock-logistics-shopfloor, OCA/stock-logistics-putaway, OCA/stock-logistics-reservation) |
| **Owner/Org** | Odoo Community Association (OCA) |
| **License** | LGPL-3.0 / AGPL-3.0 (varies by module) |
| **Language/Framework** | Python (Odoo Framework) |
| **Last Commit** | Continuously updated across multiple branches (14.0, 16.0, 17.0, 18.0) |
| **Latest Release** | Branch-based versioning matching Odoo versions |
| **GitHub Stars** | ~200-400 across individual repos |
| **Contributors** | 100+ across the OCA logistics ecosystem |
| **CI/Test Evidence** | OCA CI/CD with runbot, automated testing per PR |
| **Documentation** | Per-module READMEs, OCA documentation standards |

#### B. Maturity: **MATURE_ACTIVE**

One of the most active OCA project groups. Production-deployed at hundreds of Odoo installations globally.

#### C. Architecture Assessment

- **Codebase:** Collection of Odoo add-on modules. Each module is self-contained with `__manifest__.py`, models, views, controllers, and tests. Requires Odoo as the base platform.
- **Domain Patterns:** Odoo's ORM-centric approach. Models inherit/extend core `stock.*` models. Business logic in model methods.
- **Database:** PostgreSQL (Odoo standard). Schema extended via Odoo ORM migrations.
- **API Design:** Odoo XML-RPC / JSON-RPC. Some modules add REST endpoints.

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ✅ Full | Extended receipt workflows, quality checks |
| 2 | Putaway | ✅ Full | `stock-logistics-putaway`: advanced strategies (ABC, storage type) |
| 3 | Inventory tracking | ✅ Full | Extends Odoo's lot/serial/multi-location. Virtual reservations. |
| 4 | Stock movements | ✅ Full | Enhanced workflow modules for inter-warehouse |
| 5 | Picking | ✅ Full | Shopfloor modules with barcode-driven pick workflows |
| 6 | Packing | ✅ Full | Pack station operations via shopfloor |
| 7 | Shipping / Dispatch | ✅ Full | Dispatch and carrier integration modules |
| 8 | Cycle counting | ✅ Full | Inventory adjustment modules |
| 9 | Returns | ✅ Full | Via Odoo core + workflow extensions |
| 10 | Reservation / Allocation | ✅ Full | `stock-logistics-reservation`: virtual reservation, allocation strategies |
| 11 | Replenishment | ✅ Full | Orderpoint extensions |
| 12 | Quality control | ✅ Full | QC modules integrated into receipt/dispatch |
| 13 | Barcode / Scanner | ✅ Full | Shopfloor app, dedicated barcode UIs |
| 14 | Reporting | ✅ Full | Multiple reporting/analytics modules |
| 15 | Multi-tenant | ⚠️ Partial | Odoo multi-company (not true SaaS multi-tenant) |

#### E. What ERP_KHO Can Learn

- **Modular WMS decomposition:** OCA splits WMS into ~8 independent repos (putaway, reservation, workflow, shopfloor, etc.) — excellent reference for how to modularize WMS capabilities.
- **Shopfloor/barcode-first mobile UI:** The shopfloor module is designed exclusively for warehouse operators with barcode scanners — worth studying for ERP_KHO's mobile scanning UX.
- **Virtual reservation pattern:** `stock-logistics-reservation` implements reservation strategies that decouple sales allocation from physical stock — directly relevant to ERP_KHO's 9-dimension inventory model.
- **Putaway strategy abstraction:** Multiple putaway strategies (ABC analysis, storage type matching, fixed location) abstracted behind a clean interface.
- **Avoid:** Heavy dependency on Odoo framework makes code non-portable. XML-RPC API is outdated.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable
- **Code reuse:** ⚠️ LGPL-3.0/AGPL-3.0. Cannot copy code into proprietary project without open-sourcing derivative. Pattern study only.

---

### 2.3 ERPNext

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/frappe/erpnext |
| **Owner/Org** | Frappe Technologies |
| **License** | GNU GPLv3 |
| **Language/Framework** | Python (Frappe Framework), MariaDB/PostgreSQL, JavaScript |
| **Last Commit** | Continuously active (daily commits) |
| **Latest Release** | v15.x and v16.x active branches |
| **GitHub Stars** | ~24,700–31,800 |
| **Contributors** | Hundreds (major open-source ERP project) |
| **CI/Test Evidence** | Comprehensive CI/CD, automated test suites, nightly builds |
| **Documentation** | Extensive official docs (docs.erpnext.com), community wiki |

#### B. Maturity: **MATURE_ACTIVE**

One of the most popular open-source ERPs globally. Commercially backed by Frappe Technologies. Thousands of production deployments.

#### C. Architecture Assessment

- **Codebase:** Monolithic Frappe application with modular doctypes. `erpnext/stock/` contains all warehouse/inventory logic. Frappe framework provides ORM, workflow engine, permissions, and UI scaffolding.
- **Domain Patterns:** Doctype-centric (each business object is a "Doctype" with fields, permissions, workflows, and lifecycle hooks). Controller pattern for business logic.
- **Database:** MariaDB or PostgreSQL. Schema auto-managed by Frappe ORM. Stock Ledger Entry table for all inventory transactions.
- **API Design:** Auto-generated REST API from doctypes. Frappe Client API for programmatic access.

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ✅ Full | Purchase Receipt doctype, quality inspection, GRN |
| 2 | Putaway | ✅ Full | Putaway Rule doctype (v13+), capacity-based assignment |
| 3 | Inventory tracking | ✅ Full | Batch/serial (Serial and Batch Bundle in v15), multi-warehouse hierarchy |
| 4 | Stock movements | ✅ Full | Stock Entry (material transfer, receipt, issue, repack) |
| 5 | Picking | ✅ Full | Pick List doctype with location-based optimization |
| 6 | Packing | ✅ Full | Packing Slip + Delivery Note |
| 7 | Shipping / Dispatch | ✅ Full | Delivery Note, Shipment doctype |
| 8 | Cycle counting | ✅ Full | Stock Reconciliation doctype |
| 9 | Returns | ✅ Full | Return against Purchase Receipt / Delivery Note |
| 10 | Reservation / Allocation | ✅ Full | Stock Reservation Entry (v15+) |
| 11 | Replenishment | ✅ Full | Reorder Level, Material Request auto-creation |
| 12 | Quality control | ✅ Full | Quality Inspection doctype, batch testing |
| 13 | Barcode / Scanner | ⚠️ Basic | Barcode field on items; community PWA extensions (asoral/WMS) |
| 14 | Reporting | ✅ Full | Stock Ledger, Stock Balance, Warehouse-wise reports |
| 15 | Multi-tenant | ⚠️ Partial | Multi-company via Frappe; not true SaaS multi-tenant |

#### E. What ERP_KHO Can Learn

- **Stock Ledger Entry as immutable audit trail:** ERPNext's `Stock Ledger Entry` table is append-only and records every inventory transaction with valuation — directly analogous to ERP_KHO's `inventory_ledger` concept.
- **Valuation methods (FIFO, Moving Average, LIFO):** Well-implemented valuation strategies tied to the stock ledger.
- **Serial and Batch Bundle (v15):** Modern redesign of serial/batch tracking that groups serial numbers into bundles — cleaner than individual serial rows per transaction.
- **Putaway Rule:** Simple but effective rule-based putaway with capacity constraints per location.
- **Stock Reconciliation:** Clean approach to cycle counting that generates adjustment entries with full audit trail.
- **Avoid:** Frappe framework lock-in; tight coupling between UI and logic; FIFO valuation recalculation can be slow at scale; GPLv3 prevents any code reuse.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable
- **Code reuse:** ❌ GPLv3 (strong copyleft). No code can be incorporated into proprietary software. Pattern study only.

---

### 2.4 OpenWMS.org

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/openwms/org.openwms (umbrella) |
| **Owner/Org** | OpenWMS.org (Heiko Scherrer) |
| **License** | Apache-2.0 |
| **Language/Framework** | Java, Spring Boot 3.x, Spring Cloud, RabbitMQ |
| **Last Commit** | Active (2025-2026 commits across microservices) |
| **Latest Release** | Release 3.0.0 (ecosystem-wide); individual services iterate independently |
| **GitHub Stars** | ~756 (umbrella repo) |
| **Contributors** | Small core team (~5-10, primarily Heiko Scherrer) |
| **CI/Test Evidence** | GitHub Actions CI, unit and integration tests per service |
| **Documentation** | Project website (openwms.github.io), per-service READMEs |

#### B. Maturity: **ACTIVE**

Actively developed over 15+ years. Used in production for automated warehouse installations. Small but dedicated core team. Enterprise focus.

#### C. Architecture Assessment

- **Codebase:** True microservices architecture. Each bounded context is a separate Spring Boot service in its own Git repository:
  - `org.openwms.common.service` — Locations, Location Groups, Transport Units
  - `org.openwms.wms.movements` — Inventory movements, replenishment, stocktaking
  - `org.openwms.tms.routing` — Transport Management, material flow control, BPMN 2.0
  - `org.openwms.core.uaa` — User authentication and authorization
  - `org.openwms.core.preferences` — Configuration management
  - Spring Cloud Gateway for API gateway
- **Domain Patterns:** DDD (Domain-Driven Design) with clear aggregate boundaries per service. Event-driven communication via RabbitMQ/AMQP.
- **Database:** Per-service database (database-per-service pattern). H2/PostgreSQL.
- **API Design:** RESTful APIs per service, Spring Cloud Gateway routing, event-driven integration.

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ✅ Full | Via movements service |
| 2 | Putaway | ✅ Full | Putaway strategies calculating free bins/aisles |
| 3 | Inventory tracking | ✅ Full | Transport Unit tracking, location hierarchy |
| 4 | Stock movements | ✅ Full | Movement service with state machine |
| 5 | Picking | ⚠️ Basic | Task-based picking through movements |
| 6 | Packing | ❌ None | Not a primary focus (automated warehouse orientation) |
| 7 | Shipping / Dispatch | ⚠️ Basic | Outbound movement, carrier integration via extension |
| 8 | Cycle counting | ✅ Full | Stocktaking via movements service |
| 9 | Returns | ⚠️ Basic | Reverse movement, not a dedicated module |
| 10 | Reservation / Allocation | ⚠️ Basic | Location reservation concept |
| 11 | Replenishment | ✅ Full | Replenishment movements |
| 12 | Quality control | ❌ None | Not implemented |
| 13 | Barcode / Scanner | ✅ Full | Flutter mobile app, hardware integration (PLC/conveyor) |
| 14 | Reporting | ⚠️ Basic | Per-service metrics, no centralized reporting |
| 15 | Multi-tenant | ❌ None | Single-tenant enterprise deployment |

#### E. What ERP_KHO Can Learn

- **Microservices decomposition for WMS:** Best-in-class example of how to split WMS into bounded contexts (Common, Movements, Transport, Routing). ERP_KHO can reference this for future microservice extraction.
- **Transport Unit as first-class entity:** The concept of a Transport Unit (TU) wrapping physical goods is worth studying for ERP_KHO's handling unit model.
- **BPMN 2.0 for material flow:** Using a workflow engine (Flowable/Camunda) for complex routing — relevant if ERP_KHO needs configurable warehouse workflows.
- **Event-driven architecture:** Clean event-based communication between services using AMQP — directly applicable to ERP_KHO's Outbox pattern.
- **Hardware integration patterns:** PLC/conveyor integration via the routing service — useful if ERP_KHO ever needs automated warehouse support.
- **Avoid:** Over-engineering for simple manual warehouses; small community limits ecosystem support.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable
- **Code reuse:** ✅ Apache-2.0. Very permissive. Code can be incorporated with attribution. Compatible with proprietary use.

---

### 2.5 Odoo Community

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/odoo/odoo |
| **Owner/Org** | Odoo S.A. |
| **License** | LGPL-3.0 (Community Edition) |
| **Language/Framework** | Python (Odoo Framework), PostgreSQL, JavaScript/OWL |
| **Last Commit** | Daily (most active open-source ERP) |
| **Latest Release** | Odoo 18 (latest), branches 16.0, 17.0, 18.0 |
| **GitHub Stars** | ~38,000-55,000 |
| **Contributors** | 1,000+ |
| **CI/Test Evidence** | Extensive automated testing, runbot CI |
| **Documentation** | Comprehensive official documentation (odoo.com/documentation) |

#### B. Maturity: **MATURE_ACTIVE**

The most popular open-source ERP globally. Massive ecosystem of partners, modules, and deployments. Dual licensing model (Community LGPL / Enterprise proprietary).

#### C. Architecture Assessment

- **Codebase:** Monolithic Odoo framework with modular add-ons. Stock module at `addons/stock/`. Core models: `stock.warehouse`, `stock.location`, `stock.picking`, `stock.move`, `stock.quant`, `stock.route`, `stock.rule`.
- **Domain Patterns:** **Double-entry inventory** — the standout architectural concept. Every stock movement is a transfer from source location to destination location (analogous to double-entry bookkeeping). Model inheritance (delegation and extension) for customization.
- **Database:** PostgreSQL exclusively. ORM-managed schema with automatic migration.
- **API Design:** XML-RPC / JSON-RPC (legacy). OWL-based frontend with internal bus.

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ✅ Full | Multi-step receiving (1/2/3-step), quality check |
| 2 | Putaway | ✅ Full | Push rules, putaway rules by product/category |
| 3 | Inventory tracking | ✅ Full | Lots, serials, packages, owner tracking, quants |
| 4 | Stock movements | ✅ Full | stock.move with full chain traceability |
| 5 | Picking | ✅ Full | Multi-step routes (Pick→Pack→Ship), wave picking |
| 6 | Packing | ✅ Full | Pack operations, package hierarchy |
| 7 | Shipping / Dispatch | ✅ Full | Delivery orders, carrier connectors |
| 8 | Cycle counting | ✅ Full | Inventory adjustments, planned counts |
| 9 | Returns | ✅ Full | Return operations with reason tracking |
| 10 | Reservation / Allocation | ✅ Full | Automatic reservation, manual (un)reserve |
| 11 | Replenishment | ✅ Full | Pull/push rules, reordering rules, MTO/MTS |
| 12 | Quality control | ✅ Enterprise | Quality module (Enterprise), basic in Community |
| 13 | Barcode / Scanner | ✅ Enterprise | Barcode app (Enterprise), community alternatives |
| 14 | Reporting | ✅ Full | Stock valuation, forecasting, traceability reports |
| 15 | Multi-tenant | ⚠️ Partial | Multi-company; not true SaaS multi-tenant |

#### E. What ERP_KHO Can Learn

- **⭐ Double-entry inventory model:** This is the single most important pattern. Every movement = debit from source location + credit to destination location. No stock appears/disappears from nowhere. ERP_KHO's inventory_ledger already follows a similar philosophy.
- **stock.quant as materialized balance:** Odoo maintains `stock.quant` as a real-time snapshot of current inventory (product × location × lot × package × owner). This is the exact analogue of ERP_KHO's `inventory_balances` table with its 9-dimension grain.
- **Route automation (Pull/Push rules):** Configurable rules that automatically chain operations (e.g., receipt triggers putaway, sales order triggers pick→pack→ship). ERP_KHO could study this for its configurable warehouse workflow engine.
- **Location types as virtual locations:** Using virtual locations (Vendor, Customer, Inventory Loss, Production, Transit) to maintain double-entry integrity — elegant pattern for adjustment and scrap accounting.
- **Multi-step operations via routes:** The 1/2/3-step configuration for receiving and shipping is a clean abstraction over simple vs. complex warehouse workflows.
- **Avoid:** LGPL license constraints; Odoo-specific ORM patterns don't translate directly to EF Core; performance issues at high transaction volumes due to Python/ORM overhead; XML-RPC API is legacy.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable
- **Code reuse:** ⚠️ LGPL-3.0. Can link with proprietary code but modifications to LGPL files must be shared. Pattern study recommended, no code copying.

---

## 3. Additional Repositories

### 3.1 GreaterWMS

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/GreaterWMS/GreaterWMS |
| **Owner/Org** | GreaterWMS |
| **License** | Apache-2.0 |
| **Language/Framework** | Python (Django), Vue.js (Quasar), Rust (v3.0 plugins) |
| **Last Commit** | Active (2025-2026, recent release tags) |
| **Latest Release** | V2.1.54+ |
| **GitHub Stars** | ~4,400 |
| **Contributors** | ~63 |
| **CI/Test Evidence** | GitHub Actions, Docker deployment |
| **Documentation** | Documentation site, video tutorials, Scribd guides |

#### B. Maturity: **MATURE_ACTIVE**

Most popular dedicated open-source WMS. Production heritage from Ford Asia Pacific after-sales logistics. Active development with regular releases.

#### C. Architecture Assessment

- **Codebase:** Django backend with Quasar (Vue.js) frontend. Domain-organized modules (ASN/receiving, DN/shipping, storage, inventory). "OneAPP" concept — single frontend codebase compiles to web, mobile (Android/iOS), and PDA scanner terminals.
- **Domain Patterns:** Django model-centric with service layer. Modules organized by warehouse workflow domain.
- **Database:** Django ORM, PostgreSQL/MySQL.
- **API Design:** REST API (Django REST Framework). ERP integration endpoints (SAP, Laudus, etc.)

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ✅ Full | ASN-based, PO matching, PDA verification |
| 2 | Putaway | ✅ Full | Directed placement, scan-confirmed bin assignment |
| 3 | Inventory tracking | ✅ Full | Multi-location, barcode-driven |
| 4 | Stock movements | ✅ Full | Internal transfers, bin-to-bin |
| 5 | Picking | ✅ Full | DN-based, batch picking, PDA-guided routing |
| 6 | Packing | ✅ Full | Order consolidation, box assignment verification |
| 7 | Shipping / Dispatch | ✅ Full | Carrier management, label printing, stock state update |
| 8 | Cycle counting | ✅ Full | Scan-based counting |
| 9 | Returns | ⚠️ Basic | Reverse ASN flow |
| 10 | Reservation / Allocation | ⚠️ Basic | Order-based allocation |
| 11 | Replenishment | ⚠️ Basic | Min/max rules |
| 12 | Quality control | ⚠️ Basic | Receiving inspection |
| 13 | Barcode / Scanner | ✅ Full | ⭐ Deep GS1 integration, native PDA app, scan-driven everything |
| 14 | Reporting | ✅ Full | Warehouse analytics |
| 15 | Multi-tenant | ❌ None | Single organization |

#### E. What ERP_KHO Can Learn

- **⭐ OneAPP / Multi-platform mobile strategy:** Single Vue.js/Quasar codebase compiling to web + Android + PDA is an efficient approach for warehouse mobile apps.
- **Scan-driven workflow design:** Every operation requires barcode confirmation — product scan then bin scan — ensuring data accuracy. This is the standard ERP_KHO should target.
- **ASN/DN document model:** Clean separation of inbound (ASN) and outbound (DN/Delivery Notice) document types with scan verification at each stage.
- **ERP integration patterns:** Pre-built connectors to SAP, eCommerce platforms — useful reference for ERP_KHO's integration layer design.
- **Avoid:** Python/Django may have performance ceiling at very high transaction volumes; limited lot/serial tracking depth compared to ERPNext/Odoo.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable
- **Code reuse:** ✅ Apache-2.0. Very permissive. Can incorporate with attribution.

---

### 3.2 ModernWMS

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/fjykTec/ModernWMS |
| **Owner/Org** | fjykTec |
| **License** | Apache-2.0 |
| **Language/Framework** | **.NET 7+** (C#), Vue 3 + TypeScript + Vite, EF Core |
| **Last Commit** | Intermittent (last major activity mid-to-late 2024) — UNVERIFIED |
| **Latest Release** | No formal tagged releases (master branch deployment) |
| **GitHub Stars** | ~1,800 |
| **Contributors** | ~5 core |
| **CI/Test Evidence** | Docker deployment, minimal CI evidence |
| **Documentation** | README with deployment guide, limited API docs |

#### B. Maturity: **MAINTAINED_BUT_LIMITED**

Derived from commercial ERP implementations. Clean codebase but small team, no formal releases, intermittent commit activity. Good starting point but not production-grade on its own.

#### C. Architecture Assessment

- **Codebase:** ⭐ **Closest tech-stack match to ERP_KHO** (.NET + Vue3 + TypeScript + Vite + EF Core). Modular structure with controllers, services, and data access layers. Docker-ready.
- **Domain Patterns:** Layered architecture (Controllers → Services → Repository). Entity Framework Core for data access. Basic service layer pattern.
- **Database:** SQL Server, MySQL, or PostgreSQL via EF Core.
- **API Design:** ASP.NET Core Web API, REST endpoints.

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ✅ Full | PO tracking, discrepancy logging, label generation |
| 2 | Putaway | ✅ Full | Directed storage, location optimization |
| 3 | Inventory tracking | ✅ Full | Real-time by zone/shelf/bin |
| 4 | Stock movements | ✅ Full | Internal transfers, adjustments |
| 5 | Picking | ✅ Full | Task-based picking lists |
| 6 | Packing | ⚠️ Basic | Part of outbound flow |
| 7 | Shipping / Dispatch | ⚠️ Basic | Basic outbound |
| 8 | Cycle counting | ✅ Full | Cycle counting with digital logs |
| 9 | Returns | ⚠️ Basic | Via stock adjustments |
| 10 | Reservation / Allocation | ⚠️ Basic | Order-level allocation |
| 11 | Replenishment | ❌ None | Not implemented |
| 12 | Quality control | ❌ None | Not implemented |
| 13 | Barcode / Scanner | ⚠️ Basic | Barcode generation, basic scan support |
| 14 | Reporting | ⚠️ Basic | Basic inventory visualization |
| 15 | Multi-tenant | ❌ None | Single organization |

#### E. What ERP_KHO Can Learn

- **⭐ .NET + Vue3 + Vite reference implementation:** The closest tech-stack peer to ERP_KHO. Worth studying for:
  - How they structure the .NET API controllers for WMS operations
  - Vue 3 + TypeScript + Vite frontend organization for warehouse UI
  - EF Core configuration patterns for inventory tables
  - Docker containerization of .NET + Vue stack
- **Minimalist WMS scope:** ModernWMS intentionally keeps things simple — a useful benchmark for feature scope management.
- **Avoid:** Lack of proper domain layer (no DDD); no concurrency control patterns; no audit trail; limited CI/testing; dormant development activity.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable
- **Code reuse:** ✅ Apache-2.0. Very permissive. Code can be studied and adapted with attribution.

---

### 3.3 Indotalent Free WMS

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/go2ismail/Asp.Net-Core-Inventory-Order-Management-System |
| **Owner/Org** | go2ismail (Indotalent) |
| **License** | CC BY 4.0 (Creative Commons Attribution) |
| **Language/Framework** | **.NET 9**, C#, Clean Architecture, CQRS, MediatR, EF Core, Razor Pages + Vue.js |
| **Last Commit** | Active (upgraded to .NET 9) |
| **Latest Release** | Continuous updates |
| **GitHub Stars** | ~1,300 |
| **Contributors** | Small team |
| **CI/Test Evidence** | Basic testing, demo deployment |
| **Documentation** | Indotalent.com documentation |

#### B. Maturity: **ACTIVE**

Well-maintained demo/starter project. Uses modern .NET patterns. More of an inventory/order management system than a full WMS.

#### C. Architecture Assessment

- **Codebase:** ⭐ **Clean Architecture** (Core → Infrastructure → Presentation). CQRS with MediatR. Headless API backend + multiple frontend options.
- **Domain Patterns:** CQRS, MediatR command/query handlers, Entity Framework Core, Repository pattern.
- **Database:** SQL Server via EF Core.
- **API Design:** ASP.NET Core Web API with Razor Pages admin.

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ✅ Full | Purchase orders, goods receipt |
| 2 | Putaway | ❌ None | Not implemented |
| 3 | Inventory tracking | ✅ Full | Multi-warehouse, basic tracking |
| 4 | Stock movements | ✅ Full | Stock transfers |
| 5 | Picking | ❌ None | Not implemented |
| 6 | Packing | ❌ None | Not implemented |
| 7 | Shipping / Dispatch | ✅ Full | Sales/delivery orders |
| 8 | Cycle counting | ⚠️ Basic | Stock adjustments |
| 9 | Returns | ✅ Full | Sales returns, purchase returns |
| 10 | Reservation / Allocation | ❌ None | Not implemented |
| 11 | Replenishment | ❌ None | Not implemented |
| 12 | Quality control | ❌ None | Not implemented |
| 13 | Barcode / Scanner | ❌ None | Not implemented |
| 14 | Reporting | ⚠️ Basic | Basic inventory reports |
| 15 | Multi-tenant | ❌ None | Single organization |

#### E. What ERP_KHO Can Learn

- **⭐ Clean Architecture + CQRS + MediatR in .NET:** This is the most architecturally aligned reference to ERP_KHO's own structure. Worth studying:
  - Project layout (Core/Infrastructure/Presentation separation)
  - MediatR handler organization
  - EF Core configuration in Clean Architecture
  - How purchase/sales orders flow through command handlers
- **Avoid:** Very limited WMS depth; no concurrency handling; no lot/serial tracking; CC BY 4.0 license requires attribution but is otherwise permissive.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable
- **Code reuse:** ✅ CC BY 4.0. Very permissive with attribution requirement.

---

### 3.4 Sentry WMS

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/hightower-systems/sentry-wms |
| **Owner/Org** | Hightower Systems |
| **License** | MIT |
| **Language/Framework** | Python (Flask), React 18 + Vite (admin), React Native/Expo (mobile scanner), PostgreSQL 16 |
| **Last Commit** | Recent (2024-2025) — UNVERIFIED exact date |
| **Latest Release** | UNVERIFIED |
| **GitHub Stars** | Newer project (likely <500) — UNVERIFIED |
| **Contributors** | Small team |
| **CI/Test Evidence** | Docker Compose deployment |
| **Documentation** | README, dev.to articles with detailed feature descriptions |

#### B. Maturity: **ACTIVE**

Newer project with excellent e-commerce WMS design. Built specifically for the gap between ERP and physical warehouse execution. Clean, modern codebase.

#### C. Architecture Assessment

- **Codebase:** Flask API with parameterized SQL queries (not ORM-heavy). React 18 + Vite admin panel. React Native (Expo) mobile app designed specifically for industrial barcode scanners (Chainway C6000 Android devices, broadcast intent scanning).
- **Domain Patterns:** Service-oriented Flask backend. Dedicated API endpoints per warehouse operation. SQL-first data access.
- **Database:** PostgreSQL 16. Direct SQL queries with parameterized inputs.
- **API Design:** REST API organized by workflow domain (`/api/putaway/*`, `/api/picking/*`, etc.)

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ✅ Full | PO scanning, item verification, quantity tracking |
| 2 | Putaway | ✅ Full | ⭐ Suggested bin placement, scan-to-confirm |
| 3 | Inventory tracking | ✅ Full | Item-bin level, zone-based |
| 4 | Stock movements | ✅ Full | Bin-to-bin transfers |
| 5 | Picking | ✅ Full | ⭐ Wave validation, multi-order batch, optimized walk paths |
| 6 | Packing | ✅ Full | Pack verification (scan SO + scan items) |
| 7 | Shipping / Dispatch | ✅ Full | Carrier selection, tracking number, fulfillment completion |
| 8 | Cycle counting | ✅ Full | Blind/non-blind counting, admin approval |
| 9 | Returns | ⚠️ Basic | Via inventory adjustments |
| 10 | Reservation / Allocation | ⚠️ Basic | Order-based allocation |
| 11 | Replenishment | ❌ None | Not implemented |
| 12 | Quality control | ❌ None | Not implemented |
| 13 | Barcode / Scanner | ✅ Full | ⭐ Industrial scanner-first (broadcast intent, not camera) |
| 14 | Reporting | ⚠️ Basic | Admin dashboard, audit logs |
| 15 | Multi-tenant | ⚠️ Basic | Warehouse-level isolation |

#### E. What ERP_KHO Can Learn

- **⭐ Scan-first mobile architecture:** React Native with Expo for industrial barcode scanners using broadcast intents (not camera) — this is the correct approach for real warehouse hardware. Directly applicable to ERP_KHO's mobile strategy.
- **⭐ Putaway suggestion API:** Clean API design (`/api/putaway/suggest/<item_id>` → returns suggested bin) — excellent pattern for ERP_KHO's putaway service.
- **Wave picking design:** Wave validation and multi-order batch picking with walk-path optimization — relevant for ERP_KHO's picking module.
- **Cycle counting with approval workflow:** Blind/non-blind options with admin approval/rejection — clean design for ERP_KHO's stocktake module.
- **E-commerce execution layer concept:** Positioned as the "execution layer between ERP and warehouse floor" — this is exactly ERP_KHO's market positioning.
- **Avoid:** Flask + raw SQL may not scale to enterprise; newer project with less battle-testing; small community.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable
- **Code reuse:** ✅ MIT. Most permissive. Can freely incorporate with minimal attribution.

---

### 3.5 Fleetbase

#### A. Repository Identity

| Field | Value |
|-------|-------|
| **Full URL** | https://github.com/fleetbase/fleetbase |
| **Owner/Org** | Fleetbase |
| **License** | AGPL-3.0 (with commercial FCL option) |
| **Language/Framework** | JavaScript/Ember.js frontend, PHP/Laravel backend |
| **Last Commit** | Active |
| **Latest Release** | Active development |
| **GitHub Stars** | ~4,400 |
| **Contributors** | Growing community |
| **CI/Test Evidence** | CI pipelines present |
| **Documentation** | Official docs (fleetbase.io) |

#### B. Maturity: **ACTIVE**

More of a logistics operating system than a pure WMS. Strong in transportation, fleet management, and last-mile delivery. Warehouse is one module among many.

#### C. Architecture Assessment

- **Codebase:** Ember.js SPA frontend + Laravel (PHP) backend. Modular extension system (storefront, fleet-ops, pallet). Plugin-based architecture.
- **Domain Patterns:** Laravel service providers, modular packages.
- **Database:** MySQL/MariaDB via Laravel Eloquent ORM.
- **API Design:** REST API with webhooks.

#### D. Business Capabilities

| # | Capability | Status | Notes |
|---|-----------|--------|-------|
| 1 | Receiving / Inbound | ⚠️ Basic | Part of pallet/inventory module |
| 2 | Putaway | ❌ None | Not a focus |
| 3 | Inventory tracking | ⚠️ Basic | Pallet-level tracking |
| 4 | Stock movements | ⚠️ Basic | Transfer tracking |
| 5 | Picking | ❌ None | Not implemented |
| 6 | Packing | ❌ None | Not implemented |
| 7 | Shipping / Dispatch | ✅ Full | ⭐ Core strength — fleet dispatch, routing, tracking |
| 8 | Cycle counting | ❌ None | Not implemented |
| 9 | Returns | ⚠️ Basic | Via order management |
| 10 | Reservation / Allocation | ❌ None | Not implemented |
| 11 | Replenishment | ❌ None | Not implemented |
| 12 | Quality control | ❌ None | Not implemented |
| 13 | Barcode / Scanner | ⚠️ Basic | Order scanning |
| 14 | Reporting | ✅ Full | Logistics analytics |
| 15 | Multi-tenant | ✅ Full | Multi-org support |

#### E. What ERP_KHO Can Learn

- **Last-mile delivery integration:** If ERP_KHO ever needs delivery/dispatch tracking, Fleetbase's fleet-ops module is a reference.
- **Plugin architecture for extensions:** Modular package system for extending core functionality.
- **Limited WMS relevance:** Not a primary reference for warehouse operations.
- **Avoid:** AGPL-3.0 is restrictive for SaaS; PHP/Laravel stack mismatch; weak WMS capabilities.

#### F. License Note

- **Architecture/patterns:** ✅ Always referrable
- **Code reuse:** ❌ AGPL-3.0. Cannot use in proprietary SaaS without open-sourcing. Pattern study only.

---

## 4. Capability Comparison Matrix

| # | Capability | OpenBoxes | OCA WMS | ERPNext | OpenWMS | Odoo CE | GreaterWMS | ModernWMS | Indotalent | Sentry WMS | Fleetbase |
|---|-----------|-----------|---------|---------|---------|---------|------------|-----------|------------|------------|-----------|
| 1 | Receiving | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| 2 | Putaway | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ |
| 3 | Inventory (lot/serial/loc) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ | ✅ | ⚠️ |
| 4 | Stock movements | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| 5 | Picking | ⚠️ | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ |
| 6 | Packing | ⚠️ | ✅ | ✅ | ❌ | ✅ | ✅ | ⚠️ | ❌ | ✅ | ❌ |
| 7 | Shipping / Dispatch | ✅ | ✅ | ✅ | ⚠️ | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ |
| 8 | Cycle counting | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ | ✅ | ❌ |
| 9 | Returns | ⚠️ | ✅ | ✅ | ⚠️ | ✅ | ⚠️ | ⚠️ | ✅ | ⚠️ | ⚠️ |
| 10 | Reservation / Alloc | ⚠️ | ✅ | ✅ | ⚠️ | ✅ | ⚠️ | ⚠️ | ❌ | ⚠️ | ❌ |
| 11 | Replenishment | ❌ | ✅ | ✅ | ✅ | ✅ | ⚠️ | ❌ | ❌ | ❌ | ❌ |
| 12 | Quality control | ⚠️ | ✅ | ✅ | ❌ | ⚠️* | ⚠️ | ❌ | ❌ | ❌ | ❌ |
| 13 | Barcode / Scanner | ✅ | ✅ | ⚠️ | ✅ | ⚠️* | ✅ | ⚠️ | ❌ | ✅ | ⚠️ |
| 14 | Reporting | ✅ | ✅ | ✅ | ⚠️ | ✅ | ✅ | ⚠️ | ⚠️ | ⚠️ | ✅ |
| 15 | Multi-tenant | ❌ | ⚠️ | ⚠️ | ❌ | ⚠️ | ❌ | ❌ | ❌ | ⚠️ | ✅ |

> **Legend:** ✅ = Full implementation | ⚠️ = Basic/Partial | ❌ = Not implemented | * = Enterprise Edition feature

**Top 3 by capability coverage:**
1. **Odoo Community + OCA WMS** — 15/15 capabilities (many require Enterprise or OCA modules)
2. **ERPNext** — 14/15 capabilities (barcode scanner is basic/community)
3. **GreaterWMS** — 12/15 capabilities (best standalone WMS)

---

## 5. Architecture Comparison

| Aspect | OpenBoxes | OCA/Odoo | ERPNext | OpenWMS | GreaterWMS | ModernWMS | Sentry WMS | Indotalent |
|--------|-----------|----------|---------|---------|------------|-----------|------------|------------|
| **Architecture** | Monolith | Monolith (modular) | Monolith | Microservices | Monolith | Monolith | Layered | Clean Arch |
| **Language** | Groovy/Java | Python | Python | Java | Python | **C#/.NET** | Python | **C#/.NET** |
| **Frontend** | React | OWL/JS | JS | Flutter | Quasar/Vue | **Vue3+TS+Vite** | React+RN | Razor+Vue |
| **ORM** | Hibernate | Odoo ORM | Frappe ORM | JPA | Django ORM | **EF Core** | Raw SQL | **EF Core** |
| **DB** | MySQL/PG | PostgreSQL | MariaDB/PG | H2/PG | PG/MySQL | **SQL Server** | PostgreSQL | **SQL Server** |
| **API** | REST | RPC | REST | REST | REST | REST | REST | REST |
| **DDD** | No | No | No | ✅ Yes | No | No | No | CQRS |
| **Event-Driven** | Webhooks | Signals | Hooks | ✅ AMQP | Basic | No | No | MediatR |
| **Concurrency** | Basic | PG locks | Versioning | ✅ | Basic | Basic | Basic | Basic |
| **Audit Trail** | ✅ Strong | ✅ Strong | ✅ Strong | Per-service | Basic | Minimal | ✅ Logs | Basic |

**Closest to ERP_KHO's stack:** ModernWMS (.NET + Vue3 + EF Core + SQL Server) and Indotalent (.NET + EF Core + Clean Architecture)

---

## 6. Lessons for ERP_KHO

### 6.1 Patterns Worth Studying (Priority Order)

| Priority | Pattern | Best Source | Applicability |
|----------|---------|-------------|---------------|
| 🔴 P0 | Double-entry inventory (debit/credit by location) | **Odoo** `stock.move` / `stock.quant` | ERP_KHO's `inventory_ledger` + `inventory_balances` already follow this; validate alignment |
| 🔴 P0 | Immutable stock ledger + materialized balance | **ERPNext** `Stock Ledger Entry` | Confirms ERP_KHO's design of append-only ledger + maintained balances |
| 🟡 P1 | Putaway suggestion API | **Sentry WMS** `/api/putaway/suggest` | Direct implementation reference for putaway service |
| 🟡 P1 | Barcode/scanner-first mobile design | **Sentry WMS** (broadcast intent), **GreaterWMS** (OneAPP) | Mobile scanner app architecture |
| 🟡 P1 | Multi-step warehouse routes (Pick→Pack→Ship) | **Odoo** routes + **ERPNext** Pick List | Configurable workflow engine |
| 🟡 P1 | Virtual reservation / allocation strategies | **OCA** `stock-logistics-reservation` | 9-dimension allocation patterns |
| 🟢 P2 | Microservice bounded context decomposition | **OpenWMS.org** service topology | Future scalability reference |
| 🟢 P2 | .NET + Vue3 WMS implementation patterns | **ModernWMS** codebase | API controller + frontend organization |
| 🟢 P2 | Clean Architecture + CQRS for inventory | **Indotalent** project layout | CQRS handler organization reference |
| 🟢 P2 | Transport Unit / Handling Unit model | **OpenWMS.org** `TransportUnit` | ERP_KHO's `HandlingUnitId` dimension |
| 🟢 P2 | Wave picking + walk-path optimization | **Sentry WMS** picking module | Picking optimization |
| 🔵 P3 | Event-driven inter-module communication | **OpenWMS.org** AMQP | Outbox pattern validation |
| 🔵 P3 | OneAPP multi-platform frontend | **GreaterWMS** Quasar | Multi-device deployment strategy |

### 6.2 Business Process Designs Worth Considering

1. **Odoo's configurable operations steps (1/2/3-step receiving, 1/2/3-step shipping)** — Allow warehouse owners to configure complexity: simple operations use 1-step (receipt directly to stock), complex warehouses use 3-step (receive → quality check → putaway).

2. **ERPNext's Putaway Rule with capacity tracking** — Each putaway rule specifies target warehouse, capacity, priority, and stock UOM. System auto-assigns based on remaining capacity.

3. **Sentry WMS's cycle count with blind/non-blind modes** — Blind mode hides expected quantities from counters (more accurate), non-blind shows them (faster). Admin approval/rejection workflow.

4. **OCA's shopfloor barcode-first UX** — Full-screen scanning interface designed for warehouse floor use — no complex menus, just scan and act.

5. **GreaterWMS's scan-confirmation pattern** — Every physical action (putaway, pick, pack) requires two scans: product barcode + location barcode. This double-confirmation dramatically reduces errors.

### 6.3 What to Avoid

| Anti-Pattern | Seen In | Why to Avoid |
|-------------|---------|--------------|
| Framework lock-in | ERPNext (Frappe), Odoo (ORM) | Business logic tightly coupled to framework; can't extract or test independently |
| XML-RPC API | Odoo | Outdated, poor developer experience, no OpenAPI spec |
| ORM for all queries | Most Python-based WMS | Performance ceiling for complex inventory queries; ERP_KHO correctly uses Dapper for hot paths |
| No concurrency control | ModernWMS, Sentry WMS | Leads to race conditions in inventory updates; ERP_KHO's UPDLOCK approach is superior |
| Single-tenant only | OpenBoxes, GreaterWMS, ModernWMS | ERP_KHO's multi-tenant design is a competitive advantage |
| Monolith-only inventory engine | All except OpenWMS.org | Consider designing ERP_KHO's inventory engine as an extractable bounded context |

---

## 7. License Compatibility Summary

| Repository | License | Pattern Study | Code Reuse in Proprietary | Notes |
|-----------|---------|:------------:|:------------------------:|-------|
| OpenBoxes | EPL-1.0 | ✅ | ⚠️ Modified files must stay EPL | Weak copyleft |
| OCA WMS | LGPL-3.0 / AGPL-3.0 | ✅ | ❌ AGPL modules | Strong copyleft on some modules |
| ERPNext | GPLv3 | ✅ | ❌ Cannot incorporate | Strong copyleft |
| OpenWMS.org | **Apache-2.0** | ✅ | ✅ With attribution | Most permissive of primary repos |
| Odoo CE | LGPL-3.0 | ✅ | ⚠️ Modifications must share | Weak copyleft |
| GreaterWMS | **Apache-2.0** | ✅ | ✅ With attribution | Permissive |
| ModernWMS | **Apache-2.0** | ✅ | ✅ With attribution | Permissive, closest tech stack |
| Indotalent | **CC BY 4.0** | ✅ | ✅ With attribution | Very permissive |
| Sentry WMS | **MIT** | ✅ | ✅ Minimal obligation | Most permissive |
| Fleetbase | AGPL-3.0 | ✅ | ❌ Cannot use in SaaS | Must open-source for network use |

> **Note:** Architecture patterns, business process designs, and algorithmic approaches are **always** freely referenceable regardless of license — ideas and methods are not subject to copyright. Only specific code implementations are restricted by their license terms.

---

## Appendix: Research Methodology

- **Data Collection:** Web search across GitHub, official documentation sites, developer blogs, and community forums. Date: 2026-11-01.
- **Verification:** Cross-referenced star counts, license files, and feature claims across multiple sources. Items with single-source evidence marked UNVERIFIED.
- **Scope:** Focused on WMS-specific capabilities per task specification. ERP features (accounting, HR, CRM) excluded unless directly relevant to inventory management.
- **Limitations:** GitHub star counts are approximate (±10%) due to different tracking timestamps. Last commit dates not verified via direct API calls. Feature assessments based on documentation and README claims — actual implementation depth may vary.
