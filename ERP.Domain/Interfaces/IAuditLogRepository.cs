using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog);
}
