using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoundCloudReleaseTracker;

public sealed class AppConfig
{
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "http://127.0.0.1:8765/callback";
    public List<string> Genres { get; set; } = new() { "House", "Tech House", "Afro House" };
    public int PollMinutes { get; set; } = 15;
    public int LookbackHours { get; set; } = 24;
    public Dictionary<string, BpmRule> GenreBpmRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> FavoriteArtists { get; set; } = new();
    public List<string> BlacklistedArtists { get; set; } = new();
    public bool AutoDownload { get; set; }
    public string DownloadDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "SoundCloud New Releases");

    public static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundCloudReleaseTracker");

    public static string ConfigPath => Path.Combine(AppDir, "config.json");
    public static string TracksPath => Path.Combine(AppDir, "tracks.json");
    public static string PreviewCacheDir => Path.Combine(AppDir, "preview_cache");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppConfig Load()
    {
        Directory.CreateDirectory(AppDir);
        if (!File.Exists(ConfigPath)) return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDir);
        // Client Secret intentionally is not a property of AppConfig and therefore can never be serialized here.
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, ConfigPath, true);
    }

    public static string TryMigrateLegacyPlaintextSecret()
    {
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".soundcloud_release_tracker", "config.json");
        if (!File.Exists(legacyPath)) return "";

        try
        {
            var text = File.ReadAllText(legacyPath);
            var node = JsonNode.Parse(text) as JsonObject;
            if (node is null) return "";

            var secret = node["client_secret"]?.GetValue<string>() ?? "";
            if (!string.IsNullOrWhiteSpace(secret))
            {
                CredentialStore.WriteSecret(secret);
                node.Remove("client_secret");
                File.WriteAllText(legacyPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            return secret;
        }
        catch
        {
            return "";
        }
    }
}
