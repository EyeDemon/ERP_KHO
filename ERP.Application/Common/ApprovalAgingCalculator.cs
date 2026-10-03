using ERP.Application.Options;

namespace ERP.Application.Common;

public static class ApprovalAgingCalculator
{
    public const string Normal = "Normal";
    public const string Warning = "Warning";
    public const string Overdue = "Overdue";

    public static (long? WaitingMinutes, string? SlaStatus) Calculate(
        DateTime requestedAtUtc,
        DateTime nowUtc,
        bool pending,
        ApprovalAgingOptions options)
    {
        if (!pending)
        {
            return (null, null);
        }

        options.Validate();
        var requested = AsUtc(requestedAtUtc);
        var now = AsUtc(nowUtc);
        var elapsed = now > requested ? now - requested : TimeSpan.Zero;
        var status = elapsed >= TimeSpan.FromHours(options.OverdueAfterHours)
            ? Overdue
            : elapsed >= TimeSpan.FromHours(options.WarningAfterHours)
                ? Warning
                : Normal;

        return ((long)Math.Floor(elapsed.TotalMinutes), status);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
