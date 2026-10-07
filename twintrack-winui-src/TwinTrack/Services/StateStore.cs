using System.Text.Json;
using TwinTrack.Models;

namespace TwinTrack.Services;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    public string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TWINTRACK");
    public string ResultsDir => Path.Combine(Root, "results");
    public string StatePath => Path.Combine(Root, "state.json");

    public StateStore()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ResultsDir);
    }

    public async Task<AppState> LoadAsync()
    {
        try
        {
            if (!File.Exists(StatePath)) return new AppState();
            await using var s = File.OpenRead(StatePath);
            return await JsonSerializer.DeserializeAsync<AppState>(s, Json) ?? new AppState();
        }
        catch { return new AppState(); }
    }

    public Task SaveAsync(AppState state) => WriteAtomicAsync(StatePath, state);

    public async Task<ScanResult?> LoadResultAsync(string folderId)
    {
        var p = ResultPath(folderId);
        try
        {
            if (!File.Exists(p)) return null;
            await using var s = File.OpenRead(p);
            return await JsonSerializer.DeserializeAsync<ScanResult>(s, Json);
        }
        catch { return null; }
    }

    public Task SaveResultAsync(ScanResult result) => WriteAtomicAsync(ResultPath(result.FolderId), result);
    public string ResultPath(string id) => Path.Combine(ResultsDir, id + ".json");

    private static async Task WriteAtomicAsync<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        await using (var s = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
            await JsonSerializer.SerializeAsync(s, value, Json);
        File.Move(tmp, path, true);
    }
}
