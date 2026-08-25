using ERP.Domain.Enums;

namespace ERP.Application.Options;

public sealed class ExportReceiptOptions
{
    public bool AllowPerReceiptDispatchMode { get; set; } = true;
    public string DefaultDispatchMode { get; set; } = nameof(ExportDispatchMode.RequireSeparateDispatch);
    public bool AllowWarehouseStaffDirectDispatch { get; set; }
    public bool RequireDifferentDispatcher { get; set; }

    public ExportDispatchMode GetDefaultMode() =>
        Enum.TryParse<ExportDispatchMode>(DefaultDispatchMode, true, out var mode)
            ? mode
            : throw new InvalidOperationException("ExportReceipt:DefaultDispatchMode is invalid.");
}
