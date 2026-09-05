# ERP_KHO Private GitHub Source Handoff

Date: 2026-09-06

## Technical Milestone

- Approval Queue, Approval History, and document Reject workflows are implemented for the reviewed scope.
- The final F1 concurrent HTTP idempotency and F2 dialog lifecycle/focus test gaps are closed according to `approval-final-owner-re-review-20260906.md`.
- The latest reported baseline is Application/SQL 295/295 PASS, API 144/144 PASS, and frontend 25/25 PASS. These are prior verified results and were not rerun for this source handoff.
- Production remains NO-GO.

## Operational State

- The Approval Safety migration has not been applied to the real `ERP_KHO` database.
- The latest verified database report records `ERP_KHO` as ONLINE/SIMPLE.
- The backup pilot Scheduled Task is Disabled.
- The proposed composite index remains pending representative execution-plan evidence.
- This handoff does not authorize migration, deployment, backup, restore, recovery-model changes, or Scheduled Task changes.

## Build And Test

Prerequisites:

- .NET SDK 10.x.
- Node.js 24.x and npm.
- SQL Server for SQL integration tests, using the repository's isolated integration database harness only.
- Local secrets supplied through environment variables or .NET user-secrets. Do not commit credentials.

Commands:

```powershell
dotnet restore ERP.slnx
dotnet build ERP.slnx --configuration Release --no-restore
dotnet test ERP.Application.Tests/ERP.Application.Tests.csproj --configuration Release --no-build
dotnet test ERP.Api.Tests/ERP.Api.Tests.csproj --configuration Release --no-build
powershell -NoProfile -File scripts/check-encoding.ps1

Set-Location frontend
npm ci
npm test
npm run lint
npm run build
npm audit --audit-level=high
```

SQL integration tests must use databases generated with the allowed `ERP_KHO_Integration_` prefix, ownership marker, run ID, `DB_NAME()` guard, and denylist. They must never fall back to `ERP_KHO`.

## Next Product Slice

The proposed next slice is Approval in-app indicators and Aging. SLA thresholds remain an owner decision. Email and Zalo notifications are not authorized by this handoff. Bulk approval remains out of scope.

## Repository Handoff

- Target repository: `https://github.com/EyeDemon/ERP_KHO`
- Required visibility: Private.
- Handoff branch: `handoff/approval-complete-20260906`.
- Commit message: `chore: hand off reviewed approval workflow source`.
- GitHub access from ChatGPT must be granted separately for this new private repository.
