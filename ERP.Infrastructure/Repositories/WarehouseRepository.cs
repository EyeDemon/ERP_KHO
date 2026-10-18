using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class WarehouseRepository : Repository<Warehouse>, IWarehouseRepository
    {
        private static readonly string[] SystemLocationCodes = ["RECEIVING", "LEGACY", "DAMAGED"];

        public WarehouseRepository(ErpKhoDbContext context) : base(context)
        {
        }

        public async Task<bool> ExistsByCodeAsync(string code, int? excludeId = null)
        {
            var query = _dbSet.Where(w => w.Code == code);
            if (excludeId.HasValue)
            {
                query = query.Where(w => w.Id != excludeId.Value);
            }
            return await query.AnyAsync();
        }

        public async Task<bool> HasTransactionsAsync(int warehouseId)
        {
            if (await _context.InventoryTransactions.AnyAsync(t => t.WarehouseId == warehouseId)) return true;
            if (await _context.InventoryStocks.AnyAsync(s => s.WarehouseId == warehouseId && (s.Quantity != 0 || s.ReservedQuantity != 0))) return true;
            if (await _context.ImportReceipts.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.ExportReceipts.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.PutawayTasks.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.PickingTasks.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.PackingSessions.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.Shipments.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.Stocktakes.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.StockTransfers.AnyAsync(x => x.SourceWarehouseId == warehouseId || x.DestinationWarehouseId == warehouseId)) return true;
            if (await _context.StockReservations.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.StockAllocations.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.PurchaseOrders.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.Asns.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.SalesOrders.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.Docks.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.YardSlots.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.DockAppointments.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.HandlingUnits.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.Backorders.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.InventoryLocks.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.InventoryLocationMovements.AnyAsync(x => x.WarehouseId == warehouseId)) return true;
            if (await _context.WarehouseLocations.AnyAsync(x => x.WarehouseId == warehouseId && !x.IsSystemManaged && !SystemLocationCodes.Contains(x.Code))) return true;

            return false;
        }

        public override async Task<Warehouse> AddAsync(Warehouse entity, CancellationToken cancellationToken = default)
        {
            await using var transaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            if (entity.UserAccesses.Count > 0 && _context.Database.IsRelational())
            {
                foreach (var access in entity.UserAccesses.ToList())
                {
                    var userExists = await _context.Users.AnyAsync(u => u.Id == access.UserId, cancellationToken);
                    if (!userExists)
                    {
                        entity.UserAccesses.Remove(access);
                    }
                }
            }

            var created = await base.AddAsync(entity, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return created;
        }

        public override async Task DeleteAsync(Warehouse entity, CancellationToken cancellationToken = default)
        {
            await using var transaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var warehouseId = entity.Id;

            var emptyStocks = await _context.InventoryStocks
                .Where(x => x.WarehouseId == warehouseId && x.Quantity == 0 && x.ReservedQuantity == 0)
                .ToListAsync(cancellationToken);
            if (emptyStocks.Count > 0)
            {
                _context.InventoryStocks.RemoveRange(emptyStocks);
            }

            var shifts = await _context.WarehouseShifts
                .Where(x => x.WarehouseId == warehouseId)
                .ToListAsync(cancellationToken);
            if (shifts.Count > 0) _context.WarehouseShifts.RemoveRange(shifts);

            var days = await _context.WarehouseCalendarDays
                .Where(x => x.WarehouseId == warehouseId)
                .ToListAsync(cancellationToken);
            if (days.Count > 0) _context.WarehouseCalendarDays.RemoveRange(days);

            var calendars = await _context.WarehouseCalendars
                .Where(x => x.WarehouseId == warehouseId)
                .ToListAsync(cancellationToken);
            if (calendars.Count > 0) _context.WarehouseCalendars.RemoveRange(calendars);

            var userWarehouses = await _context.UserWarehouses
                .Where(x => x.WarehouseId == warehouseId)
                .ToListAsync(cancellationToken);
            if (userWarehouses.Count > 0) _context.UserWarehouses.RemoveRange(userWarehouses);

            var tolerancePolicies = await _context.ReceivingTolerancePolicies
                .Where(x => x.WarehouseId == warehouseId)
                .ToListAsync(cancellationToken);
            if (tolerancePolicies.Count > 0) _context.ReceivingTolerancePolicies.RemoveRange(tolerancePolicies);

            var systemLocations = await _context.WarehouseLocations
                .Where(x => x.WarehouseId == warehouseId && (x.IsSystemManaged || SystemLocationCodes.Contains(x.Code)))
                .ToListAsync(cancellationToken);
            if (systemLocations.Count > 0) _context.WarehouseLocations.RemoveRange(systemLocations);

            await base.DeleteAsync(entity, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
    }
}
