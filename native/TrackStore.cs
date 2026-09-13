using System.IO;
using System.Text.Json;

namespace SoundCloudReleaseTracker;

internal sealed class TrackStore
{
    private readonly string _path = AppConfig.TracksPath;
    private readonly Dictionary<string, TrackEntry> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public TrackStore()
    {
        Directory.CreateDirectory(AppConfig.AppDir);
        if (!File.Exists(_path)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<TrackEntry>>(File.ReadAllText(_path), Opts) ?? new();
            foreach (var t in list.Where(x => !string.IsNullOrWhiteSpace(x.Urn))) _tracks[t.Urn] = t;
        }
        catch { }
    }

    public bool Upsert(TrackEntry track)
    {
        var isNew = !_tracks.ContainsKey(track.Urn);
        if (!isNew)
        {
            var old = _tracks[track.Urn];
            track.FirstSeenUtc = old.FirstSeenUtc;
            track.DownloadedUtc = old.DownloadedUtc;
            track.DownloadPath = old.DownloadPath;
        }
        _tracks[track.Urn] = track;
        Save();
        return isNew;
    }

    public void MarkDownloaded(string urn, string path)
    {
        if (_tracks.TryGetValue(urn, out var t))
        {
            t.DownloadedUtc = DateTime.UtcNow;
            t.DownloadPath = path;
            Save();
        }
    }

    public IReadOnlyList<TrackEntry> All() =>
        _tracks.Values.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.FirstSeenUtc).ToList();

    public void Clear()
    {
        _tracks.Clear();
        Save();
    }

    private void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_tracks.Values.ToList(), Opts));
        File.Move(tmp, _path, true);
    }
}
