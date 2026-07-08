using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Services;

// Periodically writes a CSV backup of all redirects to a folder and/or emails
// it, per RedirectBackupOptions. Disabled by default (opt-in). Uses
// FlushDatabaseFactory (a standalone NPoco Database, not IScopeProvider) for
// the same reason RedirectHitFlushService/MissedRequestFlushService do: an
// independently-scheduled BackgroundService creating Umbraco ambient scopes
// is not safe (see those services' comments for the full story).
public class RedirectCsvBackupService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const string BackupFilePrefix = "redirects-";

    private readonly IOptionsMonitor<RedirectBackupOptions> _backupOptions;
    private readonly IOptionsMonitor<ConnectionStrings> _connectionStrings;
    private readonly IOptionsMonitor<GlobalSettings> _globalSettings;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<RedirectCsvBackupService> _logger;
    private DateTime _lastBackupUtc = DateTime.MinValue;
    private DateTime _lastSummaryEmailUtc = DateTime.MinValue;

    public RedirectCsvBackupService(
        IOptionsMonitor<RedirectBackupOptions> backupOptions,
        IOptionsMonitor<ConnectionStrings> connectionStrings,
        IOptionsMonitor<GlobalSettings> globalSettings,
        IEmailSender emailSender,
        ILogger<RedirectCsvBackupService> logger)
    {
        _backupOptions = backupOptions;
        _connectionStrings = connectionStrings;
        _globalSettings = globalSettings;
        _emailSender = emailSender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            await RunIfDueAsync().ConfigureAwait(false);
            await RunSummaryEmailIfDueAsync().ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task RunIfDueAsync()
    {
        var options = _backupOptions.CurrentValue;
        if (!options.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
        if (DateTime.UtcNow - _lastBackupUtc < interval)
        {
            return;
        }

        try
        {
            var redirects = FetchRedirects();
            var csvBytes = RedirectCsvWriter.Write(redirects);
            var fileName = $"{BackupFilePrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

            if (!string.IsNullOrWhiteSpace(options.FolderPath))
            {
                WriteToFolder(options.FolderPath, fileName, csvBytes, options.RetentionCount);
            }

            if (!string.IsNullOrWhiteSpace(options.EmailTo))
            {
                await SendEmailWithAttachmentAsync(
                    options.EmailTo,
                    "Redirect Manager - Scheduled CSV Backup",
                    $"Attached is the scheduled redirect backup generated on {DateTime.UtcNow:u}.",
                    fileName,
                    csvBytes).ConfigureAwait(false);
            }

            _lastBackupUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run scheduled redirect CSV backup");
        }
    }

    private async Task RunSummaryEmailIfDueAsync()
    {
        var options = _backupOptions.CurrentValue;
        if (!options.SummaryEmailEnabled || string.IsNullOrWhiteSpace(options.SummaryEmailTo))
        {
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.SummaryIntervalHours));
        if (DateTime.UtcNow - _lastSummaryEmailUtc < interval)
        {
            return;
        }

        try
        {
            var stats = BuildStats();
            var csvBytes = RedirectStatsCsvWriter.Write(stats);
            var fileName = $"redirect-overview-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            var body =
                $"Redirect Manager overview for {DateTime.UtcNow:yyyy-MM-dd}:\n\n" +
                $"Total redirects: {stats.Total}\n" +
                $"Active: {stats.Active}\n" +
                $"Inactive: {stats.Inactive}\n" +
                $"Active with 0 hits in the last 30 days: {stats.StaleRedirects.Count}\n\n" +
                "Full breakdown attached as CSV.";

            await SendEmailWithAttachmentAsync(
                options.SummaryEmailTo,
                "Redirect Manager - Overview Report",
                body,
                fileName,
                csvBytes).ConfigureAwait(false);

            _lastSummaryEmailUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send scheduled redirect overview email");
        }
    }

    private List<RedirectEntry> FetchRedirects()
    {
        using var db = FlushDatabaseFactory.Create(_connectionStrings.CurrentValue);
        return db.Fetch<RedirectEntry>($"SELECT * FROM {RedirectEntry.TableName} ORDER BY CreatedDate DESC");
    }

    private RedirectStatsBuilder.Stats BuildStats()
    {
        using var db = FlushDatabaseFactory.Create(_connectionStrings.CurrentValue);
        var redirects = db.Fetch<RedirectEntry>($"SELECT * FROM {RedirectEntry.TableName} ORDER BY CreatedDate DESC");
        var windowCounts = RedirectStatsBuilder.FetchHitWindowCounts(db);
        return RedirectStatsBuilder.Build(redirects, windowCounts);
    }

    private void WriteToFolder(string folderPath, string fileName, byte[] csvBytes, int retentionCount)
    {
        Directory.CreateDirectory(folderPath);
        File.WriteAllBytes(Path.Combine(folderPath, fileName), csvBytes);

        var stale = Directory.GetFiles(folderPath, $"{BackupFilePrefix}*.csv")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(Math.Max(0, retentionCount));

        foreach (var file in stale)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
                // Best-effort cleanup — a locked/undeletable old backup shouldn't
                // fail the backup that just succeeded.
            }
        }
    }

    private async Task SendEmailWithAttachmentAsync(string emailTo, string subject, string body, string fileName, byte[] attachmentBytes)
    {
        var recipients = emailTo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (recipients.Length == 0)
        {
            return;
        }

        var from = _globalSettings.CurrentValue.Smtp?.From;
        if (string.IsNullOrWhiteSpace(from))
        {
            _logger.LogWarning(
                "Redirect Manager: an email destination is configured but no SMTP 'From' address is set " +
                "(Umbraco:CMS:Global:Smtp:From) — skipping email delivery.");
            return;
        }

        using var attachmentStream = new MemoryStream(attachmentBytes);
        var message = new EmailMessage(
            from,
            recipients,
            null,
            null,
            null,
            subject,
            body,
            isBodyHtml: false,
            attachments: new[] { new EmailMessageAttachment(attachmentStream, fileName) });

        // IEmailSender.SendAsync's signature differs between the two Umbraco
        // major versions this package targets: Umbraco 13's (net8.0) has no
        // "expires" parameter at all, while Umbraco 17/18's (net10.0) 2-arg
        // and 3-arg overloads are obsolete in 17.1.0 ("scheduled for removal
        // in Umbraco 18") and genuinely gone by Umbraco 18 — so a 2-arg call
        // compiles fine against 17.1.0 but throws MissingMethodException at
        // runtime against an 18.x host. Each branch below calls the one
        // overload whose exact signature is stable for that TFM's Umbraco
        // major version.
#if NET10_0_OR_GREATER
        await _emailSender.SendAsync(message, "RedirectManagerBackup", enableNotification: false, expires: null).ConfigureAwait(false);
#else
        await _emailSender.SendAsync(message, "RedirectManagerBackup", enableNotification: false).ConfigureAwait(false);
#endif
    }
}
