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
