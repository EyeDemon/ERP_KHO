# ERP_KHO — Multi-Tenant Warehouse Management System (WMS SaaS)

## 1. System Overview & Tech Stack

`ERP_KHO` is a multi-tenant B2B Warehouse Management System (WMS SaaS) built on Clean Architecture (`Domain` → `Application` → `Infrastructure` → `Api`) with a 9-dimension deterministic inventory ledger engine, role-based access control (RBAC) with warehouse-scoped isolation, and a responsive Vietnamese-first web interface.

- **Backend:** `.NET 10` (`ASP.NET Core Web API`, C#), `Entity Framework Core 10`
- **Database:** `Microsoft SQL Server` (isolated database `ERP_KHO`)
- **Frontend:** `React 19`, `TypeScript`, `Vite`
- **Automated Testing:** `xUnit`, `ERP.SqlIntegrationHarness` (real SQL Server integration suite), `Vitest`, `Testing Library`, `Playwright`

---

## 2. Prerequisites & Database Setup (`ERP_KHO`)

### Prerequisites
- `.NET SDK 10.0+`
- `Node.js 22+` and `npm`
- `SQL Server` or `SQL Server LocalDB` with Windows Integrated Security or a dedicated least-privilege application account (never use `sa`, and never commit secrets into `appsettings*.json`).

### Configure Local Connection String & JWT Signing Key
Use `.NET User Secrets` (recommended) or environment variables:

```powershell
dotnet user-secrets init --project ERP.Api
dotnet user-secrets set --project ERP.Api "ConnectionStrings:DefaultConnection" "Server=localhost;Database=ERP_KHO;Integrated Security=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
dotnet user-secrets set --project ERP.Api "JwtSettings:Secret" "<replace-with-random-secret-at-least-32-bytes>"
```

Or via PowerShell environment variables for the current session:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=ERP_KHO;Integrated Security=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
$env:JwtSettings__Secret = "<replace-with-random-secret-at-least-32-bytes>"
```

### Apply EF Core Migrations

```powershell
dotnet ef database update --project ERP.Infrastructure --startup-project ERP.Api
```

---

## 3. How to Build & Run Backend

```powershell
dotnet restore ERP.slnx
dotnet build ERP.slnx -c Release
dotnet run --project ERP.Api
```

By default in local development, `ERP.Api` listens on `http://localhost:5108` and exposes health checks at `/health/live` and `/health/ready`.

---

## 4. How to Build & Run Frontend

```powershell
npm --prefix frontend ci
npm --prefix frontend run dev
```

For a production bundle verification build:

```powershell
npm --prefix frontend run build
```

---

## 5. How to Run Automated Tests

### UTF-8 & Vietnamese Text Integrity Gate
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-encoding.ps1
```

### Backend Unit, API & SQL Integration Tests
```powershell
dotnet test ERP.slnx -c Release
```

### Frontend Unit & Component Tests (Vitest)
```powershell
npm --prefix frontend test -- --run
```
