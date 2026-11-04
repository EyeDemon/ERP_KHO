using System.Data;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PermissionOperationsService
{
    public async Task<WmsEndpointOutcome> GetTenantsSummaryAsync(CancellationToken ct = default)
    {
        var activeWarehousesCount = await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct);
        var activeUsersCount = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, ct);
        var activeSkusCount = await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct);
        var ledgerTransactionsCount = await _db.InventoryTransactions.AsNoTracking().CountAsync(ct);

        const int maxWarehouses = 20;
        const int maxUsers = 100;
        const int maxSkus = 10000;
        const int maxMonthlyTransactions = 100000;
        var withinQuotas = activeWarehousesCount <= maxWarehouses && activeUsersCount <= maxUsers && activeSkusCount <= maxSkus;

        return WmsEndpointOutcome.Ok(new
        {
            tenantId = _db.CurrentTenantId,
            tenantCode = "TNT-ERP-KHO-VN",
            tenantName = "Hệ thống Kho Vận Doanh Nghiệp (Tenant Chính)",
            planCode = "ENTERPRISE_SAAS",
            planName = "Gói Doanh nghiệp Mở rộng (Enterprise SaaS)",
            isolationMode = "Cách ly đa khách hàng theo TenantId & Phân quyền Kho (Row-Level Isolation)",
            activeWarehousesCount,
            maxWarehouses,
            activeUsersCount,
            maxUsers,
            activeSkusCount,
            maxSkus,
            ledgerTransactionsCount,
            maxMonthlyTransactions,
            withinQuotas,
            quotaStatus = withinQuotas ? "TRONG_HAN_NGACH" : "VUOT_HAN_NGACH",
            quotaStatusLabel = withinQuotas
                ? "Đạt chuẩn hạn ngạch thuê bao — Toàn bộ tài nguyên trong ngưỡng an toàn"
                : "Cảnh báo vượt ngưỡng hạn ngạch thuê bao",
            quotas = new
            {
                activeWarehousesCount,
                maxWarehouses,
                activeUsersCount,
                maxUsers,
                activeSkusCount,
                maxSkus,
                ledgerTransactionsCount,
                maxMonthlyTransactions
            },
            checkedAtUtc = DateTime.UtcNow
        });
    }

    public async Task<WmsEndpointOutcome> VerifyTenantQuotasAsync(
        VerifyTenantQuotasInput? request = null,
        int? actorUserId = null,
        CancellationToken ct = default)
    {
        var activeWarehousesCount = await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct);
        var activeUsersCount = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, ct);
        var activeSkusCount = await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct);
        var ledgerTransactionsCount = await _db.InventoryTransactions.AsNoTracking().CountAsync(ct);

        var maxWarehouses = request?.MaxWarehouses is > 0 ? request.MaxWarehouses.Value : 20;
        var maxUsers = request?.MaxUsers is > 0 ? request.MaxUsers.Value : 100;
        var maxSkus = request?.MaxSkus is > 0 ? request.MaxSkus.Value : 10000;
        const int maxMonthlyTransactions = 100000;
        var withinQuotas = activeWarehousesCount <= maxWarehouses && activeUsersCount <= maxUsers && activeSkusCount <= maxSkus;

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (_currentUser is { IsAuthenticated: true, UserId: > 0 } ? _currentUser.UserId : null);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = effectiveActorId,
            EntityName = "TenantQuotaVerification",
            EntityId = 1,
            Action = "Permissions.VerifyTenantQuotas",
            NewValues = $"WH={activeWarehousesCount}/{maxWarehouses}, Users={activeUsersCount}/{maxUsers}, SKUs={activeSkusCount}/{maxSkus}",
            Reason = "Kiểm tra & xác nhận hạn ngạch thuê bao SaaS đa khách hàng",
            Result = "Success",
            Timestamp = now
        });
        await _db.SaveChangesAsync(ct);

        return WmsEndpointOutcome.Ok(new
        {
            tenantId = _db.CurrentTenantId,
            tenantCode = !string.IsNullOrWhiteSpace(request?.TenantCode) ? request.TenantCode.Trim() : "TNT-ERP-KHO-VN",
            tenantName = "Hệ thống Kho Vận Doanh Nghiệp (Tenant Chính)",
            planCode = "ENTERPRISE_SAAS",
            planName = "Gói Doanh nghiệp Mở rộng (Enterprise SaaS)",
            isolationMode = "Cách ly đa khách hàng theo TenantId & Phân quyền Kho (Row-Level Isolation)",
            activeWarehousesCount,
            maxWarehouses,
            activeUsersCount,
            maxUsers,
            activeSkusCount,
            maxSkus,
            ledgerTransactionsCount,
            maxMonthlyTransactions,
            withinQuotas,
            quotaStatus = withinQuotas ? "TRONG_HAN_NGACH" : "VUOT_HAN_NGACH",
            quotaStatusLabel = withinQuotas
                ? "Đạt chuẩn hạn ngạch thuê bao — Toàn bộ tài nguyên trong ngưỡng an toàn"
                : "Cảnh báo vượt ngưỡng hạn ngạch thuê bao",
            verifiedAtUtc = now
        });
    }

    private static readonly HashSet<string> ReservedSelfRegistrationUsernames = new(StringComparer.OrdinalIgnoreCase)
    {
        "qa_admin", "superadmin", "system", "platform_admin", "qa_billing_admin", "qa_support_admin", "qa_security_auditor", "admin"
    };

    public async Task<WmsEndpointOutcome> ProvisionSaaSTenantAsync(ProvisionTenantInput? request = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var isPublicSelfRegistration = request?.IsPublicSelfRegistration == true;
        var allocateNewTenant = isPublicSelfRegistration || request?.AllocateNewTenant == true;
        var candidateUsername = !string.IsNullOrWhiteSpace(request?.OwnerUsername) ? request.OwnerUsername.Trim() : null;
        if (isPublicSelfRegistration && candidateUsername != null && ReservedSelfRegistrationUsernames.Contains(candidateUsername))
        {
            return WmsEndpointOutcome.BadRequest(new { code = "RESERVED_USERNAME_FORBIDDEN", errorCode = "RESERVED_USERNAME_FORBIDDEN", message = $"Tên đăng nhập '{candidateUsername}' thuộc danh sách tài khoản hệ thống được bảo vệ, không được phép tự đăng ký công khai." });
        }

        var originalTenantId = _db?.CurrentTenantId > 0 ? _db.CurrentTenantId : 1;
        var tenantId = originalTenantId;
        var rawPlanCode = string.IsNullOrWhiteSpace(request?.PlanCode) ? "STARTER" : request.PlanCode.Trim().ToUpperInvariant();
        if (isPublicSelfRegistration) rawPlanCode = rawPlanCode == "FREE_TRIAL" ? "FREE_TRIAL" : "STARTER";
        var planSpec = SaasBillingService.ResolvePlanSpec(rawPlanCode);
        var planCode = !isPublicSelfRegistration && rawPlanCode is "PRO" or "GROWTH" ? rawPlanCode : planSpec.PlanCode;
        var companyName = string.IsNullOrWhiteSpace(request?.CompanyName) ? "Cửa hàng Thương mại Điện tử SME" : request.CompanyName.Trim();

        var effectiveOrderQuota = planSpec.MonthlyOrderQuota;
        var effectiveMaxWarehouses = planSpec.MaxWarehouses;
        var effectiveMaxUsers = planSpec.MaxUsers;
        var effectiveMonthlyPrice = planSpec.MonthlyPriceVnd;
        var effectiveMaxSkus = planSpec.MaxSkus;

        if (_db != null)
        {
            await TenantProvisioningGate.WaitAsync(ct);
            try
            {
                await using var tx = _db.Database.CurrentTransaction is null && _db.Database.IsRelational()
                    ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                    : null;

                if (_db.Database.IsSqlServer() && _db.Database.CurrentTransaction != null)
                {
                    await _db.Database.ExecuteSqlRawAsync("""
                        DECLARE @result int;
                        EXEC @result = sys.sp_getapplock 
                            @Resource = N'ERP_KHO_ProvisionSaaSTenant_GlobalLock', 
                            @LockMode = N'Exclusive', 
                            @LockOwner = N'Transaction', 
                            @LockTimeout = 10000;
                        IF @result < 0
                            THROW 50001, 'Could not acquire global tenant provisioning lock (sp_getapplock failed or timed out).', 1;
                        """, ct);
                }

                var explicitTaxCode = !string.IsNullOrWhiteSpace(request?.TaxCode) ? request.TaxCode.Trim() : null;
                if (isPublicSelfRegistration)
                {
                    var usernameLower = candidateUsername?.ToLowerInvariant();
                    var taxLower = explicitTaxCode?.ToLowerInvariant();
                    var duplicateUser = usernameLower != null
                        && await _db.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.Username.ToLower() == usernameLower, ct);
                    var duplicateTax = taxLower != null
                        && await _db.SaasTenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(t => t.TaxCode != null && t.TaxCode.ToLower() == taxLower, ct);
                    if (duplicateUser || duplicateTax)
                    {
                        return WmsEndpointOutcome.Conflict(new
                        {
                            code = "TENANT_OR_USERNAME_ALREADY_EXISTS",
                            errorCode = "TENANT_OR_USERNAME_ALREADY_EXISTS",
                            message = "Tên đăng nhập hoặc mã số thuế doanh nghiệp đã tồn tại trên hệ thống."
                        });
                    }
                }

                if (allocateNewTenant)
                {
                    var maxSubTenant = await _db.TenantSubscriptions.IgnoreQueryFilters().MaxAsync(s => (int?)s.TenantId, ct) ?? 1;
                    var maxWhTenant = await _db.Warehouses.IgnoreQueryFilters().MaxAsync(w => (int?)w.TenantId, ct) ?? 1;
                    var maxUserTenant = await _db.Users.IgnoreQueryFilters().MaxAsync(u => (int?)u.TenantId, ct) ?? 1;
                    tenantId = Math.Max(2, Math.Max(Math.Max(maxSubTenant, maxWhTenant), maxUserTenant) + 1);

                    if (!isPublicSelfRegistration && candidateUsername != null)
                    {
                        var usernameLower = candidateUsername.ToLowerInvariant();
                        if (await _db.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.TenantId != tenantId && u.Username.ToLower() == usernameLower, ct))
                        {
                            return WmsEndpointOutcome.Conflict(new
                            {
                                code = "TENANT_OR_USERNAME_ALREADY_EXISTS",
                                errorCode = "TENANT_OR_USERNAME_ALREADY_EXISTS",
                                message = "Tên đăng nhập chủ sở hữu đã tồn tại ở không gian thuê bao khác."
                            });
                        }
                    }
                }

                _db.UseTenant(tenantId);

                if (!await _db.SaasPlans.AsNoTracking().AnyAsync(ct))
                {
                    _db.SaasPlans.AddRange(
                        new SaasPlan { PlanCode = "FREE_TRIAL", PlanName = "Gói Dùng thử 14 Ngày — 0 đ/tháng", MonthlyOrderQuota = 300, MaxWarehouses = 1, MaxUsers = 3, MonthlyPriceVnd = 0m, IsActive = true },
                        new SaasPlan { PlanCode = "STARTER", PlanName = "Gói Khởi đầu — 490.000 đ/tháng", MonthlyOrderQuota = 1500, MaxWarehouses = 1, MaxUsers = 5, MonthlyPriceVnd = 490000m, IsActive = true },
                        new SaasPlan { PlanCode = "GROWTH", PlanName = "Gói Tăng trưởng — 1.490.000 đ/tháng", MonthlyOrderQuota = 10000, MaxWarehouses = 3, MaxUsers = 20, MonthlyPriceVnd = 1490000m, IsActive = true },
                        new SaasPlan { PlanCode = "PRO", PlanName = "Gói Tăng trưởng — 1.490.000 đ/tháng", MonthlyOrderQuota = 10000, MaxWarehouses = 3, MaxUsers = 20, MonthlyPriceVnd = 1490000m, IsActive = true },
                        new SaasPlan { PlanCode = "ENTERPRISE", PlanName = "Gói Doanh nghiệp — 3.490.000 đ/tháng", MonthlyOrderQuota = 100000, MaxWarehouses = 20, MaxUsers = 100, MonthlyPriceVnd = 3490000m, IsActive = true }
                    );
                    await _db.SaveChangesAsync(ct);
                }

                var matchedDbPlan = await _db.SaasPlans.AsNoTracking().FirstOrDefaultAsync(p => p.IsActive && p.PlanCode == planCode, ct);
                if (matchedDbPlan != null)
                {
                    effectiveOrderQuota = matchedDbPlan.MonthlyOrderQuota > 0 ? matchedDbPlan.MonthlyOrderQuota : planSpec.MonthlyOrderQuota;
                    effectiveMaxWarehouses = matchedDbPlan.MaxWarehouses > 0 ? matchedDbPlan.MaxWarehouses : planSpec.MaxWarehouses;
                    effectiveMaxUsers = matchedDbPlan.MaxUsers > 0 ? matchedDbPlan.MaxUsers : planSpec.MaxUsers;
                    effectiveMonthlyPrice = matchedDbPlan.MonthlyPriceVnd >= 0m ? matchedDbPlan.MonthlyPriceVnd : planSpec.MonthlyPriceVnd;
                }

                var trackedTenant = await _db.SaasTenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
                if (trackedTenant == null)
                {
                    _db.SaasTenants.Add(new SaasTenant
                    {
                        TenantId = tenantId,
                        TenantCode = tenantId == 1 ? "DEFAULT" : $"TENANT-{tenantId}",
                        CompanyName = companyName,
                        TaxCode = !string.IsNullOrWhiteSpace(request?.TaxCode) ? request.TaxCode.Trim() : null,
                        TaxpayerType = "ENTERPRISE",
                        OwnerEmail = !string.IsNullOrWhiteSpace(request?.OwnerEmail) ? request.OwnerEmail.Trim() : $"owner.t{tenantId}@erpkho.local",
                        LifecycleStatus = "ACTIVE",
                        AllowSupportAccess = true,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                    await _db.SaveChangesAsync(ct);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(request?.CompanyName)) trackedTenant.CompanyName = companyName;
                    if (!string.IsNullOrWhiteSpace(request?.TaxCode)) trackedTenant.TaxCode = request.TaxCode.Trim();
                    if (!string.IsNullOrWhiteSpace(request?.OwnerEmail)) trackedTenant.OwnerEmail = request.OwnerEmail.Trim();
                    trackedTenant.UpdatedAtUtc = now;
                    await _db.SaveChangesAsync(ct);
                }

                var sub = await _db.TenantSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
                if (sub == null)
                {
                    _db.TenantSubscriptions.Add(new TenantSubscription
                    {
                        TenantId = tenantId,
                        PlanCode = planCode,
                        Status = "ACTIVE",
                        MonthlyOrderQuota = effectiveOrderQuota,
                        CurrentMonthOrderCount = 0,
                        MaxWarehouses = effectiveMaxWarehouses,
                        MaxUsers = effectiveMaxUsers,
                        TrialEndsAtUtc = now.AddDays(14),
                        CurrentPeriodStartUtc = now,
                        CurrentPeriodEndUtc = now.AddDays(30),
                        UpdatedAtUtc = now
                    });
                }
                else
                {
                    sub.PlanCode = planCode;
                    sub.Status = "ACTIVE";
                    sub.MonthlyOrderQuota = effectiveOrderQuota;
                    sub.MaxWarehouses = effectiveMaxWarehouses;
                    sub.MaxUsers = effectiveMaxUsers;
                    sub.UpdatedAtUtc = now;
                }
                await _db.SaveChangesAsync(ct);

                var defaultWhCode = tenantId == 1 ? "WH01" : $"WH-T{tenantId}";
                var wh = await _db.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Code == defaultWhCode, ct);
                if (wh == null)
                {
                    wh = new Warehouse
                    {
                        TenantId = tenantId,
                        Code = defaultWhCode,
                        Name = tenantId == 1 ? "Kho Trung tâm (WH01)" : $"Kho Trung tâm ({defaultWhCode})",
                        Address = "Việt Nam",
                        IsActive = true
                    };
                    _db.Warehouses.Add(wh);
                    await _db.SaveChangesAsync(ct);

                    var defaultLocations = new[]
                    {
                        ("STAGE-IN", "Khu Tiếp Nhận Đầu Vào", WarehouseLocationType.Receiving),
                        ("PICK-A01", "Kệ Nhặt Hàng A01", WarehouseLocationType.Storage),
                        ("PACK-01", "Bàn Đóng Gói 01", WarehouseLocationType.Staging),
                        ("STAGE-OUT", "Khu Tập Kết Xuất Kho", WarehouseLocationType.Staging),
                        ("QUARANTINE", "Khu Biệt Trữ Kiểm Định", WarehouseLocationType.Damaged)
                    };
                    foreach (var (code, name, locType) in defaultLocations)
                    {
                        _db.WarehouseLocations.Add(new WarehouseLocation
                        {
                            TenantId = tenantId,
                            WarehouseId = wh.Id,
                            Code = code,
                            Name = name,
                            LocationType = locType,
                            IsActive = true,
                            IsPickable = locType == WarehouseLocationType.Storage,
                            IsReceivable = locType == WarehouseLocationType.Receiving
                        });
                    }
                    await _db.SaveChangesAsync(ct);
                }

                var ownerUsername = !string.IsNullOrWhiteSpace(request?.OwnerUsername)
                    ? request.OwnerUsername.Trim()
                    : $"tenant_{tenantId}_owner";
                if (tenantId > 1 || !string.IsNullOrWhiteSpace(request?.OwnerUsername) || !string.IsNullOrWhiteSpace(request?.OwnerPassword))
                {
                    var initialPassword = !string.IsNullOrWhiteSpace(request?.OwnerPassword) ? request.OwnerPassword : $"Owner@T{tenantId}#2026";
                    var hashedPassword = (_passwordHasher ?? new PasswordHasherService()).HashPassword(initialPassword);
                    var adminRoleId = await _db.Roles.AsNoTracking().Where(r => r.RoleName == "Admin").Select(r => (int?)r.Id).FirstOrDefaultAsync(ct)
                        ?? await _db.Roles.AsNoTracking().Where(r => r.SecurityDomain != "PLATFORM").Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
                    if (!adminRoleId.HasValue)
                    {
                        var createdAdminRole = new Role { RoleName = "Admin", SecurityDomain = "CUSTOMER_TENANT" };
                        _db.Roles.Add(createdAdminRole);
                        await _db.SaveChangesAsync(ct);
                        adminRoleId = createdAdminRole.Id;
                    }

                    var existingOwner = await _db.Users.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Username == ownerUsername, ct);
                    if (existingOwner == null)
                    {
                        var newOwner = new User
                        {
                            TenantId = tenantId,
                            Username = ownerUsername,
                            FullName = $"Chủ shop - {companyName}",
                            Email = !string.IsNullOrWhiteSpace(request?.OwnerEmail) ? request.OwnerEmail.Trim() : $"owner.t{tenantId}@erpkho.local",
                            PasswordHash = hashedPassword,
                            RoleId = adminRoleId.Value,
                            SecurityDomain = "CUSTOMER_TENANT",
                            IsActive = true,
                            CreatedAt = now
                        };
                        _db.Users.Add(newOwner);
                        await _db.SaveChangesAsync(ct);

                        if (wh != null && wh.Id > 0)
                        {
                            _db.UserWarehouses.Add(new UserWarehouse
                            {
                                UserId = newOwner.Id,
                                WarehouseId = wh.Id,
                                CreatedBy = newOwner.Id,
                                CreatedAt = now
                            });
                            await _db.SaveChangesAsync(ct);
                        }
                    }
                    else if (isPublicSelfRegistration || allocateNewTenant)
                    {
                        return WmsEndpointOutcome.Conflict(new
                        {
                            code = "TENANT_OR_USERNAME_ALREADY_EXISTS",
                            errorCode = "TENANT_OR_USERNAME_ALREADY_EXISTS",
                            message = "Tên đăng nhập chủ sở hữu đã tồn tại trên hệ thống."
                        });
                    }
                    else
                    {
                        existingOwner.PasswordHash = hashedPassword;
                        existingOwner.SecurityDomain = "CUSTOMER_TENANT";
                        existingOwner.IsActive = true;
                        await _db.SaveChangesAsync(ct);
                    }
                }

                if (tx != null) await tx.CommitAsync(ct);
            }
            finally
            {
                _db.UseTenant(originalTenantId);
                TenantProvisioningGate.Release();
            }
        }

        var resolvedOwnerUsername = !string.IsNullOrWhiteSpace(request?.OwnerUsername)
            ? request.OwnerUsername.Trim()
            : $"tenant_{tenantId}_owner";

        return WmsEndpointOutcome.Ok(new
        {
            tenantId,
            tenantCode = $"TENANT-{tenantId}",
            ownerUsername = resolvedOwnerUsername,
            companyName,
            taxCode = !string.IsNullOrWhiteSpace(request?.TaxCode) ? request.TaxCode.Trim() : string.Empty,
            planCode,
            planName = planSpec.PlanName,
            monthlyPriceVnd = effectiveMonthlyPrice,
            monthlyOrderQuota = effectiveOrderQuota,
            maxWarehouses = effectiveMaxWarehouses,
            maxUsers = effectiveMaxUsers,
            maxSkus = effectiveMaxSkus,
            defaultWarehouseCode = tenantId == 1 ? "WH01" : $"WH-T{tenantId}",
            defaultBins = new[] { "STAGE-IN", "PICK-A01", "PACK-01", "STAGE-OUT", "QUARANTINE" },
            defaultRoles = new[] { "Chủ shop (Admin)", "Thủ kho (Manager)", "Nhân viên Đóng gói (Staff)", "Kế toán (Accountant)" },
            provisionedAtUtc = now,
            statusLabel = $"Đã tự động khởi tạo không gian thuê bao SaaS cho '{companyName}' trong < 3 giây"
        });
    }
}
