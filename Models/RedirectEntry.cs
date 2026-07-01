using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Umbraco.RedirectManager.Models;

[TableName(RedirectEntry.TableName)]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class RedirectEntry
{
    public const string TableName = "RedirectManagerEntries";

    [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
    [Column("Id")]
    public int Id { get; set; }

    [Column("OldUrl")]
    [Length(2048)]
    public string OldUrl { get; set; } = string.Empty;

    [Column("NewUrl")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(2048)]
    public string? NewUrl { get; set; }

    [Column("Domain")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? Domain { get; set; }

    [Column("Description")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(2048)]
    public string? Description { get; set; }

    [Column("StatusCode")]
    public int StatusCode { get; set; } = 301;

    [Column("CreatedDate")]
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [Column("UpdatedDate")]
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    [Column("IsRegex")]
    [Constraint(Default = false)]
    public bool IsRegex { get; set; } = false;

    [Column("HitCount")]
    [Constraint(Default = 0)]
    public int HitCount { get; set; } = 0;

    [Column("LastHitDate")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? LastHitDate { get; set; }
}
