using System.Text.Json.Serialization;
using ERP.Domain.Enums;

namespace ERP.Application.DTOs;

public sealed class ObserveReceivingDto
{
    public List<ObserveReceivingLineDto> Lines { get; set; } = [];
}

public sealed class ObserveReceivingLineDto
{
    public int LineId { get; set; }
    public decimal ObservedQuantity { get; set; }
    public int? ObservedUnitId { get; set; }
    public List<ObservationItemDto> Items { get; set; } = [];
}

public sealed class ObservationItemDto
{
    public decimal Quantity { get; set; }
    public string? ScanReference { get; set; }
}

public sealed class SubmitReceivingDiscrepancyDto
{
    public string Action { get; set; } = string.Empty;
    public decimal DoorRejectedQuantity { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResponsibleParty ResponsibleParty { get; set; }
    public bool SupplierClaimRequired { get; set; }
    public string? Note { get; set; }
    public string? EvidenceReference { get; set; }
    public bool ConfirmNormalizedObservation { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class RecountReceivingDto
{
    public decimal ObservedQuantity { get; set; }
    public int? ObservedUnitId { get; set; }
    public List<int> VoidedObservationItemIds { get; set; } = [];
    public List<ObservationItemDto> Items { get; set; } = [];
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ReceivingDiscrepancyDto
{
    public int Id { get; set; }
    public int ReceiptLineId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal ExpectedQuantity { get; set; }
    public decimal BaseExpectedQuantity { get; set; }
    public decimal ObservedQuantity { get; set; }
    public decimal BaseObservedQuantity { get; set; }
    public decimal NormalizedObservedQuantity { get; set; }
    public decimal DifferenceQuantity => NormalizedObservedQuantity - ExpectedQuantity;
    public decimal BaseDifferenceQuantity => BaseObservedQuantity - BaseExpectedQuantity;
    public decimal DoorRejectedQuantity { get; set; }
    public decimal FinalReceivedQuantity { get; set; }
    public string OperationUnitCode { get; set; } = string.Empty;
    public int OperationUnitId { get; set; }
    public int ObservedUnitId { get; set; }
    public string ObservedUnitCode { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public decimal ConversionFactor { get; set; }
    public int ConversionVersion { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<ReceivingObservationDto> Observations { get; set; } = [];
    public List<ReceivingResolutionDto> Resolutions { get; set; } = [];
}

public sealed class ReceivingReasonCodeDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool RequiresNote { get; set; }
    public bool RequiresAttachment { get; set; }
    public bool RequiresApproval { get; set; }
}

public sealed class ReceivingObservationDto
{
    public int Id { get; set; }
    public int Version { get; set; }
    public decimal ObservedQuantity { get; set; }
    public decimal BaseObservedQuantity { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ReceivingResolutionDto
{
    public int Id { get; set; }
    public int Version { get; set; }
    public string Action { get; set; } = string.Empty;
    public decimal DoorRejectedQuantity { get; set; }
    public decimal FinalReceivedQuantity { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string ReasonName { get; set; } = string.Empty;
    public int ReasonVersion { get; set; }
    public bool RequiresApproval { get; set; }
    public string ResponsibleParty { get; set; } = string.Empty;
    public bool SupplierClaimRequired { get; set; }
    public string? Note { get; set; }
    public string? EvidenceReference { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
}
