# Capacity Runner Review Guide

Status: `CAPACITY RUNNER OFFLINE PACKAGE OWNER ACCEPTED / LIVE EXECUTION BLOCKED / CAPACITY EXECUTION NOT AUTHORIZED / PRODUCTION NO-GO`

Owner acceptance covers only the offline orchestration, fail-closed guards, workload skeleton, mock contracts and hardened k6 installer. It does not approve controlled live adapters, final workload or thresholds, an execution environment, paid resources, capacity execution or any production release gate.

The runner implements the reviewed 1/3/6/10 VU development profiles and a separate 2 VU contention profile. Workload percentages, data volume and thresholds remain proposals. Mock success is not capacity evidence.

## Supply chain pin

- Tool: Grafana k6 `v2.1.0`.
- Official asset: `https://github.com/grafana/k6/releases/download/v2.1.0/k6-v2.1.0-windows-amd64.zip`.
- SHA-256: `185CA503EAD8F0348DAA79C002469E5EB324473C39452F29B5F70B1C1B4C8503`.
- Installation must download that exact asset, verify SHA-256 before extraction, and reject an existing binary whose version is not exactly `v2.1.0`. Download/install is a separately reviewed environment-preparation action.

The implementation is `Install-K6Pinned.ps1`. It is never invoked by offline tests and requires an explicit `-Download` switch and absolute destination.

## Execution boundary

`Invoke-CapacityBaseline.ps1 -ValidateOnly` performs offline schema and limit validation only. Live execution additionally requires owner-approved settings, an unexpired UTC window and a controlled-environment adapter. The adapter must independently collect API deployment evidence and SQL ownership evidence. Both must identify the same environment, database, Run ID and opaque binding digest. The API evidence must match the expected deployment commit and environment marker. SQL must verify `DB_NAME()` and the exact ownership marker. Missing or mismatched linkage is `TARGET_VERIFICATION_BLOCKED`; there is no bypass switch.

The checked-in sample intentionally fails closed. Credentials are supplied at execution through an approved secret source and exposed only to the k6 child process; they are never arguments, logs, manifests or results. Each worker receives distinct synthetic documents and quantity budgets. Maker/checker identities are separate and transfer checkers require both warehouse scopes.

## Planned commands

Offline only:

```powershell
powershell.exe -NoProfile -NonInteractive -File tools/ERP.CapacityBaseline/Test-CapacityRunner.ps1
powershell.exe -NoProfile -NonInteractive -File tools/ERP.CapacityBaseline/Invoke-CapacityBaseline.ps1 `
  -ConfigPath <reviewed-local-config.psd1> -ValidateOnly
```

Future live command shape is shown below for review. The current runner deliberately returns `CONTROLLED_LIVE_ADAPTER_NOT_IMPLEMENTED` before loading any adapter; implementing two controlled independent verifiers is a separate owner-reviewed slice.

```powershell
powershell.exe -NoProfile -NonInteractive -File tools/ERP.CapacityBaseline/Invoke-CapacityBaseline.ps1 `
  -ConfigPath <approved-local-config.psd1> -Execute -AdapterPath <controlled-live-adapter.ps1>
```

## Seed, metrics and reconciliation

The live adapter must seed only through authenticated application services and existing state transitions. Direct stock, reservation or ledger writes are forbidden. It writes an exact Run ID resource manifest. Collectors are read-only and report `unavailable` with a reason when a metric cannot be collected; absence is never zero.

Reconciliation must fail for negative OnHand/Reserved, Reserved greater than OnHand, reservation aggregate mismatch, duplicate/missing ledger references, movement from rejected/cancelled drafts, decimal scale errors or aggregate deltas inconsistent with labelled successful commands. Partial redacted evidence is retained on stop. Cleanup is a separate owner-approved operation that revalidates exact resource ownership; wildcard cleanup is forbidden.

## Decisions still required

Owner approval remains required for environment/provider and budget, SQL edition/version, final workload and thresholds, test window, result confirmer, secret source, live adapter, seed manifest and cleanup scope. A short baseline does not close the Capacity gate or prove an SLA.
