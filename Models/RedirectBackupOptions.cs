namespace Umbraco.RedirectManager.Models;

// Bound from the "RedirectManager:Backup" appsettings section. Disabled by
// default (opt-in) since it touches the file system and/or sends email.
public class RedirectBackupOptions
{
    public bool Enabled { get; set; } = false;

    // Folder to write timestamped CSV backups to. Null/blank disables the
    // file-backup destination (email can still be used on its own).
    public string? FolderPath { get; set; }

    public int IntervalHours { get; set; } = 24;

    // How many backup files to keep in FolderPath; older ones are deleted.
    public int RetentionCount { get; set; } = 30;

    // Comma-separated recipient list. Null/blank disables the email
    // destination (file backup can still be used on its own).
    public string? EmailTo { get; set; }
}
