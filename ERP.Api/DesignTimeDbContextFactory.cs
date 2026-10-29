using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Api;

// EF tooling must not boot the production web host just to apply migrations.
// The caller supplies the target connection explicitly; no default database
// or application credential is fabricated here.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ErpKhoDbContext>
{
    public ErpKhoDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Thiếu cấu hình kết nối cơ sở dữ liệu cho EF design-time.");
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(connection)
            .Options;
        return new ErpKhoDbContext(options);
    }
}
