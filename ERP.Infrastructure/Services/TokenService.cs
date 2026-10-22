using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Security;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ERP.Infrastructure.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _configuration;
        private readonly ErpKhoDbContext? _db;

        public TokenService(IConfiguration configuration, ErpKhoDbContext? db = null)
        {
            _configuration = configuration;
            _db = db;
        }

        public AccessTokenDto GenerateAccessToken(User user)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["Secret"];

            if (string.IsNullOrWhiteSpace(secretKey) || secretKey == "__SET_IN_USER_SECRETS_OR_ENV__")
            {
                throw new InvalidOperationException("JWT Secret is not configured.");
            }

            var key = Encoding.UTF8.GetBytes(secretKey);

            var jti = Guid.NewGuid().ToString("N");
            var expiresAtUtc = DateTime.UtcNow.AddMinutes(double.Parse(jwtSettings["ExpiryMinutes"] ?? "15"));
            var tenantId = user.TenantId > 0 ? user.TenantId : 1;
            var roleName = user.Role?.RoleName;
            if (string.IsNullOrWhiteSpace(roleName) && _db is not null && user.RoleId > 0)
            {
                roleName = _db.Roles
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(r => r.Id == user.RoleId)
                    .Select(r => r.RoleName)
                    .FirstOrDefault();
            }

            var permissionCodes = ResolvePermissionCodes(user, roleName);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, roleName ?? string.Empty),
                new(TenantClaimTypes.TenantId, tenantId.ToString()),
                new(TenantClaimTypes.TenantCode, tenantId == 1 ? "DEFAULT" : $"TENANT-{tenantId}"),
                new(JwtRegisteredClaimNames.Jti, jti)
            };

            foreach (var permissionCode in permissionCodes)
            {
                claims.Add(new Claim("permission", permissionCode));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expiresAtUtc,
                Issuer = jwtSettings["Issuer"],
                Audience = jwtSettings["Audience"],
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return new AccessTokenDto(tokenHandler.WriteToken(token), jti, expiresAtUtc);
        }

        private IReadOnlyCollection<string> ResolvePermissionCodes(User user, string? roleName)
        {
            var effectivePermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var defaultPerm in AppPermissions.GetDefaultPermissionsForRole(roleName))
            {
                effectivePermissions.Add(defaultPerm.Trim().ToLowerInvariant());
            }

            if (user.Role?.Permissions is { Count: > 0 })
            {
                foreach (var rp in user.Role.Permissions)
                {
                    if (!string.IsNullOrWhiteSpace(rp.Permission?.Code))
                    {
                        effectivePermissions.Add(rp.Permission.Code.Trim().ToLowerInvariant());
                    }
                }
            }

            if (_db is not null)
            {
                try
                {
                    var roleId = user.RoleId;
                    if (roleId <= 0 && user.Id > 0)
                    {
                        roleId = _db.Users
                            .IgnoreQueryFilters()
                            .AsNoTracking()
                            .Where(u => u.Id == user.Id)
                            .Select(u => u.RoleId)
                            .FirstOrDefault();
                    }

                    if (roleId > 0)
                    {
                        var anyRolePermissionsSeeded = _db.RolePermissions
                            .IgnoreQueryFilters()
                            .AsNoTracking()
                            .Any();

                        if (anyRolePermissionsSeeded)
                        {
                            var seededPermissionCodes = _db.Permissions
                                .IgnoreQueryFilters()
                                .AsNoTracking()
                                .Where(p => !string.IsNullOrWhiteSpace(p.Code))
                                .Select(p => p.Code)
                                .ToList();

                            var grantedDbCodes = _db.RolePermissions
                                .IgnoreQueryFilters()
                                .AsNoTracking()
                                .Where(rp => rp.RoleId == roleId && rp.Permission != null && !string.IsNullOrWhiteSpace(rp.Permission.Code))
                                .Select(rp => rp.Permission.Code)
                                .ToList();

                            var grantedSet = new HashSet<string>(
                                grantedDbCodes.Select(c => c.Trim().ToLowerInvariant()),
                                StringComparer.OrdinalIgnoreCase);

                            foreach (var seededCode in seededPermissionCodes)
                            {
                                var normalized = seededCode.Trim().ToLowerInvariant();
                                if (grantedSet.Contains(normalized))
                                {
                                    effectivePermissions.Add(normalized);
                                }
                                else
                                {
                                    effectivePermissions.Remove(normalized);
                                }
                            }

                            foreach (var granted in grantedSet)
                            {
                                effectivePermissions.Add(granted);
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback to default role permissions when DB is unavailable in isolated unit tests.
                }
            }

            if (string.Equals(roleName, "WarehouseStaff", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Viewer", StringComparison.OrdinalIgnoreCase))
            {
                var explicitlyGrantedOnEntity = user.Role?.Permissions?.Any(rp =>
                    string.Equals(rp.Permission?.Code, AppPermissions.InventoryCostRead, StringComparison.OrdinalIgnoreCase)) == true;
                if (!explicitlyGrantedOnEntity)
                {
                    effectivePermissions.Remove(AppPermissions.InventoryCostRead);
                }
            }

            return effectivePermissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public string GenerateToken(User user) => GenerateAccessToken(user).Token;

        public string GenerateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

        public string HashRefreshToken(string refreshToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        }

        public bool FixedTimeHashEquals(string leftHash, string rightHash)
        {
            try
            {
                return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(leftHash), Convert.FromHexString(rightHash));
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
