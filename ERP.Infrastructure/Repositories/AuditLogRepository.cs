using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;

namespace ERP.Infrastructure.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly ErpKhoDbContext _context;

    public AuditLogRepository(ErpKhoDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AuditLog auditLog)
    {
        await _context.AuditLogs.AddAsync(auditLog);
    }
}
