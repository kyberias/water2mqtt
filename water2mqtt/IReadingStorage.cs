namespace water2mqtt;

public interface IReadingStorage
{
    Volume? LatestGood { get; set; }
    DateTimeOffset? LatestGoodTimestamp { get; }
}