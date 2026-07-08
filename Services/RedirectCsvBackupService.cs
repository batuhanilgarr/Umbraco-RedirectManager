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
                await SendByEmailAsync(options.EmailTo, fileName, csvBytes).ConfigureAwait(false);
            }

            _lastBackupUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run scheduled redirect CSV backup");
        }
    }

    private List<RedirectEntry> FetchRedirects()
    {
        using var db = FlushDatabaseFactory.Create(_connectionStrings.CurrentValue);
        return db.Fetch<RedirectEntry>($"SELECT * FROM {RedirectEntry.TableName} ORDER BY CreatedDate DESC");
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

    private async Task SendByEmailAsync(string emailTo, string fileName, byte[] csvBytes)
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
                "Redirect CSV backup: EmailTo is configured but no SMTP 'From' address is set " +
                "(Umbraco:CMS:Global:Smtp:From) — skipping email delivery.");
            return;
        }

        using var attachmentStream = new MemoryStream(csvBytes);
        var message = new EmailMessage(
            from,
            recipients,
            null,
            null,
            null,
            "Redirect Manager - Scheduled CSV Backup",
            $"Attached is the scheduled redirect backup generated on {DateTime.UtcNow:u}.",
            isBodyHtml: false,
            attachments: new[] { new EmailMessageAttachment(attachmentStream, fileName) });

        await _emailSender.SendAsync(message, "RedirectManagerBackup").ConfigureAwait(false);
    }
}
