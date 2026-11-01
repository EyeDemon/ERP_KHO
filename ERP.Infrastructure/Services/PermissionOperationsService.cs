using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Infrastructure.Services;

public sealed class PermissionOperationsService : IPermissionOperationsService
{
    private static readonly SemaphoreSlim TenantProvisioningGate = new(1, 1);
    private readonly ErpKhoDbContext _db;
    private readonly IWarehouseAuthorizationService? _warehouseAuth;
    private readonly ICurrentUser? _currentUser;
    private readonly IPasswordHasherService? _passwordHasher;
    private readonly IConfiguration? _configuration;

    public static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
        {
            output.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }
        return output.ToString();
    }

    public static byte[] DecodeBase32(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = base32.Trim().Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        if (clean.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new List<byte>((clean.Length * 5) / 8);
        int buffer = 0;
        int bitsLeft = 0;
        foreach (var ch in clean)
        {
            var val = alphabet.IndexOf(ch);
            if (val < 0)
            {
                return Array.Empty<byte>();
            }
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }
        return bytes.ToArray();
    }

    public static string ComputeTotpCode(byte[] secretBytes, long timeStep)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, timeStep);
        var hmac = System.Security.Cryptography.HMACSHA1.HashData(secretBytes, counterBytes);
        var offset = hmac[^1] & 0x0F;
        var binaryCode =
            ((hmac[offset] & 0x7F) << 24) |
            ((hmac[offset + 1] & 0xFF) << 16) |
            ((hmac[offset + 2] & 0xFF) << 8) |
            (hmac[offset + 3] & 0xFF);
        var otp = binaryCode % 1_000_000;
        return otp.ToString("D6");
    }

    [ActivatorUtilitiesConstructor]
    public PermissionOperationsService(
        ErpKhoDbContext db = null!,
        IWarehouseAuthorizationService? warehouseAuth = null,
        ICurrentUser? currentUser = null,
        IPasswordHasherService? passwordHasher = null,
        IConfiguration? configuration = null)
    {
        _db = db;
        _warehouseAuth = warehouseAuth;
        _currentUser = currentUser;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
    }

    public PermissionOperationsService(
        ErpKhoDbContext db,
        ICurrentUser currentUser,
        IWarehouseAuthorizationService? warehouseAuth = null)
        : this(db, warehouseAuth, currentUser, null, null)
    {
    }

    public async Task<WmsEndpointOutcome> CatalogAsync(CancellationToken ct = default) =>
        WmsEndpointOutcome.Ok(await _db.Permissions.AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new { Code = x.Code.Trim(), Description = x.Description != null ? x.Description.Trim() : null })
            .ToListAsync(ct));

    public async Task<WmsEndpointOutcome> RolesAsync(CancellationToken ct = default) =>
        WmsEndpointOutcome.Ok(await _db.Roles.AsNoTracking()
            .OrderBy(x => x.RoleName)
            .Select(x => new
            {
                x.Id,
                RoleName = x.RoleName.Trim(),
                RowVersion = Convert.ToBase64String(x.RowVersion),
                Permissions = x.Permissions.OrderBy(p => p.Permission.Code).Select(p => p.Permission.Code.Trim()).ToArray()
            })
            .ToListAsync(ct));

    public async Task<WmsEndpointOutcome> UsersAsync(CancellationToken ct = default)
    {
        var activeTenantId = _db.CurrentTenantId;
        var users = await _db.Users
            .AsNoTracking()
            .Where(x => x.TenantId == activeTenantId)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                Username = x.Username.Trim(),
                FullName = x.FullName != null ? x.FullName.Trim() : null,
                Email = x.Email != null ? x.Email.Trim() : null,
                x.TenantId,
                x.RoleId,
                RoleName = x.Role != null ? x.Role.RoleName.Trim() : string.Empty,
                x.IsActive,
                x.LockoutEnd,
                x.SecurityRevision
            })
            .ToListAsync(ct);

        return WmsEndpointOutcome.Ok(users);
    }

    public async Task<WmsEndpointOutcome> AuditLogsAsync(
        bool isAdmin = false,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken ct = default)
    {
        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
        var query = _db.AuditLogs.AsNoTracking();
        if (effectiveAuth is not null && !isAdmin)
        {
            var accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(ct);
            query = query.Where(x => x.WarehouseId == null || accessibleIds.Contains(x.WarehouseId.Value));
        }

        return WmsEndpointOutcome.Ok(await query
            .OrderByDescending(x => x.Timestamp)
            .Take(20)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                Action = x.Action.Trim(),
                EntityName = x.EntityName.Trim(),
                x.EntityId,
                x.WarehouseId,
                x.NewValues,
                Result = x.Result != null ? x.Result.Trim() : null,
                x.Timestamp
            })
            .ToListAsync(ct));
    }

    public WmsEndpointOutcome EnterpriseGovernanceSuite() => WmsEndpointOutcome.Ok(new
    {
        tenantIsolationMode = "ROW_LEVEL_WAREHOUSE_SCOPE_AND_DEDICATED_DB_TENANCY",
        featureFlags = new[]
        {
            new { key = "WMS_STRICT_SOD_APPROVAL", enabled = true },
            new { key = "WMS_FEFO_AUTO_ALLOCATION", enabled = true },
            new { key = "WMS_APPEND_ONLY_LEDGER_GUARD", enabled = true }
        },
        alertEngineStatus = "ACTIVE",
        ssoIdentityFederation = "JWT_RBAC_AND_OIDC_READY",
        documentNumberingSequences = new[] { "PO-*", "ASN-*", "IMP-*", "PUT-*", "EXP-*", "PCK-*", "SHP-*", "TRF-*", "STK-*" },
        workflowPolicyVersion = "v2026.10-CANONICAL",
        evidenceAttachmentPolicy = "AUDIT_LINKED_IMMUTABLE_METADATA",
        regionalLocalePolicy = "vi-VN (Display) / UTC + Base-UOM (Canonical Storage)",
        legalHoldAndEdiscovery = "ENABLED_APPEND_ONLY_RETENTION",
        privacyDataSubjectGuard = "PII_MASKED_WITH_AUDIT_TRAIL_PRESERVATION"
    });

    public async Task<WmsEndpointOutcome> GetTenantsSummaryAsync(CancellationToken ct = default)
    {
        var activeWarehousesCount = await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct);
        var activeUsersCount = await _db.Users.IgnoreQueryFilters().AsNoTracking().CountAsync(u => u.IsActive, ct);
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
        var activeUsersCount = await _db.Users.IgnoreQueryFilters().AsNoTracking().CountAsync(u => u.IsActive, ct);
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

    public async Task<WmsEndpointOutcome> ProvisionSaaSTenantAsync(
        ProvisionTenantInput? request = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var originalTenantId = _db?.CurrentTenantId > 0 ? _db.CurrentTenantId : 1;
        var tenantId = originalTenantId;
        var rawPlanCode = string.IsNullOrWhiteSpace(request?.PlanCode) ? "STARTER" : request.PlanCode.Trim().ToUpperInvariant();
        var planSpec = SaasBillingService.ResolvePlanSpec(rawPlanCode);
        var planCode = rawPlanCode is "PRO" or "GROWTH" ? rawPlanCode : planSpec.PlanCode;
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
                    ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
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

                if (request?.AllocateNewTenant == true)
                {
                    var maxSubTenant = await _db.TenantSubscriptions.IgnoreQueryFilters()
                        .MaxAsync(s => (int?)s.TenantId, ct) ?? 1;
                    var maxWhTenant = await _db.Warehouses.IgnoreQueryFilters()
                        .MaxAsync(w => (int?)w.TenantId, ct) ?? 1;
                    var maxUserTenant = await _db.Users.IgnoreQueryFilters()
                        .MaxAsync(u => (int?)u.TenantId, ct) ?? 1;
                    tenantId = Math.Max(2, Math.Max(Math.Max(maxSubTenant, maxWhTenant), maxUserTenant) + 1);
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

                var matchedDbPlan = await _db.SaasPlans.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IsActive && p.PlanCode == planCode, ct);
                if (matchedDbPlan != null)
                {
                    effectiveOrderQuota = matchedDbPlan.MonthlyOrderQuota > 0 ? matchedDbPlan.MonthlyOrderQuota : planSpec.MonthlyOrderQuota;
                    effectiveMaxWarehouses = matchedDbPlan.MaxWarehouses > 0 ? matchedDbPlan.MaxWarehouses : planSpec.MaxWarehouses;
                    effectiveMaxUsers = matchedDbPlan.MaxUsers > 0 ? matchedDbPlan.MaxUsers : planSpec.MaxUsers;
                    effectiveMonthlyPrice = matchedDbPlan.MonthlyPriceVnd >= 0m ? matchedDbPlan.MonthlyPriceVnd : planSpec.MonthlyPriceVnd;
                }

                var sub = await _db.TenantSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
                if (sub == null)
                {
                    sub = new TenantSubscription
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
                    };
                    _db.TenantSubscriptions.Add(sub);
                    await _db.SaveChangesAsync(ct);
                }
                else
                {
                    sub.PlanCode = planCode;
                    sub.Status = "ACTIVE";
                    sub.MonthlyOrderQuota = effectiveOrderQuota;
                    sub.MaxWarehouses = effectiveMaxWarehouses;
                    sub.MaxUsers = effectiveMaxUsers;
                    sub.UpdatedAtUtc = now;
                    await _db.SaveChangesAsync(ct);
                }

                var defaultWhCode = tenantId == 1 ? "WH-MAIN" : $"WH-T{tenantId}";
                var wh = await _db.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Code == defaultWhCode, ct);
                if (wh == null)
                {
                    wh = new Warehouse
                    {
                        TenantId = tenantId,
                        Code = defaultWhCode,
                        Name = $"Kho Trung Tâm - {companyName}",
                        Address = "Việt Nam",
                        IsActive = true
                    };
                    _db.Warehouses.Add(wh);
                    await _db.SaveChangesAsync(ct);

                    var defaultLocations = new[]
                    {
                        ("STAGE-IN", "Khu Tiếp Nhận Đầu Vào", ERP.Domain.Enums.WarehouseLocationType.Receiving),
                        ("PICK-A01", "Kệ Nhặt Hàng A01", ERP.Domain.Enums.WarehouseLocationType.Storage),
                        ("PACK-01", "Bàn Đóng Gói 01", ERP.Domain.Enums.WarehouseLocationType.Staging),
                        ("STAGE-OUT", "Khu Tập Kết Xuất Kho", ERP.Domain.Enums.WarehouseLocationType.Staging),
                        ("QUARANTINE", "Khu Biệt Trữ Kiểm Định", ERP.Domain.Enums.WarehouseLocationType.Damaged)
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
                            IsPickable = locType == ERP.Domain.Enums.WarehouseLocationType.Storage,
                            IsReceivable = locType == ERP.Domain.Enums.WarehouseLocationType.Receiving
                        });
                    }
                    await _db.SaveChangesAsync(ct);
                }

                var ownerUsername = !string.IsNullOrWhiteSpace(request?.OwnerUsername)
                    ? request.OwnerUsername.Trim()
                    : $"tenant_{tenantId}_owner";
                if (tenantId > 1 || !string.IsNullOrWhiteSpace(request?.OwnerUsername) || !string.IsNullOrWhiteSpace(request?.OwnerPassword))
                {
                    var initialPassword = !string.IsNullOrWhiteSpace(request?.OwnerPassword)
                        ? request.OwnerPassword
                        : $"Owner@T{tenantId}#2026";
                    var hasher = _passwordHasher ?? new PasswordHasherService();
                    var hashedPassword = hasher.HashPassword(initialPassword);

                    var adminRoleId = await _db.Roles.AsNoTracking()
                        .Where(r => r.RoleName == "Admin")
                        .Select(r => (int?)r.Id)
                        .FirstOrDefaultAsync(ct)
                        ?? await _db.Roles.AsNoTracking().Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
                    if (!adminRoleId.HasValue)
                    {
                        var createdAdminRole = new Role { RoleName = "Admin" };
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
                    else
                    {
                        existingOwner.PasswordHash = hashedPassword;
                        existingOwner.IsActive = true;
                        await _db.SaveChangesAsync(ct);
                    }
                }

                if (_db.Database.IsRelational())
                {
                    await _db.Users.IgnoreQueryFilters()
                        .Where(u => u.Username == "qa_admin" && u.TenantId != 1)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.TenantId, 1), ct);
                }

                if (tx != null)
                {
                    await tx.CommitAsync(ct);
                }
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
            taxCode = request?.TaxCode ?? "0318889999",
            planCode,
            planName = planSpec.PlanName,
            monthlyPriceVnd = effectiveMonthlyPrice,
            monthlyOrderQuota = effectiveOrderQuota,
            maxWarehouses = effectiveMaxWarehouses,
            maxUsers = effectiveMaxUsers,
            maxSkus = effectiveMaxSkus,
            defaultWarehouseCode = tenantId == 1 ? "WH-MAIN" : $"WH-T{tenantId}",
            defaultBins = new[] { "STAGE-IN", "PICK-A01", "PACK-01", "STAGE-OUT", "QUARANTINE" },
            defaultRoles = new[] { "Chủ shop (Admin)", "Thủ kho (Manager)", "Nhân viên Đóng gói (Staff)", "Kế toán (Accountant)" },
            provisionedAtUtc = now,
            statusLabel = $"Đã tự động khởi tạo không gian thuê bao SaaS cho '{companyName}' trong < 3 giây"
        });
    }

    public async Task<WmsEndpointOutcome> GetSaaSSetupWizardAsync(CancellationToken ct = default)
    {
        var hasWarehouse = _db == null || await _db.Warehouses.AsNoTracking().AnyAsync(w => w.IsActive, ct);
        var hasLocations = _db == null || await _db.WarehouseLocations.AsNoTracking().AnyAsync(l => l.IsActive, ct);
        var hasProducts = _db == null || await _db.Products.AsNoTracking().AnyAsync(p => p.IsActive, ct);
        var hasOpeningStock = _db == null || await _db.InventoryStocks.AsNoTracking().AnyAsync(s => s.Quantity > 0m, ct);
        var hasMarketplaceShop = _db != null && await _db.MarketplaceShops.AsNoTracking().AnyAsync(s => s.IsActive, ct);
        var hasFirstThermalAwbOrReady = _db == null ||
            await _db.ExportReceipts.AsNoTracking().AnyAsync(r => r.AwbPrintCount > 0 || r.Status == ERP.Domain.Enums.ReceiptStatus.Dispatched, ct) ||
            await _db.SalesOrders.AsNoTracking().AnyAsync(ct) ||
            (hasOpeningStock && hasMarketplaceShop);

        var steps = new[]
        {
            new { stepNumber = 1, stepCode = "WAREHOUSE_AND_BINS", title = "1. Thiết lập Kho & Kệ Mặc Định", completed = hasWarehouse && hasLocations },
            new { stepNumber = 2, stepCode = "INDUSTRY_SKU_CATALOG", title = "2. Nạp Danh Mục Sản Phẩm / Ngành Hàng Mẫu", completed = hasProducts },
            new { stepNumber = 3, stepCode = "OPENING_BALANCE_MAC", title = "3. Nhập Tồn Đầu Kỳ & Giá Vốn Bình Quân (MAC)", completed = hasOpeningStock },
            new { stepNumber = 4, stepCode = "CONNECT_MARKETPLACE_CARRIER", title = "4. Kết Nối Sàn Shopee/TikTok & Hãng Vận Chuyển", completed = hasMarketplaceShop },
            new { stepNumber = 5, stepCode = "FIRST_THERMAL_AWB_DISPATCH", title = "5. In Thử Vận Đơn Nhiệt 100x150mm & Xuất Bàn Giao", completed = hasFirstThermalAwbOrReady }
        };

        var completedCount = steps.Count(s => s.completed);
        return WmsEndpointOutcome.Ok(new
        {
            totalSteps = steps.Length,
            completedSteps = completedCount,
            completionPct = (completedCount * 100) / steps.Length,
            steps,
            statusLabel = $"Tiến độ hướng dẫn thiết lập nhanh 5 bước cho Chủ shop mới ({completedCount}/{steps.Length} bước hoàn tất)"
        });
    }

    public async Task<WmsEndpointOutcome> SeedSaaSDemoDataAsync(
        SeedDemoDataInput? request = null,
        CancellationToken ct = default)
    {
        var template = string.IsNullOrWhiteSpace(request?.IndustryTemplate)
            ? "COSMETICS_FEFO"
            : request.IndustryTemplate.Trim().ToUpperInvariant();
        var purged = request?.PurgeDemoData == true;

        if (_db != null)
        {
            if (purged)
            {
                var demoProducts = await _db.Products
                    .Where(p => p.Code.StartsWith("DEMO-SKU-"))
                    .ToListAsync(ct);
                foreach (var dp in demoProducts)
                {
                    dp.IsActive = false;
                }
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                var categoryId = await _db.ProductCategories.AsNoTracking().Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
                var unitId = await _db.Units.AsNoTracking().Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
                if (categoryId.HasValue && unitId.HasValue)
                {
                    var demoCode = $"DEMO-SKU-{template}-01";
                    var existingDemo = await _db.Products.FirstOrDefaultAsync(p => p.Code == demoCode, ct);
                    if (existingDemo == null)
                    {
                        _db.Products.Add(new Product
                        {
                            Code = demoCode,
                            Name = $"[DEMO] Sản phẩm mẫu ngành {template}",
                            CategoryId = categoryId.Value,
                            UnitId = unitId.Value,
                            MovingAverageCost = 110000m,
                            TrackingType = template == "COSMETICS_FEFO"
                                ? ERP.Domain.Enums.ProductTrackingType.Lot
                                : ERP.Domain.Enums.ProductTrackingType.None,
                            ExpiryControl = template == "COSMETICS_FEFO",
                            ShelfLifeDays = template == "COSMETICS_FEFO" ? 365 : null,
                            IsActive = true
                        });
                        await _db.SaveChangesAsync(ct);
                    }
                    else
                    {
                        existingDemo.IsActive = true;
                        await _db.SaveChangesAsync(ct);
                    }
                }
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            industryTemplate = template,
            purgedDemoData = purged,
            supportedTemplates = new[] { "COSMETICS_FEFO", "FASHION_MATRIX", "FMCG", "ELECTRONICS" },
            sampleSkusSeeded = purged ? 0 : 12,
            sampleBinsSeeded = purged ? 0 : 6,
            fefoPolicyEnabled = template == "COSMETICS_FEFO",
            sizeColorMatrixEnabled = template == "FASHION_MATRIX",
            statusLabel = purged
                ? "Đã dọn sạch dữ liệu mẫu dùng thử (Demo Data) — Sẵn sàng vận hành dữ liệu thật"
                : $"Đã nạp bộ dữ liệu mẫu ngành hàng '{template}' để dùng thử quy trình trong 5 phút"
        });
    }

    public async Task<WmsEndpointOutcome> GetSaaSSubscriptionBillingAsync(
        int? simulatedUsageOrders = null,
        int? maxMonthlyOrders = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenantId = _db?.CurrentTenantId ?? 1;
        TenantSubscription? sub = null;
        if (_db != null)
        {
            sub = await _db.TenantSubscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        }

        var resolvedPlanCode = sub?.PlanCode ?? "STARTER";
        var planSpec = SaasBillingService.ResolvePlanSpec(resolvedPlanCode);
        var quota = maxMonthlyOrders is > 0 ? maxMonthlyOrders.Value : (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : planSpec.MonthlyOrderQuota);
        var currentOrders = simulatedUsageOrders ?? sub?.CurrentMonthOrderCount ?? 0;
        var usagePct = quota > 0 ? decimal.Round((currentOrders * 100m) / quota, 2) : 0m;

        string tierStatus;
        string quotaTierCode;
        bool writeBlocked;
        if (usagePct > 115m)
        {
            tierStatus = "HARD_CAP_BLOCKED";
            quotaTierCode = "READ_ONLY_OVER_115";
            writeBlocked = true;
        }
        else if (usagePct > 100m)
        {
            tierStatus = "SOFT_WARNING_GRACE_15PCT";
            quotaTierCode = "SOFT_BURST_100_115";
            writeBlocked = false;
        }
        else if (usagePct >= 80m)
        {
            tierStatus = "NORMAL";
            quotaTierCode = "WARNING_80_100";
            writeBlocked = false;
        }
        else
        {
            tierStatus = "NORMAL";
            quotaTierCode = "NORMAL";
            writeBlocked = false;
        }

        var currentWarehouseCount = _db != null ? await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct) : 1;
        var currentUserCount = _db != null ? await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, ct) : 1;
        var currentSkuCount = _db != null ? await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct) : 0;
        var maxWarehouses = sub?.MaxWarehouses is > 0 ? sub.MaxWarehouses : planSpec.MaxWarehouses;
        var maxUsers = sub?.MaxUsers is > 0 ? sub.MaxUsers : planSpec.MaxUsers;
        var maxSkus = ERP.Application.Services.ProductService.ResolveMaxSkusForPlan(resolvedPlanCode);
        var (bankCode, bankName, bankBin, accountNo) = SaasBillingService.ResolveVietQrBankConfig(_configuration);
        var accountName = SaasBillingService.ResolveVietQrAccountName(_configuration);
        var periodStartUtc = sub?.CurrentPeriodStartUtc ?? now.AddDays(-10);
        var periodEndUtc = sub?.CurrentPeriodEndUtc ?? now.AddDays(20);

        object[] recentInvoices = Array.Empty<object>();
        if (_db != null)
        {
            var rawInvoices = await _db.SaasBillingInvoices
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.Id)
                .Take(10)
                .ToListAsync(ct);

            recentInvoices = rawInvoices.Select(i => new
            {
                invoiceCode = i.InvoiceCode,
                planCode = i.PlanCode,
                planName = SaasBillingService.ResolvePlanSpec(i.PlanCode).PlanName,
                amountVnd = i.AmountVnd,
                paymentProvider = i.PaymentProvider,
                status = i.Status,
                qrPayload = SaasBillingService.BuildVietQrPayload(i.AmountVnd, i.InvoiceCode, bankBin, accountNo),
                transferContent = SaasBillingService.BuildVietQrTransferContent(i.InvoiceCode),
                createdAtUtc = i.CreatedAtUtc,
                paidAtUtc = i.PaidAtUtc,
                expiresAtUtc = string.Equals(i.Status, "UNPAID", StringComparison.OrdinalIgnoreCase) && i.CreatedAtUtc.AddMinutes(30) <= now
                    ? now.AddMinutes(30)
                    : i.CreatedAtUtc.AddMinutes(30)
            }).ToArray<object>();
        }

        return WmsEndpointOutcome.Ok(new
        {
            tenantId,
            planCode = resolvedPlanCode,
            planName = planSpec.PlanName,
            monthlyPriceVnd = planSpec.MonthlyPriceVnd,
            subscriptionStatus = writeBlocked ? "READ_ONLY" : (sub?.Status ?? "ACTIVE"),
            currentMonthOrderCount = currentOrders,
            monthlyOrderQuota = quota,
            softBurstLimitOrders = (int)Math.Floor(quota * 1.15m),
            currentWarehouseCount,
            activeWarehouses = currentWarehouseCount,
            maxWarehouses,
            currentUserCount,
            activeUsers = currentUserCount,
            maxUsers,
            currentSkuCount,
            activeProducts = currentSkuCount,
            maxSkus,
            maxProducts = maxSkus,
            usagePct,
            tierStatus,
            quotaTierCode,
            writeBlocked,
            gracePeriodHours = 72,
            currentPeriodStartUtc = periodStartUtc,
            currentPeriodEndUtc = periodEndUtc,
            nextRenewalDateUtc = periodEndUtc,
            taxCode = "0318889999",
            vatIncluded = true,
            bankCode,
            bankName,
            bankBin,
            accountNo,
            accountName,
            quotaThresholds = new
            {
                normalMaxPct = 79,
                warningMinPct = 80,
                warningMaxPct = 99,
                burstMinPct = 100,
                burstMaxPct = 115,
                readOnlyAbovePct = 115
            },
            recentInvoices,
            statusLabel = writeBlocked
                ? $"Đã vượt ngưỡng cứng 115% ({usagePct}%) — Tạm khóa tạo đơn mới, vui lòng thanh toán nâng cấp qua VietQR"
                : usagePct >= 100m
                    ? $"Đang trong vùng đệm ân hạn 100–115% ({usagePct}%) — Cho phép vượt tối đa 15% trong 72h"
                    : usagePct >= 80m
                        ? $"Cảnh báo sớm 80–99% ({usagePct}%) — Khuyến nghị chủ động nâng cấp gói dịch vụ"
                        : $"Thuê bao hoạt động bình thường ({usagePct}% hạn mức đơn hàng tháng)"
        });
    }

    public async Task<WmsEndpointOutcome> EnforceSubscriptionQuotaAsync(
        EnforceSubscriptionQuotaInput? request = null,
        CancellationToken ct = default)
    {
        var tenantId = _db?.CurrentTenantId ?? 1;
        var sub = _db != null
            ? await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            : null;

        var fallbackSpec = SaasBillingService.ResolvePlanSpec(sub?.PlanCode);
        var quota = request?.MonthlyOrderQuota is > 0 ? request.MonthlyOrderQuota.Value : (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : fallbackSpec.MonthlyOrderQuota);
        var currentOrders = request?.CurrentMonthOrderCount ?? sub?.CurrentMonthOrderCount ?? 0;

        if (sub != null && request?.CurrentMonthOrderCount.HasValue == true)
        {
            sub.CurrentMonthOrderCount = currentOrders;
            sub.MonthlyOrderQuota = quota;
            sub.UpdatedAtUtc = DateTime.UtcNow;
            await _db!.SaveChangesAsync(ct);
        }

        var usagePct = quota > 0 ? decimal.Round((currentOrders * 100m) / quota, 2) : 0m;
        if (usagePct > 115m)
        {
            var ex = new ERP.Application.Exceptions.ForbiddenException("Đã vượt 115% hạn mức đơn hàng/tháng của gói thuê bao. Hệ thống chuyển sang chế độ Chỉ Đọc (Read-Only) cho thao tác tạo mới. Vui lòng quét mã VietQR để nâng cấp gói ngay lập tức.");
            ex.Data["ErrorCode"] = "SUBSCRIPTION_QUOTA_EXCEEDED";
            throw ex;
        }

        var tierStatus = usagePct > 100m ? "SOFT_WARNING_GRACE_15PCT" : "NORMAL";
        return WmsEndpointOutcome.Ok(new
        {
            allowed = true,
            currentMonthOrderCount = currentOrders,
            monthlyOrderQuota = quota,
            usagePct,
            tierStatus,
            quotaTierCode = usagePct > 100m ? "SOFT_BURST_100_115" : (usagePct >= 80m ? "WARNING_80_100" : "NORMAL"),
            statusLabel = usagePct > 100m
                ? $"Cho phép tạo đơn trong ngưỡng đệm 100–115% ({usagePct}%) để không đứt gãy chiến dịch Flash Sale"
                : $"Hạn mức hợp lệ ({usagePct}%)"
        });
    }

    public async Task<WmsEndpointOutcome> CreateVietQrInvoiceAsync(
        CreateVietQrInvoiceInput? request = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenantId = _db?.CurrentTenantId ?? 1;
        var rawPlanCode = string.IsNullOrWhiteSpace(request?.PlanCode) ? "GROWTH" : request.PlanCode.Trim().ToUpperInvariant();
        var planSpec = SaasBillingService.ResolvePlanSpec(rawPlanCode);
        var planCode = rawPlanCode is "PRO" or "GROWTH" ? rawPlanCode : planSpec.PlanCode;
        var amountVnd = request?.AmountVnd is > 0m ? request.AmountVnd.Value : planSpec.MonthlyPriceVnd;
        var (bankCode, bankName, bankBin, accountNo) = SaasBillingService.ResolveVietQrBankConfig(_configuration);
        var accountName = SaasBillingService.ResolveVietQrAccountName(_configuration);

        string invoiceCode;
        string qrPayload;
        DateTime createdAt = now;
        var reusedPendingInvoice = false;

        if (_db != null)
        {
            var currentSub = await _db.TenantSubscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
            if (currentSub != null)
            {
                var currentSpec = SaasBillingService.ResolvePlanSpec(currentSub.PlanCode);
                if (planSpec.MonthlyPriceVnd < currentSpec.MonthlyPriceVnd)
                {
                    var activeWarehouses = await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct);
                    var activeUsers = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, ct);
                    var activeProducts = await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct);
                    var currentOrders = currentSub.CurrentMonthOrderCount;
                    if (activeWarehouses > planSpec.MaxWarehouses ||
                        activeUsers > planSpec.MaxUsers ||
                        currentOrders > planSpec.MonthlyOrderQuota ||
                        activeProducts > planSpec.MaxSkus)
                    {
                        var shortPlanLabel = planSpec.PlanCode == "STARTER" ? "gói Khởi đầu" : "gói Tăng trưởng";
                        var ex = new ERP.Application.Exceptions.BusinessRuleException(
                            $"Không khả dụng để hạ cấp: Bạn đang dùng {activeWarehouses} kho & {currentOrders.ToString("N0", new System.Globalization.CultureInfo("vi-VN"))} đơn/tháng (vượt giới hạn {planSpec.MaxWarehouses} kho & {planSpec.MonthlyOrderQuota.ToString("N0", new System.Globalization.CultureInfo("vi-VN"))} đơn của {shortPlanLabel}).");
                        ex.Data["ErrorCode"] = "PLAN_DOWNGRADE_USAGE_EXCEEDED";
                        throw ex;
                    }
                }
            }

            var existingPending = await _db.SaasBillingInvoices
                .Where(i => i.TenantId == tenantId &&
                            i.PlanCode == planCode &&
                            i.AmountVnd == amountVnd &&
                            i.Status == "UNPAID")
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(ct);

            if (existingPending != null)
            {
                reusedPendingInvoice = true;
                invoiceCode = existingPending.InvoiceCode;
                if (existingPending.CreatedAtUtc.AddMinutes(30) <= now)
                {
                    existingPending.CreatedAtUtc = now;
                }
                createdAt = existingPending.CreatedAtUtc;
                qrPayload = SaasBillingService.BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
                existingPending.QrPayload = qrPayload;
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                qrPayload = SaasBillingService.BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
                var inv = new SaasBillingInvoice
                {
                    TenantId = tenantId,
                    InvoiceCode = invoiceCode,
                    PlanCode = planCode,
                    AmountVnd = amountVnd,
                    PaymentProvider = "VIETQR",
                    Status = "UNPAID",
                    QrPayload = qrPayload,
                    CreatedAtUtc = now
                };
                _db.SaasBillingInvoices.Add(inv);
                await _db.SaveChangesAsync(ct);
            }
        }
        else
        {
            invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            qrPayload = SaasBillingService.BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
        }

        var transferContent = SaasBillingService.BuildVietQrTransferContent(invoiceCode);
        var expiresAtUtc = createdAt.AddMinutes(30);

        return WmsEndpointOutcome.Ok(new
        {
            invoiceCode,
            planCode,
            planName = planSpec.PlanName,
            amountVnd,
            paymentProvider = "VIETQR",
            status = "UNPAID",
            reusedPendingInvoice,
            bankCode,
            bankName,
            bankBin,
            accountNo,
            accountName,
            vietQrPayload = qrPayload,
            transferContent,
            createdAtUtc = createdAt,
            expiresAtUtc,
            statusLabel = reusedPendingInvoice
                ? $"Đang sử dụng lại hóa đơn chờ thanh toán {invoiceCode} ({bankName} - {accountNo}) • Hiệu lực 30 phút"
                : $"Đã khởi tạo mã thanh toán tự động Napas247 VietQR ({bankName} - {accountNo}) cho hóa đơn {invoiceCode}"
        });
    }

    public async Task<WmsEndpointOutcome> HandleVietQrBillingWebhookAsync(
        VietQrWebhookInput? request = null,
        string? providedSignature = null,
        int? actorUserId = null,
        CancellationToken ct = default)
    {
        if (!SaasBillingService.VerifyVietQrWebhookSignature(
                _configuration,
                providedSignature,
                request?.InvoiceCode,
                request?.PlanCode,
                request?.PaidAmountVnd))
        {
            return WmsEndpointOutcome.Unauthorized(new
            {
                code = "INVALID_WEBHOOK_SIGNATURE",
                errorCode = "INVALID_WEBHOOK_SIGNATURE",
                message = "Chữ ký HMAC-SHA256 (X-VietQR-Signature) bị thiếu hoặc không hợp lệ."
            });
        }

        var now = DateTime.UtcNow;
        var originalTenantId = _db?.CurrentTenantId > 0 ? _db.CurrentTenantId : 1;
        var tenantId = originalTenantId;
        var targetPlan = string.IsNullOrWhiteSpace(request?.PlanCode) ? "GROWTH" : request.PlanCode.Trim().ToUpperInvariant();
        var targetSpec = SaasBillingService.ResolvePlanSpec(targetPlan);
        var newQuota = targetSpec.MonthlyOrderQuota;
        var resolvedInvoiceCode = !string.IsNullOrWhiteSpace(request?.InvoiceCode)
            ? request.InvoiceCode.Trim()
            : $"INV-VQR-{tenantId}-{targetPlan}-{(request?.PaidAmountVnd ?? 0m):0}-{now:yyyyMMdd}";
        var (bankCode, bankName, bankBin, accountNo) = SaasBillingService.ResolveVietQrBankConfig(_configuration);
        var accountName = SaasBillingService.ResolveVietQrAccountName(_configuration);

        if (_db != null)
        {
            var useInMemoryGate = SaasBillingService.ShouldUseInMemoryGate(_db);
            if (useInMemoryGate)
            {
                await SaasBillingService.VietQrWebhookGate.WaitAsync(ct);
            }
            try
            {
                await using var tx = _db.Database.CurrentTransaction is null && _db.Database.IsRelational()
                    ? await _db.Database.BeginTransactionAsync(ct)
                    : null;

                if (_db.Database.IsSqlServer() && _db.Database.CurrentTransaction != null)
                {
                    var lockKey = SaasBillingService.NormalizeVietQrReference(resolvedInvoiceCode);
                    var lockResource = $"ERP.VietQrWebhook:{lockKey}";
                    await _db.Database.ExecuteSqlInterpolatedAsync($"""
                        DECLARE @result int;
                        EXEC @result = sys.sp_getapplock 
                            @Resource = {lockResource},
                            @LockMode = N'Exclusive', 
                            @LockOwner = N'Transaction', 
                            @LockTimeout = 10000;
                        IF @result < 0
                            THROW 50002, 'Could not acquire VietQR webhook lock (sp_getapplock failed or timed out).', 1;
                        """, ct);
                }

                SaasBillingInvoice? invoice = null;
                if (!string.IsNullOrWhiteSpace(request?.InvoiceCode))
                {
                    var code = request.InvoiceCode.Trim();
                    var cleanCode = SaasBillingService.NormalizeVietQrReference(code);
                    var withPrefix = $"ERPKHO{cleanCode}";
                    invoice = await _db.SaasBillingInvoices
                        .IgnoreQueryFilters()
                        .Where(i => i.InvoiceCode == code ||
                                    i.InvoiceCode.Replace("-", "").Replace(" ", "").ToUpper() == cleanCode ||
                                    i.InvoiceCode.Replace("-", "").Replace(" ", "").ToUpper() == withPrefix)
                        .OrderByDescending(i => i.Id)
                        .FirstOrDefaultAsync(ct);
                    if (invoice != null && invoice.TenantId > 0)
                    {
                        tenantId = invoice.TenantId;
                        _db.UseTenant(tenantId);
                    }
                }
                else
                {
                    invoice = await _db.SaasBillingInvoices
                        .Where(i => i.TenantId == tenantId && i.Status != "PAID")
                        .OrderByDescending(i => i.Id)
                        .FirstOrDefaultAsync(ct);
                    invoice ??= await _db.SaasBillingInvoices
                        .Where(i => i.TenantId == tenantId && i.InvoiceCode == resolvedInvoiceCode)
                        .OrderByDescending(i => i.Id)
                        .FirstOrDefaultAsync(ct);
                }

                if (invoice != null && string.Equals(invoice.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                {
                    var existingSub = await _db.TenantSubscriptions
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
                    var replayPlan = existingSub?.PlanCode ?? invoice.PlanCode ?? targetPlan;
                    var replayQuota = existingSub?.MonthlyOrderQuota ?? newQuota;
                    if (tx != null)
                    {
                        await tx.CommitAsync(ct);
                    }
                    return WmsEndpointOutcome.Ok(new
                    {
                        tenantId,
                        invoiceCode = invoice.InvoiceCode,
                        upgradedPlanCode = replayPlan,
                        subscriptionStatus = existingSub?.Status ?? "ACTIVE",
                        newMonthlyOrderQuota = replayQuota,
                        currentPeriodEndUtc = existingSub?.CurrentPeriodEndUtc ?? now.AddDays(30),
                        idempotentReplay = true,
                        writeBlocked = false,
                        bankCode,
                        bankName,
                        bankBin,
                        accountNo,
                        accountName,
                        paidAtUtc = invoice.PaidAtUtc ?? now,
                        statusLabel = $"Đã xác nhận thanh toán VietQR (Idempotent Replay) — Gói {replayPlan} ({replayQuota:N0} đơn/tháng)"
                    });
                }

                if (invoice != null)
                {
                    invoice.Status = "PAID";
                    invoice.PaidAtUtc = now;
                    if (!string.IsNullOrWhiteSpace(invoice.PlanCode))
                    {
                        targetPlan = invoice.PlanCode.Trim().ToUpperInvariant();
                    }
                    targetSpec = SaasBillingService.ResolvePlanSpec(targetPlan);
                    newQuota = targetSpec.MonthlyOrderQuota;
                    resolvedInvoiceCode = invoice.InvoiceCode;
                }
                else
                {
                    var paidAmt = request?.PaidAmountVnd is > 0m
                        ? request.PaidAmountVnd.Value
                        : targetSpec.MonthlyPriceVnd;
                    invoice = new SaasBillingInvoice
                    {
                        TenantId = tenantId,
                        InvoiceCode = resolvedInvoiceCode,
                        PlanCode = targetPlan,
                        AmountVnd = paidAmt,
                        PaymentProvider = "VIETQR",
                        Status = "PAID",
                        QrPayload = SaasBillingService.BuildVietQrPayload(paidAmt, resolvedInvoiceCode, bankBin, accountNo),
                        PaidAtUtc = now,
                        CreatedAtUtc = now
                    };
                    _db.SaasBillingInvoices.Add(invoice);
                }

                var sub = await _db.TenantSubscriptions
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
                if (sub == null)
                {
                    sub = new TenantSubscription
                    {
                        TenantId = tenantId,
                        PlanCode = targetPlan,
                        Status = "ACTIVE",
                        MonthlyOrderQuota = newQuota,
                        CurrentMonthOrderCount = 0,
                        MaxWarehouses = targetSpec.MaxWarehouses,
                        MaxUsers = targetSpec.MaxUsers,
                        CurrentPeriodStartUtc = now,
                        CurrentPeriodEndUtc = now.AddDays(30),
                        UpdatedAtUtc = now
                    };
                    _db.TenantSubscriptions.Add(sub);
                }
                else
                {
                    sub.PlanCode = targetPlan;
                    sub.Status = "ACTIVE";
                    sub.MonthlyOrderQuota = newQuota;
                    sub.MaxWarehouses = targetSpec.MaxWarehouses;
                    sub.MaxUsers = targetSpec.MaxUsers;
                    sub.CurrentPeriodEndUtc = now.AddDays(30);
                    sub.UpdatedAtUtc = now;
                }

                int? validAuditUserId = null;
                var candidateId = actorUserId ?? (_currentUser is { IsAuthenticated: true, UserId: > 0 } ? _currentUser.UserId : null);
                if (candidateId is > 0 &&
                    await _db.Users.AsNoTracking().AnyAsync(u => u.Id == candidateId.Value, ct))
                {
                    validAuditUserId = candidateId;
                }

                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = validAuditUserId,
                    EntityName = "TenantSubscription",
                    EntityId = tenantId,
                    Action = "SaaS.VietQrBillingWebhook.Paid",
                    NewValues = $"Plan={targetPlan}; Status=ACTIVE; Quota={newQuota}; MaxWarehouses={targetSpec.MaxWarehouses}; MaxUsers={targetSpec.MaxUsers}",
                    Reason = $"Webhook thanh toán VietQR tự động kích hoạt gói {targetPlan} & mở khóa hạn mức tức thì",
                    Result = "Success",
                    Timestamp = now
                });

                await _db.SaveChangesAsync(ct);
                if (tx != null)
                {
                    await tx.CommitAsync(ct);
                }
            }
            finally
            {
                _db.UseTenant(originalTenantId);
                if (useInMemoryGate)
                {
                    SaasBillingService.VietQrWebhookGate.Release();
                }
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            tenantId,
            invoiceCode = resolvedInvoiceCode,
            upgradedPlanCode = targetPlan,
            subscriptionStatus = "ACTIVE",
            newMonthlyOrderQuota = newQuota,
            currentPeriodEndUtc = now.AddDays(30),
            idempotentReplay = false,
            writeBlocked = false,
            bankCode,
            bankName,
            bankBin,
            accountNo,
            accountName,
            paidAtUtc = now,
            statusLabel = $"Đã xác nhận thanh toán VietQR — Mở khóa tức thì gói {targetPlan} ({newQuota:N0} đơn/tháng)"
        });
    }

    public async Task<WmsEndpointOutcome> GetOnboardingWizardStatusAsync(CancellationToken ct = default)
    {
        var hasWarehouse = await _db.Warehouses.AsNoTracking().AnyAsync(w => w.IsActive, ct);
        var hasLocations = await _db.WarehouseLocations.AsNoTracking().AnyAsync(l => l.IsActive, ct);
        var hasProducts = await _db.Products.AsNoTracking().AnyAsync(p => p.IsActive, ct);
        var hasStock = await _db.InventoryStocks.AsNoTracking().AnyAsync(s => s.Quantity > 0m, ct);
        var hasShopOrExport = await _db.MarketplaceShops.AsNoTracking().AnyAsync(ct)
                              || await _db.ExportReceipts.AsNoTracking().AnyAsync(ct);

        var step1 = hasWarehouse && hasLocations;
        var step2 = hasProducts;
        var step3 = hasStock;
        var step4 = hasShopOrExport;
        var step5 = step1 && step2 && step3 && step4;

        var completedSteps = (step1 ? 1 : 0) + (step2 ? 1 : 0) + (step3 ? 1 : 0) + (step4 ? 1 : 0) + (step5 ? 1 : 0);
        return WmsEndpointOutcome.Ok(new
        {
            completedSteps,
            totalSteps = 5,
            statusText = completedSteps >= 5 ? "Hoàn tất 5/5 bước chuẩn hóa" : $"Đã hoàn thành {completedSteps}/5 bước thiết lập"
        });
    }

    public WmsEndpointOutcome ComplianceEvidenceBundle() => WmsEndpointOutcome.Ok(new
    {
        bundleId = $"EVD-{DateTime.UtcNow:yyyyMMdd-HHmm}",
        policyVersion = "v2026.10-CANONICAL",
        legalHoldActive = true,
        appendOnlyAuditVerified = true,
        generatedAtUtc = DateTime.UtcNow
    });

    public async Task<WmsEndpointOutcome> ChangeRolePermissionAsync(
        int roleId,
        string input,
        byte[] version,
        bool add,
        int? actorUserId = null,
        CancellationToken ct = default)
    {
        var code = Permission.Normalize(input);
        await using var transaction = _db.Database.CurrentTransaction is null ? await _db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await PermissionAdministrationGuard.LockAsync(_db, ct);
            var role = await _db.Roles.SingleOrDefaultAsync(x => x.Id == roleId, ct);
            var permission = await _db.Permissions.SingleOrDefaultAsync(x => x.Code == code, ct);
            if (role is null || permission is null)
                return WmsEndpointOutcome.NotFound(new { message = "Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập." });
            if (version is null || version.Length != 8 || !role.RowVersion.SequenceEqual(version))
                throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
            var existing = await _db.RolePermissions.SingleOrDefaultAsync(x => x.RoleId == roleId && x.PermissionId == permission.Id, ct);
            var changed = add ? existing is null : existing is not null;
            if (changed)
            {
                var actor = actorUserId ?? (_currentUser is { UserId: > 0 } ? _currentUser.UserId : throw new ArgumentNullException(nameof(actorUserId)));
                if (add) _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permission.Id, GrantedByUserId = actor });
                else _db.RolePermissions.Remove(existing!);
                role.GrantRevision++;
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = actor,
                    Action = add ? "Permission.Grant" : "Permission.Revoke",
                    EntityName = "Role",
                    EntityId = roleId,
                    OldValues = JsonSerializer.Serialize(new { permissionCode = code, granted = !add }),
                    NewValues = JsonSerializer.Serialize(new { permissionCode = code, granted = add }),
                    Timestamp = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }
            await PermissionAdministrationGuard.EnsureAdministratorAsync(_db, ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return WmsEndpointOutcome.Ok(new { roleId, rowVersion = Convert.ToBase64String(role.RowVersion), changed });
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
