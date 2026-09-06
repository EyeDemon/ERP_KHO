namespace ERP.Application.Options;

public sealed class ApprovalAgingOptions
{
    public int WarningAfterHours { get; set; } = 24;
    public int OverdueAfterHours { get; set; } = 48;

    public void Validate()
    {
        if (WarningAfterHours < 1 || OverdueAfterHours <= WarningAfterHours)
        {
            throw new InvalidOperationException("Approval aging thresholds must be positive and overdue must be greater than warning.");
        }
    }
}
