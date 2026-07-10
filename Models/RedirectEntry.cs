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

    // A/B test: when set, a visitor is split between NewUrl (variant A) and
    // VariantBUrl (variant B) by VariantBWeight (percentage sent to B),
    // sticky per-visitor via a cookie. Null VariantBUrl means "not an A/B
    // test" — NewUrl/HitCount/LastHitDate behave exactly as before.
    [Column("VariantBUrl")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(2048)]
    public string? VariantBUrl { get; set; }

    [Column("VariantBWeight")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? VariantBWeight { get; set; }

    [Column("VariantBHitCount")]
    [Constraint(Default = 0)]
    public int VariantBHitCount { get; set; } = 0;

    [Column("VariantBLastHitDate")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? VariantBLastHitDate { get; set; }

    [Column("PreserveQueryString")]
    [Constraint(Default = false)]
    public bool PreserveQueryString { get; set; } = false;

    [Column("ValidFrom")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ValidFrom { get; set; }

    [Column("ValidUntil")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ValidUntil { get; set; }
}
