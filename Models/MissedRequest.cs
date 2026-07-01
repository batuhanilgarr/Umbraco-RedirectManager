using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Umbraco.RedirectManager.Models;

[TableName(MissedRequest.TableName)]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class MissedRequest
{
    public const string TableName = "RedirectManagerMissedRequests";

    [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Path")]
    [Length(2048)]
    public string Path { get; set; } = string.Empty;

    // SHA-256 hex digest of the (already-truncated) Path. A unique index on Path
    // itself isn't viable on SQL Server (its row-size limit for index keys is far
    // smaller than 2048 nvarchar characters), so this fixed-length column carries
    // the uniqueness constraint instead, letting the upsert in
    // MissedRequestFlushService detect and recover from a concurrent-insert race
    // instead of silently accumulating duplicate rows for the same path.
    [Column("PathHash")]
    [Length(64)]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_RedirectManagerMissedRequests_PathHash")]
    public string PathHash { get; set; } = string.Empty;

    [Column("Domain")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Length(255)]
    public string? Domain { get; set; }

    [Column("HitCount")]
    [Constraint(Default = 1)]
    public int HitCount { get; set; } = 1;

    [Column("FirstSeenDate")]
    public DateTime FirstSeenDate { get; set; } = DateTime.UtcNow;

    [Column("LastSeenDate")]
    public DateTime LastSeenDate { get; set; } = DateTime.UtcNow;
}
