using System.Text.Json.Serialization;

namespace SoundCloudReleaseTracker;

public sealed class BpmRule
{
    public int? From { get; set; }
    public int? To { get; set; }
}

public sealed class TrackEntry
{
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Genre { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string PermalinkUrl { get; set; } = "";
    public string ArtworkUrl { get; set; } = "";
    public int Duration { get; set; }
    public int? Bpm { get; set; }
    public bool Downloadable { get; set; }
    public string DownloadUrl { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DownloadedUtc { get; set; }
    public string DownloadPath { get; set; } = "";
}
