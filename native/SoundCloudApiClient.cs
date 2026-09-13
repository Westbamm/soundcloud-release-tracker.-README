using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;

namespace SoundCloudReleaseTracker;

internal sealed class SoundCloudApiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly string _clientId;
    private readonly string _clientSecret;
    private string _accessToken = "";
    private DateTime _tokenExpiryUtc = DateTime.MinValue;

    public SoundCloudApiClient(string clientId, string clientSecret)
    {
        _clientId = clientId.Trim();
        _clientSecret = clientSecret.Trim();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SoundCloudReleaseTracker/5.0");
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTime.UtcNow < _tokenExpiryUtc.AddMinutes(-1))
            return _accessToken;

        using var content = new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret
        });
        using var resp = await _http.PostAsync("https://secure.soundcloud.com/oauth/token", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"SoundCloud OAuth {(int)resp.StatusCode}: {Trim(body)}");
        using var doc = JsonDocument.Parse(body);
        _accessToken = doc.RootElement.GetProperty("access_token").GetString() ?? "";
        var expires = doc.RootElement.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var seconds) ? seconds : 3600;
        _tokenExpiryUtc = DateTime.UtcNow.AddSeconds(Math.Max(300, expires));
        return _accessToken;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"SoundCloud API {(int)resp.StatusCode}: {Trim(body)}");
        return JsonDocument.Parse(body);
    }

    public async Task TestAsync(CancellationToken ct) =>
        _ = await SearchTracksAsync("", DateTime.UtcNow.AddHours(-1), null, null, 1, ct);

    public async Task<List<TrackEntry>> SearchTracksAsync(
        string genre, DateTime createdFromUtc, int? bpmFrom, int? bpmTo, int limit, CancellationToken ct)
    {
        var q = HttpUtility.ParseQueryString("");
        q["linked_partitioning"] = "true";
        q["limit"] = Math.Clamp(limit, 1, 200).ToString();
        q["access"] = "playable,preview";
        if (!string.IsNullOrWhiteSpace(genre)) q["genres"] = genre;
        q["created_at[from]"] = createdFromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
        if (bpmFrom.HasValue) q["bpm[from]"] = bpmFrom.Value.ToString();
        if (bpmTo.HasValue) q["bpm[to]"] = bpmTo.Value.ToString();

        var url = "https://api.soundcloud.com/tracks?" + q;
        var results = new List<TrackEntry>();
        for (var page = 0; page < 2 && !string.IsNullOrWhiteSpace(url); page++)
        {
            using var doc = await GetJsonAsync(url, ct);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray()) results.Add(MapTrack(el));
                break;
            }
            if (root.TryGetProperty("collection", out var collection) && collection.ValueKind == JsonValueKind.Array)
                foreach (var el in collection.EnumerateArray()) results.Add(MapTrack(el));
            url = root.TryGetProperty("next_href", out var next) ? next.GetString() ?? "" : "";
        }
        return results.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<string> DownloadPreviewAsync(TrackEntry track, CancellationToken ct)
    {
        Directory.CreateDirectory(AppConfig.PreviewCacheDir);
        var safe = string.Concat(track.Urn.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        var path = Path.Combine(AppConfig.PreviewCacheDir, safe + ".preview.mp3");
        if (File.Exists(path) && new FileInfo(path).Length > 1024) return path;

        var urn = Uri.EscapeDataString(track.Urn).Replace("%3A", ":");
        using var doc = await GetJsonAsync($"https://api.soundcloud.com/tracks/{urn}/streams", ct);
        if (!doc.RootElement.TryGetProperty("preview_mp3_128_url", out var preview) || string.IsNullOrWhiteSpace(preview.GetString()))
            throw new InvalidOperationException("Для этого трека SoundCloud не предоставляет preview.");

        using var resp = await _http.GetAsync(preview.GetString(), HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(path);
        await resp.Content.CopyToAsync(fs, ct);
        return path;
    }

    public async Task<string> DownloadTrackAsync(TrackEntry track, string baseDir, CancellationToken ct)
    {
        if (!track.Downloadable || string.IsNullOrWhiteSpace(track.DownloadUrl))
            throw new InvalidOperationException("Автор не разрешил официальное скачивание этого трека.");

        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, track.DownloadUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Download {(int)resp.StatusCode}");

        var genreDir = Path.Combine(baseDir, SafeFile(track.Genre.Length > 0 ? track.Genre : "Other"));
        Directory.CreateDirectory(genreDir);
        var ext = resp.Content.Headers.ContentType?.MediaType switch
        {
            "audio/wav" => ".wav",
            "audio/flac" => ".flac",
            "audio/x-flac" => ".flac",
            _ => ".mp3"
        };
        var path = UniquePath(Path.Combine(genreDir, $"{SafeFile(track.Artist)} - {SafeFile(track.Title)}{ext}"));
        await using var fs = File.Create(path);
        await resp.Content.CopyToAsync(fs, ct);
        return path;
    }

    private static TrackEntry MapTrack(JsonElement el)
    {
        string Str(string name) => el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
        var urn = Str("urn");
        if (string.IsNullOrWhiteSpace(urn) && el.TryGetProperty("id", out var id)) urn = "soundcloud:tracks:" + id;
        var artist = Str("metadata_artist");
        if (string.IsNullOrWhiteSpace(artist) && el.TryGetProperty("user", out var user) && user.TryGetProperty("username", out var un))
            artist = un.GetString() ?? "Unknown artist";

        int? bpm = null;
        if (el.TryGetProperty("bpm", out var bp) && bp.ValueKind == JsonValueKind.Number && bp.TryGetInt32(out var b)) bpm = b;

        return new TrackEntry
        {
            Urn = urn,
            Title = Str("title"),
            Artist = artist,
            Genre = Str("genre"),
            CreatedAt = Str("created_at"),
            PermalinkUrl = Str("permalink_url"),
            ArtworkUrl = Str("artwork_url"),
            Duration = el.TryGetProperty("duration", out var d) && d.TryGetInt32(out var di) ? di : 0,
            Bpm = bpm,
            Downloadable = el.TryGetProperty("downloadable", out var dl) && dl.ValueKind == JsonValueKind.True,
            DownloadUrl = Str("download_url"),
            FirstSeenUtc = DateTime.UtcNow
        };
    }

    private static string Trim(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim()[..Math.Min(300, s.Length)];
    private static string SafeFile(string s)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(s) ? "track" : s.Trim();
    }
    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i=2;i<10000;i++)
        {
            var p=Path.Combine(dir,$"{stem} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
        throw new IOException("Не удалось подобрать свободное имя файла.");
    }
}
