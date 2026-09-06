using ERP.Api.Middleware;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using ERP.Application.Common;

using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/system-.txt", rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing") && allowedOrigins.Length == 0)
    throw new InvalidOperationException("Cors:AllowedOrigins must be configured outside Development/Testing.");
if (!builder.Environment.IsDevelopment() && allowedOrigins.Any(origin => origin.Contains('*')))
    throw new InvalidOperationException("Wildcard CORS origins are not allowed outside Development.");

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .WithHeaders("Authorization", "Content-Type", "Accept")
              .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
              .AllowCredentials();
    });
});

var authSecurity = builder.Configuration.GetSection("AuthSecurity").Get<AuthSecurityOptions>() ?? new();
if (authSecurity.MaxFailedAttempts <= 0 || authSecurity.LockoutMinutes <= 0)
    throw new InvalidOperationException("AuthSecurity configuration must contain positive values.");
builder.Services.AddSingleton(authSecurity);
var sessionSecurity = builder.Configuration.GetSection("SessionSecurity").Get<SessionSecurityOptions>() ?? new();
if (sessionSecurity.RefreshTokenDays <= 0 || sessionSecurity.RetentionDays <= 0)
    throw new InvalidOperationException("SessionSecurity configuration must contain positive values.");
builder.Services.AddSingleton(sessionSecurity);

var loginPermitLimit = builder.Configuration.GetValue<int>("RateLimiting:Login:PermitLimit");
var loginWindowSeconds = builder.Configuration.GetValue<int>("RateLimiting:Login:WindowSeconds");
var apiPermitLimit = builder.Configuration.GetValue<int>("RateLimiting:Api:PermitLimit");
var apiWindowSeconds = builder.Configuration.GetValue<int>("RateLimiting:Api:WindowSeconds");
if (loginPermitLimit <= 0 || loginWindowSeconds <= 0 || apiPermitLimit <= 0 || apiWindowSeconds <= 0)
    throw new InvalidOperationException("RateLimiting configuration must contain positive values.");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))
            : loginWindowSeconds;
        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Quá nhiều yêu cầu. Vui lòng thử lại sau." }, cancellationToken);
    };
    options.AddPolicy("Login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = TimeSpan.FromSeconds(loginWindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("Refresh", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("SessionMutation", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiPermitLimit,
                Window = TimeSpan.FromSeconds(apiWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// Database
builder.Services.AddDbContext<ErpKhoDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// DI Registrations
builder.Services.AddScoped<ERP.Domain.Interfaces.IProductRepository, ERP.Infrastructure.Repositories.ProductRepository>();
builder.Services.AddScoped<ERP.Application.Interfaces.IProductService, ERP.Application.Services.ProductService>();

builder.Services.AddScoped<ERP.Domain.Interfaces.IWarehouseRepository, ERP.Infrastructure.Repositories.WarehouseRepository>();
builder.Services.AddScoped<ERP.Application.Interfaces.IWarehouseService, ERP.Application.Services.WarehouseService>();

builder.Services.AddScoped<ERP.Domain.Interfaces.IUnitRepository, ERP.Infrastructure.Repositories.UnitRepository>();
builder.Services.AddScoped<ERP.Application.Interfaces.IUnitService, ERP.Application.Services.UnitService>();

builder.Services.AddScoped<ERP.Domain.Interfaces.IExportReceiptRepository, ERP.Infrastructure.Repositories.ExportReceiptRepository>();
builder.Services.AddScoped<ERP.Application.Interfaces.IExportReceiptService, ERP.Application.Services.ExportReceiptService>();

builder.Services.AddScoped<ERP.Domain.Interfaces.IStocktakeRepository, ERP.Infrastructure.Repositories.StocktakeRepository>();
builder.Services.AddScoped<ERP.Application.Interfaces.IStocktakeService, ERP.Application.Services.StocktakeService>();

builder.Services.AddScoped<ERP.Domain.Interfaces.IImportReceiptRepository, ERP.Infrastructure.Repositories.ImportReceiptRepository>();
builder.Services.AddScoped<ERP.Application.Interfaces.IImportReceiptService, ERP.Application.Services.ImportReceiptService>();

builder.Services.AddScoped<ERP.Domain.Interfaces.IUnitOfWork, ERP.Infrastructure.Repositories.UnitOfWork>();

builder.Services.AddScoped<ERP.Domain.Interfaces.IInventoryStockRepository, ERP.Infrastructure.Repositories.InventoryStockRepository>();
builder.Services.AddScoped<ERP.Domain.Interfaces.IInventoryTransactionRepository, ERP.Infrastructure.Repositories.InventoryTransactionRepository>();

builder.Services.AddScoped<ERP.Domain.Interfaces.IReportRepository, ERP.Infrastructure.Repositories.ReportRepository>();
builder.Services.AddScoped<ERP.Application.Interfaces.IReportService, ERP.Application.Services.ReportService>();

builder.Services.AddScoped<ERP.Application.Interfaces.IInventoryQueryService, ERP.Infrastructure.Queries.InventoryQueryService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IInventoryTransactionQueryService, ERP.Infrastructure.Queries.InventoryTransactionQueryService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IInventoryReconciliationQueryService, ERP.Infrastructure.Queries.InventoryReconciliationQueryService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IStocktakeQueryService, ERP.Infrastructure.Queries.StocktakeQueryService>();

builder.Services.AddScoped<ERP.Domain.Interfaces.IAuditLogRepository, ERP.Infrastructure.Repositories.AuditLogRepository>();

// Authentication and Security Services
builder.Services.AddScoped<ERP.Domain.Interfaces.IUserRepository, ERP.Infrastructure.Repositories.UserRepository>();
builder.Services.AddScoped<ERP.Application.Interfaces.IPasswordHasherService, ERP.Infrastructure.Services.PasswordHasherService>();
builder.Services.AddScoped<ERP.Application.Interfaces.ITokenService, ERP.Infrastructure.Services.TokenService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IAuthService, ERP.Application.Services.AuthService>();
builder.Services.AddScoped<ERP.Application.Interfaces.ICurrentUser, ERP.Api.Authorization.HttpCurrentUser>();
builder.Services.AddScoped<ERP.Api.Infrastructure.RequestMetadata>();
builder.Services.AddScoped<ERP.Application.Interfaces.IRequestMetadata>(sp => sp.GetRequiredService<ERP.Api.Infrastructure.RequestMetadata>());
builder.Services.AddScoped<ERP.Application.Interfaces.IWarehouseAuthorizationService, ERP.Infrastructure.Services.WarehouseAuthorizationService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IUserWarehouseAccessService, ERP.Infrastructure.Services.UserWarehouseAccessService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IAccountAdminService, ERP.Infrastructure.Services.AccountAdminService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IUserSessionService, ERP.Infrastructure.Services.UserSessionService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IAccessTokenSessionValidator, ERP.Infrastructure.Services.AccessTokenSessionValidator>();
builder.Services.AddScoped<ERP.Application.Interfaces.IStockTransferService, ERP.Infrastructure.Services.StockTransferService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IStockReservationService, ERP.Infrastructure.Services.StockReservationService>();
builder.Services.AddScoped<ERP.Application.Interfaces.IApprovalWorkflowService, ERP.Infrastructure.Services.ApprovalWorkflowService>();
builder.Services.AddSingleton(TimeProvider.System);
var approvalAgingOptions = builder.Configuration.GetSection("ApprovalAging").Get<ERP.Application.Options.ApprovalAgingOptions>() ?? new ERP.Application.Options.ApprovalAgingOptions();
approvalAgingOptions.Validate();
builder.Services.AddSingleton(approvalAgingOptions);
builder.Services.AddSingleton(builder.Configuration.GetSection("StockReservation").Get<ERP.Application.Options.StockReservationOptions>() ?? new ERP.Application.Options.StockReservationOptions());
var exportReceiptOptions = builder.Configuration.GetSection("ExportReceipt").Get<ERP.Application.Options.ExportReceiptOptions>() ?? new ERP.Application.Options.ExportReceiptOptions();
exportReceiptOptions.WriteEnabled = builder.Configuration.GetValue("ExportWorkflow:WriteEnabled", true);
_ = exportReceiptOptions.GetDefaultMode();
builder.Services.AddSingleton(exportReceiptOptions);

// Configure JWT Authentication (Fail-Fast: no fallback, reject placeholder/empty)
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["Secret"];

if (string.IsNullOrWhiteSpace(secretKey) || secretKey == "__SET_IN_USER_SECRETS_OR_ENV__")
{
    throw new InvalidOperationException("JWT Secret is not configured. Application cannot start.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var jti = context.SecurityToken.Id;
            if (!int.TryParse(userIdValue, out var userId) || string.IsNullOrWhiteSpace(jti))
            {
                context.Fail("Access token session is invalid.");
                return;
            }

            var validator = context.HttpContext.RequestServices
                .GetRequiredService<ERP.Application.Interfaces.IAccessTokenSessionValidator>();
            if (!await validator.IsActiveAsync(userId, jti, context.HttpContext.RequestAborted))
                context.Fail("Access token session is no longer active.");
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ERP.Api.Authorization.ApprovalPolicies.Checker,
        policy => policy.RequireAuthenticatedUser().RequireRole(
            ERP.Api.Authorization.AppRoles.Admin,
            ERP.Api.Authorization.AppRoles.Manager));
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Global Exception Handling
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
app.UseMiddleware<ERP.Api.Infrastructure.CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

app.UseRouting();
app.UseCors("ConfiguredOrigins");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }

