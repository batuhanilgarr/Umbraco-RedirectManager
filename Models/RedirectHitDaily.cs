using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Umbraco.RedirectManager.Models;

[TableName(RedirectHitDaily.TableName)]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class RedirectHitDaily
{
    public const string TableName = "RedirectManagerHitDaily";

    [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
    [Column("Id")]
    public int Id { get; set; }

    [Column("RedirectId")]
    public int RedirectId { get; set; }

    // Date-only (time component zeroed) — one row per redirect per UTC day.
    [Column("HitDate")]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_RedirectManagerHitDaily_RedirectId_HitDate", ForColumns = "RedirectId,HitDate")]
    public DateTime HitDate { get; set; }

    [Column("HitCount")]
    [Constraint(Default = 0)]
    public int HitCount { get; set; } = 0;
}
