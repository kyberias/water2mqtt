namespace water2mqtt;

public class ReadingStorageFile : IReadingStorage
{
    private TimeProvider time;

    public ReadingStorageFile(TimeProvider time)
    {
        this.time = time;

        if (File.Exists("knowngood.txt"))
        {
            var knownTxt = File.ReadAllText("knowngood.txt");
            var parts = knownTxt.Split();
            var parsed = decimal.Parse(parts[0]);

            LatestGood = Volume.FromCubicMeters(parsed);
            LatestGoodTimestamp = DateTimeOffset.Parse(parts[1]);
        }
    }

    private Volume? latestGood;
    public Volume? LatestGood
    {
        get => latestGood;
        set
        {
            latestGood = value;
            LatestGoodTimestamp = time.GetUtcNow();
            File.WriteAllText("knowngood.txt", $"{value.ToCubicMeters()} {LatestGoodTimestamp:O}");
            
        }
    }

    public DateTimeOffset? LatestGoodTimestamp { get; private set; }
}