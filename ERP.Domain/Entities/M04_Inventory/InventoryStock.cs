using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class InventoryStock : ITenantEntity
{
    private int _tenantId = 1;
    private int _productId;
    private int _warehouseId;
    private int? _locationId;
    private int? _lotId;
    private int? _serialId;
    private int? _handlingUnitId;
    private int? _ownerId;
    private InventoryStatus _status = InventoryStatus.Available;

    public InventoryStock()
    {
        RefreshGrainHash();
    }

    public int Id { get; set; }

    public int TenantId
    {
        get => _tenantId;
        set
        {
            _tenantId = value;
            RefreshGrainHash();
        }
    }

    public int ProductId
    {
        get => _productId;
        set
        {
            _productId = value;
            RefreshGrainHash();
        }
    }

    public int WarehouseId
    {
        get => _warehouseId;
        set
        {
            _warehouseId = value;
            RefreshGrainHash();
        }
    }

    public int? LocationId
    {
        get => _locationId;
        set
        {
            _locationId = value;
            RefreshGrainHash();
        }
    }

    public int? LotId
    {
        get => _lotId;
        set
        {
            _lotId = value;
            RefreshGrainHash();
        }
    }

    public int? SerialId
    {
        get => _serialId;
        set
        {
            _serialId = value;
            RefreshGrainHash();
        }
    }

    public int? HandlingUnitId
    {
        get => _handlingUnitId;
        set
        {
            _handlingUnitId = value;
            RefreshGrainHash();
        }
    }

    public int? OwnerId
    {
        get => _ownerId;
        set
        {
            _ownerId = value;
            RefreshGrainHash();
        }
    }

    public InventoryStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            RefreshGrainHash();
        }
    }
    
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public decimal Quantity { get; set; }

    public decimal ReservedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal LockedQuantity { get; set; }
    public decimal MovingAverageCost { get; set; }
    public byte[]? GrainHash { get; set; }
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public void RefreshGrainHash()
    {
        var resolvedLotId = LotId is > 0 ? LotId : (Lot?.Id is > 0 ? Lot.Id : null);
        var resolvedSerialId = SerialId is > 0 ? SerialId : (Serial?.Id is > 0 ? Serial.Id : null);
        if ((resolvedLotId is null && Lot is not null && !string.IsNullOrWhiteSpace(Lot.LotNumber))
            || (resolvedSerialId is null && Serial is not null && !string.IsNullOrWhiteSpace(Serial.SerialNumber)))
        {
            var lotPart = resolvedLotId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ?? (Lot is not null && !string.IsNullOrWhiteSpace(Lot.LotNumber) ? $"LOT:{Lot.LotNumber}" : "-1");
            var serialPart = resolvedSerialId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ?? (Serial is not null && !string.IsNullOrWhiteSpace(Serial.SerialNumber) ? $"SER:{Serial.SerialNumber}" : "-1");
            var canonical = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{TenantId}|{WarehouseId}|{(LocationId is > 0 ? LocationId.Value : -1)}|{ProductId}|{lotPart}|{serialPart}|{(HandlingUnitId is > 0 ? HandlingUnitId.Value : -1)}|{(int)Status}|{(OwnerId is > 0 ? OwnerId.Value : -1)}");
            GrainHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
            return;
        }

        GrainHash = ComputeCanonicalGrainHash(
            TenantId,
            WarehouseId,
            LocationId,
            ProductId,
            resolvedLotId,
            resolvedSerialId,
            HandlingUnitId,
            Status,
            OwnerId);
    }

    public static byte[] ComputeCanonicalGrainHash(
        int tenantId,
        int warehouseId,
        int? locationId,
        int productId,
        int? lotId,
        int? serialId,
        int? handlingUnitId,
        InventoryStatus status,
        int? ownerId)
    {
        var canonicalKey = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{tenantId}|{warehouseId}|{(locationId is > 0 ? locationId.Value : -1)}|{productId}|{(lotId is > 0 ? lotId.Value : -1)}|{(serialId is > 0 ? serialId.Value : -1)}|{(handlingUnitId is > 0 ? handlingUnitId.Value : -1)}|{(int)status}|{(ownerId is > 0 ? ownerId.Value : -1)}");
        return System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalKey));
    }

    private InventoryLot? _lot;
    private InventorySerial? _serial;

    // Navigation
    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public WarehouseLocation? Location { get; set; }
    public InventoryLot? Lot
    {
        get => _lot;
        set
        {
            _lot = value;
            if (value is { Id: > 0 } && (_lotId is null or <= 0))
            {
                _lotId = value.Id;
            }
            RefreshGrainHash();
        }
    }
    public InventorySerial? Serial
    {
        get => _serial;
        set
        {
            _serial = value;
            if (value is { Id: > 0 } && (_serialId is null or <= 0))
            {
                _serialId = value.Id;
            }
            RefreshGrainHash();
        }
    }
    public InventoryStatusDefinition StatusDefinition { get; set; } = null!;
}
