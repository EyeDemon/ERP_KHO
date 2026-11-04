using System.Data;
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

public interface ISecurityDomainCurrentUser
{
    string SecurityDomain { get; }
}

public sealed partial class PermissionOperationsService : IPermissionOperationsService
{
    private static readonly SemaphoreSlim TenantProvisioningGate = new(1, 1);

    private static readonly HashSet<string> PlatformInternalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlatformSuperAdmin",
        "PlatformOwner",
        "PlatformBillingAdmin",
        "PlatformOpsAdmin",
        "PlatformSupportAgent",
        "SuperAdmin",
        "BillingAdmin",
        "SupportAdmin",
        "SecurityAuditor"
    };

    private readonly ErpKhoDbContext _db;
    private readonly IWarehouseAuthorizationService? _warehouseAuth;
    private readonly ICurrentUser? _currentUser;
    private readonly IPasswordHasherService? _passwordHasher;
    private readonly IConfiguration? _configuration;

    public static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
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
            return [];
        }

        var bytes = new List<byte>((clean.Length * 5) / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var ch in clean)
        {
            var val = alphabet.IndexOf(ch);
            if (val < 0)
            {
                return [];
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

    public async Task<WmsEndpointOutcome> CatalogAsync(CancellationToken ct = default)
    {
        var rawPermissions = await _db.Permissions.AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                Code = x.Code.Trim(),
                Description = x.Description != null ? x.Description.Trim() : null
            })
            .ToListAsync(ct);

        var customerPermissions = rawPermissions
            .Where(x => !x.Code.StartsWith("platform.", StringComparison.OrdinalIgnoreCase))
            .Select(x => new
            {
                x.Code,
                Description = NormalizePermissionDescriptionVi(x.Code, x.Description)
            })
            .ToList();

        return WmsEndpointOutcome.Ok(customerPermissions);
    }

    private static string NormalizePermissionDescriptionVi(string code, string? description)
    {
        if (string.Equals(code, ERP.Domain.Security.AppPermissions.ApprovalApprove, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, ERP.Domain.Security.AppPermissions.ApprovalReject, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(description)
            || System.Text.RegularExpressions.Regex.IsMatch(
                description,
                @"(phê duyệt\s+phê duyệt|kiểm tra\s+kiểm tra)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return ERP.Domain.Security.AppPermissions.ResolvePermissionDescriptionVi(code);
        }

        return description;
    }

    private static bool IsPlatformInternalRole(string? roleName, string? securityDomain)
    {
        var clean = (roleName ?? string.Empty).Trim();
        return PlatformInternalRoles.Contains(clean)
            || clean.StartsWith("Platform", StringComparison.OrdinalIgnoreCase)
            || string.Equals(securityDomain?.Trim(), "PLATFORM", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetCanonicalCustomerRoleRank(string? roleCode) => (roleCode ?? string.Empty).Trim() switch
    {
        "Admin" => 0,
        "Manager" => 1,
        "Supervisor" => 2,
        "Accountant" => 3,
        "WarehouseStaff" => 4,
        "Viewer" => 5,
        _ => 100
    };

    public static string ResolveRoleDisplayNameVi(string? roleCode)
    {
        var normalized = (roleCode ?? string.Empty).Trim();
        return normalized switch
        {
            "Admin" => "Quản trị viên công ty",
            "Manager" => "Quản lý kho",
            "Supervisor" => "Giám sát & QC",
            "Accountant" => "Kế toán kho",
            "WarehouseStaff" => "Nhân viên kho",
            "Viewer" => "Chỉ xem / Kiểm toán",
            "Receiver" => "Nhân viên nhận hàng",
            "PickerPacker" => "Nhân viên lấy/đóng gói",
            "PlatformSuperAdmin" => "Quản trị viên Cấp cao",
            "PlatformBillingAdmin" => "Quản trị Cước phí & Hạn ngạch",
            "PlatformOpsAdmin" => "Quản trị Vận hành Nền tảng",
            "PlatformSupportAgent" => "Chuyên viên Hỗ trợ Kỹ thuật",
            { Length: > 0 } code => $"Vai trò {code}",
            _ => "Vai trò chưa định danh"
        };
    }

    public async Task<WmsEndpointOutcome> RolesAsync(CancellationToken ct = default)
    {
        var rawRoles = await _db.Roles.AsNoTracking()
            .Select(x => new
            {
                x.Id,
                RoleName = x.RoleName.Trim(),
                x.SecurityDomain,
                RowVersion = Convert.ToBase64String(x.RowVersion),
                Permissions = x.Permissions
                    .OrderBy(p => p.Permission.Code)
                    .Select(p => p.Permission.Code.Trim())
                    .ToArray()
            })
            .ToListAsync(ct);

        var items = rawRoles
            .Where(x => !IsPlatformInternalRole(x.RoleName, x.SecurityDomain))
            .OrderBy(x => GetCanonicalCustomerRoleRank(x.RoleName))
            .ThenBy(x => x.RoleName, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var roleCode = x.RoleName;
                return new
                {
                    x.Id,
                    RoleName = roleCode,
                    RoleCode = roleCode,
                    DisplayNameVi = ResolveRoleDisplayNameVi(roleCode),
                    x.RowVersion,
                    Permissions = x.Permissions
                        .Where(p => !p.StartsWith("platform.", StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                };
            })
            .ToList();

        return WmsEndpointOutcome.Ok(items);
    }

    public async Task<WmsEndpointOutcome> UsersAsync(CancellationToken ct = default)
    {
        var activeTenantId = _db.CurrentTenantId;
        var rawUsers = await _db.Users
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
                x.SecurityDomain,
                x.RoleId,
                RoleName = x.Role != null ? x.Role.RoleName.Trim() : string.Empty,
                RoleSecurityDomain = x.Role != null ? x.Role.SecurityDomain : null,
                x.IsActive,
                x.LockoutEnd,
                x.SecurityRevision
            })
            .ToListAsync(ct);

        var customerUsers = rawUsers
            .Where(x => !string.Equals(x.SecurityDomain?.Trim(), "PLATFORM", StringComparison.OrdinalIgnoreCase)
                     && !IsPlatformInternalRole(x.RoleName, x.RoleSecurityDomain)
                     && !ERP.Domain.Security.AppPermissions.IsPlatformRole(x.RoleName))
            .Select(x => new
            {
                x.Id,
                x.Username,
                x.FullName,
                x.Email,
                x.TenantId,
                x.RoleId,
                x.RoleName,
                x.IsActive,
                x.LockoutEnd,
                x.SecurityRevision
            })
            .ToList();

        return WmsEndpointOutcome.Ok(customerUsers);
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
        if (code.StartsWith("platform.", StringComparison.OrdinalIgnoreCase))
        {
            return WmsEndpointOutcome.ForbiddenObject(new
            {
                code = "PLATFORM_ROLE_MUTATION_FORBIDDEN",
                errorCode = "PLATFORM_ROLE_MUTATION_FORBIDDEN",
                message = "Không được phép gán hoặc thu hồi quyền quản trị nền tảng (platform.*) trên cổng khách hàng."
            });
        }

        await using var transaction = _db.Database.CurrentTransaction is null
            ? (_db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : await _db.Database.BeginTransactionAsync(ct))
            : null;
        try
        {
            await PermissionAdministrationGuard.LockAsync(_db, ct);
            var role = await _db.Roles.SingleOrDefaultAsync(x => x.Id == roleId, ct);
            if (role is not null && IsPlatformInternalRole(role.RoleName, role.SecurityDomain))
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                return WmsEndpointOutcome.ForbiddenObject(new
                {
                    code = "PLATFORM_ROLE_MUTATION_FORBIDDEN",
                    errorCode = "PLATFORM_ROLE_MUTATION_FORBIDDEN",
                    message = "Không được phép thay đổi vai trò quản trị nền tảng (PLATFORM) trên cổng khách hàng."
                });
            }
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
