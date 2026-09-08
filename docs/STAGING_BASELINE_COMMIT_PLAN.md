# ERP_KHO Staging Baseline Commit Plan

Status: `COMMIT PREPARATION ONLY / NOT PUSHED / PRODUCTION NO-GO`

## Commit purpose

Create one local staging-candidate commit containing the reviewed health/readiness
runtime changes, their isolated test and the staging preparation documents.
This commit is not a deployment, release approval, migration authorization or
runtime start.

Planned message:

`feat: prepare staging baseline runtime`

## Included files

### Group A - required runtime/source-test changes

- `ERP.Api/Program.cs`
- `ERP.Api/Health/DatabaseReadinessHealthCheck.cs`
- `ERP.Api.Tests/HealthEndpointsTests.cs`
- `Dockerfile`
- `docker-compose.yml`

### Group B - staging preparation documentation

All currently untracked staging-preparation Markdown files under `docs/`,
including the staging manifest, database/operations/UAT plans, solo governance,
execution design, GO decision and freeze records. Existing tracked business
documentation is not re-added or modified by this plan.

## Excluded files

- `.agent/**` operational reports and local evidence;
- `bin/**`, `obj/**`, `coverage/**`, `frontend/dist/**` and other generated output;
- logs, test-result artifacts, binaries and archives;
- credentials, tokens, cookies, connection strings, private keys and secrets;
- unrelated tracked business source or documentation.

No broad `git add .` or `git add -A` is permitted.

## Reason

The runtime health contract and staging operating design need one reviewable
source baseline. Keeping local operational evidence and generated artifacts out
of the commit preserves source safety and avoids treating preparation evidence
as a release artifact.

## Validation required

- Markdown consistency and explicit HOLD/NO-GO status checks.
- UTF-8/encoding check.
- Changed-content secret scan.
- `git diff --check` and `git diff --cached --check`.
- Staged file list audit for exact scope.
- No SQL, migration, Docker runtime, deployment, backup/restore or capacity.

## Decision

`COMMIT PREPARATION ONLY`
