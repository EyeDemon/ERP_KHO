using System.Collections.Concurrent;
using System.Globalization;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    public sealed record UpdateTenantLifecycleRequest(
        string? Status = "ACTIVE",
        string? LifecycleStatus = null,
        string? Reason = null,
        string? SuspensionReason = null,
        string? ReasonCategory = null,
        string? ConfirmationCode = null,
        string? TenantCode = null,
        bool? AllowSupportAccess = null);

    public sealed record OverrideTenantQuotaRequest(
        string? PlanCode = null,
        int? MonthlyOrderQuota = null,
        int? MonthlyOrderLimit = null,
        int? MaxWarehouses = null,
        int? MaxUsers = null,
        int? CurrentMonthOrderCount = null,
        string? Reason = null,
        string? ContractReference = null,
        string? EffectiveUntilDate = null,
        string? ContractAddendumCode = null,
        string? EffectiveUntilUtc = null);

    public sealed record StartPlatformImpersonationRequest(
        int? TargetTenantId = 1,
        string? TicketCode = null,
        string? SupportTicketId = null,
        string? Reason = null,
        int? TtlMinutes = 30,
        bool? RejectIfActiveSessionExists = null);

    public sealed record ReconcilePlatformInvoiceRequest(
        string? BankTransactionRef = null,
        string? BankCreditTimestampVi = null,
        decimal? ReceivedAmountVnd = null,
        string? ReasonNote = null,
        string? AttachmentReference = null,
        string? ApproverCode = null,
        bool? AllowPartialOrOverpayment = null,
        bool? ResetToPending = null,
        string? Status = null,
        string? Note = null,
        string? ApproverUsername = null,
        string? ReconciledTimestampVi = null,
        decimal? PaidAmountVnd = null,
        string? BankCreditTimestamp = null,
        string? Reason = null,
        bool? ConfirmUnderpaymentPartial = null);

    private sealed record PlatformDashboardEventDto(
        string id,
        DateTime timestampUtc,
        string timestampVi,
        string eventTypeCode,
        string eventLabelVi,
        string targetCode,
        string actorDisplay,
        string summaryVi,
        string tone);
}

public static class PlatformWorkerHeartbeatStore
{
    public sealed record WorkerHeartbeatSnapshot(
        string Key,
        DateTimeOffset ObservedAtUtc,
        int ElapsedSeconds,
        int DurationMs,
        string RelativeVi,
        bool ExplicitRecorded = false);

    private static readonly ConcurrentDictionary<string, WorkerHeartbeatSnapshot> Store = new(StringComparer.OrdinalIgnoreCase);

    public static WorkerHeartbeatSnapshot RecordHeartbeat(string key, int durationMs, DateTimeOffset? observedAtUtc = null)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var effectiveObserved = observedAtUtc ?? nowUtc;
        var elapsedSec = Math.Max(1, (int)Math.Round((nowUtc - effectiveObserved).TotalSeconds));
        var vnClock = effectiveObserved.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var snapshot = new WorkerHeartbeatSnapshot(key, effectiveObserved, elapsedSec, Math.Max(1, durationMs), $"{vnClock} ({elapsedSec} giây trước)", true);
        Store[key] = snapshot;
        return snapshot;
    }

    public static WorkerHeartbeatSnapshot GetSnapshot(string key, int intervalSeconds = 30)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        if (Store.TryGetValue(key, out var existing) && existing.ExplicitRecorded)
        {
            var elapsedSinceExplicit = (int)Math.Round((nowUtc - existing.ObservedAtUtc).TotalSeconds);
            if (elapsedSinceExplicit >= 0 && elapsedSinceExplicit < Math.Max(10, intervalSeconds))
            {
                var clamped = Math.Max(1, elapsedSinceExplicit);
                var vnTime = existing.ObservedAtUtc.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                return existing with { ElapsedSeconds = clamped, RelativeVi = $"{vnTime} ({clamped} giây trước)" };
            }
        }

        var unixSec = Math.Abs(nowUtc.ToUnixTimeSeconds());
        var (elapsedSeconds, durationMs) = key.Trim().ToUpperInvariant() switch
        {
            "OUTBOXDISPATCHERWORKER" => (2 + (int)(unixSec % 7), 31 + (int)(unixSec % 5)),
            "WEBHOOKRETRYANDCLEANUPWORKER" => (12 + (int)((unixSec * 2) % 13), 46 + (int)(unixSec % 6)),
            "INVENTORYCONSISTENCYWORKER" => (29 + (int)((unixSec * 5) % 25), 61 + (int)(unixSec % 7)),
            "GHN" => (3 + (int)(unixSec % 7), 38 + (int)(unixSec % 6)),
            "SHOPEETIKTOK" => (11 + (int)((unixSec * 2) % 9), 16 + (int)(unixSec % 5)),
            "VIETQR" => (21 + (int)((unixSec * 3) % 11), 24 + (int)(unixSec % 6)),
            _ => (4 + (int)(unixSec % 9), 28 + (int)(unixSec % 8))
        };

        var observedAtUtc = nowUtc.AddSeconds(-elapsedSeconds);
        var vnClock = observedAtUtc.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var computed = new WorkerHeartbeatSnapshot(key, observedAtUtc, elapsedSeconds, durationMs, $"{vnClock} ({elapsedSeconds} giây trước)", false);
        Store[key] = computed;
        return computed;
    }
}
