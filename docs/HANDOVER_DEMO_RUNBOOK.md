# ERP KHO - Handover and Demo Runbook

## 1. Prerequisites and Safe Local Configuration
To run the ERP KHO system locally, you need:
- .NET 10 SDK
- Node.js (v20+)
- SQL Server LocalDB or a compatible SQL Server instance

### Environment Variables / User Secrets
Do not hardcode secrets or connection strings in configuration files. Configure them locally via .NET User Secrets or Environment Variables.

**Backend Configuration (User Secrets)**
```bash
cd ERP.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<YOUR_LOCAL_DB_CONNECTION_STRING>"
dotnet user-secrets set "JwtSettings:Secret" "<YOUR_32_BYTE_JWT_SECRET>"
```

**Alternatively (Environment Variables)**
```bash
export ConnectionStrings__DefaultConnection="<YOUR_LOCAL_DB_CONNECTION_STRING>"
export JwtSettings__Secret="<YOUR_32_BYTE_JWT_SECRET>"
```

## 2. Startup Order

1. **Backend (API)**: Start the API first. Database migrations are a separate, reviewed DBA/deployment step; application startup does not apply them automatically.
   ```bash
   cd ERP.Api
   dotnet run
   ```
2. **Frontend (React)**: Start the frontend development server.
   ```bash
   cd frontend
   npm run dev
   ```

## 3. Demo Accounts and Roles
For security, passwords are not documented here. Please contact the system administrator to obtain the demo credentials.
- **Admin Role**: Full access to all modules, including system configuration and data management.
- **Manager Role**: Can approve/reject export receipts, view inventory, run reports, and manage stocktakes.
- **Staff Role**: Can create import/export receipts, view product details, and check current stock.
- **Viewer Role**: Read-only access to view reports and inventory stock.

## 4. End-to-End Demo Flows

### 4.1 Login
- Users open the frontend URL and authenticate using their credentials.
- The system issues a JWT token. Invalid secrets will cause backend rejection.

### 4.2 Products & Warehouses
- **Admin/Manager**: Create, update, or delete products and warehouses.
- **Staff/Viewer**: Read product and warehouse information only.

### 4.3 Import Receipts
- **Admin/Manager**: Create a new Import Receipt, select a warehouse, and add product lines and quantities.
- Inventory changes only when an Admin or Manager approves the pending receipt. Creating or canceling a pending receipt does not increment stock.

### 4.4 Export Receipts (Approval / Cancel)
- **Staff**: Create a new Export Receipt. Status begins as "Pending".
- **Admin/Manager/Staff**: Review a pending export receipt and use the available approve or cancel action.
  - If approved, stock is deducted.
  - If canceled, no stock is deducted.

### 4.5 Current Inventory
- All roles can view the Current Inventory screen.
- Users can filter by keyword, warehouse, product, or low stock threshold.
- The system returns real-time calculated stock.

### 4.6 Stocktake
- **Admin/Manager/Staff**: Create a stocktake session, enter physical quantities, and approve the stocktake according to the current controller policy.
- Approval generates adjustment transactions for discrepancies.

### 4.7 Reports
- **Manager/Admin**: Navigate to Reports. Generate inbound/outbound flow summaries over a selected date range.

## 5. Expected Authorization by Role
| Feature | Admin | Manager | Staff | Viewer |
|---------|-------|---------|-------|--------|
| Create Product/Warehouse | Yes | Yes | No | No |
| Create Import Receipt | Yes | Yes | No | No |
| Create/Approve/Cancel Export Receipt | Yes | Yes | Yes | No |
| Manage Stocktake | Yes | Yes | Yes | No |
| View Inventory | Yes | Yes | Yes | Yes |
| View Reports | Yes | Yes | No | Yes |

## 6. Rollback and Troubleshooting
- **Database Rollback**: Stop the deployment and follow the reviewed DBA rollback plan using a verified backup. Do not issue migration downgrade commands against shared data without explicit approval and a tested recovery point.
- **JWT Errors**: If the backend fails to start, verify `JwtSettings:Secret` is at least 32 bytes and not a placeholder.
- **Connection Refused**: Ensure the backend is running before the frontend attempts to communicate via `localhost:8080`.
- **Docker Compose Issues**: Use `docker compose down` to stop local services. Volume deletion is intentionally excluded because it destroys local data and requires separate explicit approval.

## 7. UAT Checklist

| ID | Test Case | Expected Result | Sign-off |
|---|---|---|---|
| UAT-1 | Authenticate with valid credentials | Login successful, token received | PENDING_HUMAN_SIGNOFF |
| UAT-2 | Start Backend without secret | App throws exception at startup | PENDING_HUMAN_SIGNOFF |
| UAT-3 | Create Import Receipt | Inventory stock increments | PENDING_HUMAN_SIGNOFF |
| UAT-4 | Create & Approve Export Receipt | Inventory stock decrements | PENDING_HUMAN_SIGNOFF |
| UAT-5 | Reject Export Receipt | Stock remains unchanged | PENDING_HUMAN_SIGNOFF |
| UAT-6 | View Current Stock with Filters | Correct filtered stock is displayed | PENDING_HUMAN_SIGNOFF |
| UAT-7 | Role-based Authorization | Staff cannot approve export receipts | PENDING_HUMAN_SIGNOFF |

*Approval signatures are pending final human UAT sign-off.*
