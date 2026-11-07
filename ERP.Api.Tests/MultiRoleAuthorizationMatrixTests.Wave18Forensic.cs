using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Hosting;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace ERP.Api.Tests;

public sealed partial class MultiRoleAuthorizationMatrixTests
{
    [Fact]
    public async Task Forensic_FND_W18_02_CustomerPermissionsAndRoles_StrictlyExcludePlatformPlane_EvenForSuperAdminAndAdmin()
    {
        var dbName = $"{nameof(Forensic_FND_W18_02_CustomerPermissionsAndRoles_StrictlyExcludePlatformPlane_EvenForSuperAdminAndAdmin)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        var allPermissions = ERP.Domain.Security.AppPermissions.All
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select((code, idx) => new Permission(code, $"Quyền {code}") { Id = idx + 1 })
            .ToList();
        db.Permissions.AddRange(allPermissions);
        var permByCode = allPermissions.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

        byte[] rv = [0, 0, 0, 0, 0, 0, 0, 1];
        var roleSpecs = new (int Id, string Name, string Domain)[]
        {
            (1, "Admin", "CUSTOMER_TENANT"),
            (2, "Manager", "CUSTOMER_TENANT"),
            (3, "WarehouseStaff", "CUSTOMER_TENANT"),
            (4, "Viewer", "CUSTOMER_TENANT"),
            (41, "Accountant", "CUSTOMER_TENANT"),
            (42, "PlatformSuperAdmin", "PLATFORM"),
            (43, "PlatformOpsAdmin", "PLATFORM"),
            (44, "PlatformBillingAdmin", "PLATFORM"),
            (45, "PlatformSupportAgent", "PLATFORM"),
            (46, "SuperAdmin", "PLATFORM"),
            (47, "BillingAdmin", "PLATFORM"),
            (48, "SupportAdmin", "PLATFORM"),
            (49, "SecurityAuditor", "PLATFORM"),
            (50, "Supervisor", "CUSTOMER_TENANT")
        };

        foreach (var spec in roleSpecs)
        {
            db.Roles.Add(new Role
            {
                Id = spec.Id,
                RoleName = spec.Name,
                SecurityDomain = spec.Domain,
                RowVersion = rv
            });
            foreach (var code in ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole(spec.Name))
            {
                if (permByCode.TryGetValue(code, out var p))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = spec.Id, PermissionId = p.Id });
                }
            }
        }

        // Inject an accidental platform.* permission on Admin to prove RolesAsync strips platform.* codes
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = 1,
            PermissionId = permByCode[AppPermissions.PlatformDashboardRead].Id
        });
        await db.SaveChangesAsync();

        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        foreach (var (callerName, callerRole, callerDomain) in new[]
        {
            ("qa_admin", "SuperAdmin", "PLATFORM"),
            ("tenant_admin", "Admin", "CUSTOMER_TENANT")
        })
        {
            var httpCtx = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(ClaimTypes.Name, callerName),
                    new Claim(ClaimTypes.Role, callerRole),
                    new Claim(TenantClaimTypes.TenantId, "1"),
                    new Claim(TenantClaimTypes.SecurityDomain, callerDomain),
                    new Claim("permission", AppPermissions.PermissionRead),
                    new Claim("permission", AppPermissions.RoleRead),
                    new Claim("permission", AppPermissions.PermissionAssign)
                ], "TestAuth"))
            };
            var currentUser = new HttpCurrentUser(new HttpContextAccessor { HttpContext = httpCtx });
            var permOps = new PermissionOperationsService(db, currentUser, Mock.Of<IWarehouseAuthorizationService>());
            var controller = new PermissionsController(permOps, Mock.Of<IWarehouseAuthorizationService>(), currentUser)
            {
                ControllerContext = new ControllerContext { HttpContext = httpCtx }
            };

            // 1. Catalog (/api/v1/permissions) returns ONLY 141 Customer WMS permissions and 0 platform.* codes
            var catalogOk = (await controller.Catalog(CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject;
            using (var catDoc = JsonSerializer.SerializeToDocument(catalogOk.Value, webJson))
            {
                var codes = catDoc.RootElement.EnumerateArray().Select(x => x.GetProperty("code").GetString()!).ToList();
                codes.Should().HaveCount(141);
                codes.Should().NotContain(c => c.StartsWith("platform.", StringComparison.OrdinalIgnoreCase));
            }

            // 2. Roles (/api/v1/permissions/roles) returns ONLY the 6 canonical Customer Warehouse Roles in exact order
            var rolesOk = (await controller.Roles(CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject;
            using (var rolesDoc = JsonSerializer.SerializeToDocument(rolesOk.Value, webJson))
            {
                var roles = rolesDoc.RootElement.EnumerateArray().ToList();
                roles.Should().HaveCount(6);
                roles.Select(r => r.GetProperty("roleCode").GetString()!).Should().Equal(
                    "Admin", "Manager", "Supervisor", "Accountant", "WarehouseStaff", "Viewer");
                roles.Select(r => r.GetProperty("displayNameVi").GetString()!).Should().Equal(
                    "Quản trị công ty",
                    "Quản lý kho",
                    "Giám sát & QC",
                    "Kế toán kho",
                    "Nhân viên kho",
                    "Kiểm toán nội bộ");
                roles.Select(r => r.GetProperty("permissions").GetArrayLength()).Should().Equal(141, 98, 54, 42, 36, 18);
                foreach (var r in roles)
                {
                    r.GetProperty("permissions").EnumerateArray()
                        .Select(p => p.GetString()!)
                        .Should().NotContain(c => c.StartsWith("platform.", StringComparison.OrdinalIgnoreCase));
                }
            }

            // 3. ChangeRolePermissionAsync blocks mutating PLATFORM roles or assigning platform.* permissions
            var mutatePlatformRole = await permOps.ChangeRolePermissionAsync(46, AppPermissions.ProductRead, rv, add: true, actorUserId: 1);
            mutatePlatformRole.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            using (var errDoc1 = JsonSerializer.SerializeToDocument(mutatePlatformRole.Payload, webJson))
            {
                errDoc1.RootElement.GetProperty("errorCode").GetString().Should().Be("PLATFORM_ROLE_MUTATION_FORBIDDEN");
            }

            var grantPlatformPerm = await permOps.ChangeRolePermissionAsync(1, AppPermissions.PlatformDashboardRead, rv, add: true, actorUserId: 1);
            grantPlatformPerm.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            using (var errDoc2 = JsonSerializer.SerializeToDocument(grantPlatformPerm.Payload, webJson))
            {
                errDoc2.RootElement.GetProperty("errorCode").GetString().Should().Be("PLATFORM_ROLE_MUTATION_FORBIDDEN");
            }
        }
    }

    [Fact]
    public void Forensic_FND_W18_06_DatabaseBackupHostedService_HardensWindowsDirectoryAcl_And_RejectsUnsafePaths()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ERP.slnx")))
        {
            root = root.Parent;
        }
        var repoRoot = root?.FullName ?? Path.GetTempPath();
        var tempBackupDir = Path.Combine(Path.GetTempPath(), $"erpkho-bkp-w18-{Guid.NewGuid():N}");
        var tempFile = Path.Combine(Path.GetTempPath(), $"erpkho-bkp-file-{Guid.NewGuid():N}.tmp");

        try
        {
            // 1. Unsafe paths must be rejected deterministically
            FluentActions.Invoking(() => DatabaseBackupHostedService.EnsureAndHardenBackupDirectory("", repoRoot))
                .Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => DatabaseBackupHostedService.EnsureAndHardenBackupDirectory(@"\\server\share\backups", repoRoot))
                .Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => DatabaseBackupHostedService.EnsureAndHardenBackupDirectory(Path.GetPathRoot(repoRoot)!, repoRoot))
                .Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => DatabaseBackupHostedService.EnsureAndHardenBackupDirectory(Path.Combine(repoRoot, "backups"), repoRoot))
                .Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => DatabaseBackupHostedService.EnsureAndHardenBackupDirectory(tempBackupDir + @"\..\escape", repoRoot))
                .Should().Throw<InvalidOperationException>();

            File.WriteAllText(tempFile, "probe");
            FluentActions.Invoking(() => DatabaseBackupHostedService.EnsureAndHardenBackupDirectory(tempFile, repoRoot))
                .Should().Throw<InvalidOperationException>();

            // 2. Valid outside-repo directory is created and ACL-hardened on Windows
            var dirInfo = DatabaseBackupHostedService.EnsureAndHardenBackupDirectory(tempBackupDir, repoRoot);
            dirInfo.Exists.Should().BeTrue();

            if (OperatingSystem.IsWindows())
            {
                VerifyWindowsDirectoryAcl(dirInfo);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            if (Directory.Exists(tempBackupDir))
            {
                Directory.Delete(tempBackupDir, recursive: true);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyWindowsDirectoryAcl(DirectoryInfo dirInfo)
    {
        var acl = dirInfo.GetAccessControl(AccessControlSections.Access);
        acl.AreAccessRulesProtected.Should().BeTrue("inherited ACLs must be stripped");

        var rules = acl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var builtinUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        rules.Should().NotContain(r => r.IdentityReference == everyoneSid);
        rules.Should().NotContain(r => r.IdentityReference == builtinUsersSid);
        rules.Should().Contain(r => r.IdentityReference == systemSid && r.AccessControlType == AccessControlType.Allow);
        rules.Should().Contain(r => r.IdentityReference == adminsSid && r.AccessControlType == AccessControlType.Allow);
    }
}
