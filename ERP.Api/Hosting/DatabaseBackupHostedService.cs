using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using ERP.Api.Controllers;

namespace ERP.Api.Hosting;

/// <summary>
/// Background governance service that validates and hardens the configured external database
/// backup directory (outside the repository root, protected Windows ACLs without Everyone/Users)
/// and records periodic operational heartbeats.
/// </summary>
public sealed class DatabaseBackupHostedService(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<DatabaseBackupHostedService> logger) : BackgroundService
{
    public const string HeartbeatKey = "DatabaseBackupHostedService";
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RunCheckOnce();
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                RunCheckOnce();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DatabaseBackupHostedService periodic directory check failed.");
            }
        }
    }

    public void RunCheckOnce()
    {
        var sw = Stopwatch.StartNew();
        var rawBackupPath = configuration["Backup:DirectoryPath"]?.Trim();
        if (string.IsNullOrWhiteSpace(rawBackupPath)
            || rawBackupPath.StartsWith("__SET_IN_", StringComparison.OrdinalIgnoreCase)
            || rawBackupPath.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            PlatformWorkerHeartbeatStore.RecordHeartbeat(HeartbeatKey, Math.Max(1, (int)sw.ElapsedMilliseconds));
            logger.LogInformation("DatabaseBackupHostedService startup/idle check: Backup:DirectoryPath is not configured.");
            return;
        }

        try
        {
            var repoRoot = ResolveRepositoryRootPath(hostEnvironment.ContentRootPath);
            var hardened = EnsureAndHardenBackupDirectory(rawBackupPath, repoRoot);
            sw.Stop();
            PlatformWorkerHeartbeatStore.RecordHeartbeat(HeartbeatKey, Math.Max(1, (int)sw.ElapsedMilliseconds));
            logger.LogInformation(
                "DatabaseBackupHostedService verified hardened backup directory at '{BackupDirectory}'.",
                hardened.FullName);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "DatabaseBackupHostedService rejected unsafe Backup:DirectoryPath configuration.");
        }
    }

    public static DirectoryInfo EnsureAndHardenBackupDirectory(string backupPath, string repositoryRootPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            throw new ArgumentException("Đường dẫn thư mục sao lưu không được để trống.", nameof(backupPath));
        }

        if (string.IsNullOrWhiteSpace(repositoryRootPath))
        {
            throw new ArgumentException("Đường dẫn gốc mã nguồn không được để trống.", nameof(repositoryRootPath));
        }

        var trimmedInput = backupPath.Trim();
        if (trimmedInput.StartsWith(@"\\", StringComparison.Ordinal)
            || trimmedInput.StartsWith("//", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Không cho phép dùng đường dẫn mạng UNC cho thư mục sao lưu cục bộ.");
        }

        var rawSegments = trimmedInput.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (rawSegments.Contains("..", StringComparer.Ordinal) || !Path.IsPathRooted(trimmedInput))
        {
            throw new InvalidOperationException("Đường dẫn thư mục sao lưu phải là đường dẫn tuyệt đối và không chứa '..'.");
        }

        var fullBackupPath = Path.GetFullPath(trimmedInput)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRepoRoot = Path.GetFullPath(repositoryRootPath.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var rootPath = Path.GetPathRoot(fullBackupPath)?
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(fullBackupPath)
            || string.Equals(fullBackupPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Không được dùng thư mục gốc ổ đĩa làm thư mục sao lưu.");
        }

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(fullBackupPath, fullRepoRoot, pathComparison)
            || fullBackupPath.StartsWith(fullRepoRoot + Path.DirectorySeparatorChar, pathComparison))
        {
            throw new InvalidOperationException("Thư mục sao lưu bắt buộc phải nằm ngoài cây mã nguồn dự án.");
        }

        if (File.Exists(fullBackupPath))
        {
            throw new InvalidOperationException("Đường dẫn sao lưu trỏ tới một tệp tin hiện hữu thay vì thư mục.");
        }

        EnsureNoReparsePointsAlongChain(fullBackupPath);
        var dirInfo = Directory.CreateDirectory(fullBackupPath);
        dirInfo.Refresh();
        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Không cho phép sử dụng Symlink hoặc Junction cho thư mục sao lưu.");
        }

        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            security.AddAccessRule(new FileSystemAccessRule(
                systemSid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                adminsSid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));

            using var currentIdentity = WindowsIdentity.GetCurrent();
            if (currentIdentity.User is { } currentUserSid)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUserSid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            }

            dirInfo.SetAccessControl(security);
        }

        return dirInfo;
    }

    private static void EnsureNoReparsePointsAlongChain(string targetFullPath)
    {
        var probe = new DirectoryInfo(targetFullPath);
        while (probe is not null)
        {
            if (probe.Exists && (probe.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Phát hiện Symlink/Junction không an toàn trên đường dẫn thư mục sao lưu.");
            }

            probe = probe.Parent;
        }
    }

    private static string ResolveRepositoryRootPath(string contentRootPath)
    {
        var dir = new DirectoryInfo(contentRootPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ERP.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return contentRootPath;
    }
}
