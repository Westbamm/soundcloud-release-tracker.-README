using System.Security.Cryptography;
using TwinTrack.Core;
using TwinTrack.Models;

namespace TwinTrack.Services;

public sealed class ScannerService
{
    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".wave", ".aac", ".m4a", ".ogg", ".opus", ".wma", ".aiff", ".aif", ".alac", ".ape", ".mka", ".mid", ".midi", ".ac3", ".dts"
    };

    private readonly AsyncPauseGate _pause = new();
    private CancellationTokenSource? _cts;
    public bool IsRunning { get; private set; }
    public bool IsPaused => _pause.IsPaused;
    public string? ActiveFolderId { get; private set; }

    public void Pause() => _pause.Pause();
    public void Resume() => _pause.Resume();
    public void Cancel() { _pause.Resume(); _cts?.Cancel(); }

    public async Task<ScanResult> ScanAsync(FolderModel folder, IProgress<ScanProgress> progress, CancellationToken outer = default)
    {
        if (IsRunning) throw new InvalidOperationException("Сканирование уже выполняется");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(outer);
        _cts = linked;
        var ct = linked.Token;
        IsRunning = true;
        ActiveFolderId = folder.Id;
        try
        {
            progress.Report(new ScanProgress(1, "Поиск аудиофайлов…", "enumerate"));
            var files = await EnumerateAsync(folder, progress, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var sizeGroups = files.GroupBy(x => x.Size).Where(g => g.Count() > 1).ToList();
            int candidates = sizeGroups.Sum(g => g.Count());
            int quickDone = 0;
            foreach (var group in sizeGroups)
            {
                foreach (var item in group)
                {
                    await _pause.WaitAsync(ct).ConfigureAwait(false);
                    item.QuickSig = await QuickHashAsync(item.FullPath, item.Size, ct).ConfigureAwait(false);
                    quickDone++;
                    double p = 10 + (candidates == 0 ? 0 : 15d * quickDone / candidates);
                    progress.Report(new ScanProgress(p, $"Быстрое сравнение: {quickDone}/{candidates}", "quick"));
                }
            }

            var quickGroups = sizeGroups
                .SelectMany(g => g.GroupBy(x => x.QuickSig).Where(q => q.Count() > 1))
                .ToList();
            long totalBytes = quickGroups.Sum(g => g.Sum(x => x.Size));
            long doneBytes = 0;
            foreach (var group in quickGroups)
            {
                foreach (var item in group)
                {
                    item.ContentHash = await FullHashAsync(item.FullPath, item.Size, progress, totalBytes, doneBytes, ct).ConfigureAwait(false);
                    doneBytes += item.Size;
                }
            }

            var duplicates = quickGroups
                .SelectMany(g => g.GroupBy(x => x.ContentHash).Where(h => h.Count() > 1).Select(h => h.ToList()))
                .Where(g => g.Count > 1)
                .OrderBy(g => g[0].Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var item in duplicates.SelectMany(x => x))
            {
                var meta = MetadataReader.Read(item.FullPath);
                item.Artist = meta.Artist;
                item.Title = meta.Title;
            }

            int dupCount = duplicates.Sum(g => g.Count);
            long dupBytes = duplicates.Sum(g => g.Sum(x => x.Size));
            progress.Report(new ScanProgress(100, "Сканирование завершено", "done"));
            return new ScanResult
            {
                FolderId = folder.Id,
                FolderPath = folder.Path,
                FileCount = files.Count,
                Groups = duplicates,
                Summary = new ResultSummary { DuplicateCount = dupCount, GroupCount = duplicates.Count, DuplicateBytes = dupBytes },
                ScannedAt = DateTime.UtcNow.ToString("O")
            };
        }
        finally
        {
            IsRunning = false;
            ActiveFolderId = null;
            _pause.Resume();
            _cts = null;
        }
    }

    public async Task<int> CountAudioAsync(string root, CancellationToken ct = default)
    {
        return await Task.Run(() => EnumeratePathsSafe(root).Count(p => AudioExt.Contains(Path.GetExtension(p))), ct).ConfigureAwait(false);
    }

    private async Task<List<FileItem>> EnumerateAsync(FolderModel folder, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        var paths = await Task.Run(() => EnumeratePathsSafe(folder.Path).Where(p => AudioExt.Contains(Path.GetExtension(p))).ToList(), ct).ConfigureAwait(false);
        var outList = new List<FileItem>(paths.Count);
        for (int i = 0; i < paths.Count; i++)
        {
            await _pause.WaitAsync(ct).ConfigureAwait(false);
            string p = paths[i];
            try
            {
                var fi = new FileInfo(p);
                string rel = Path.GetRelativePath(folder.Path, p);
                string dir = Path.GetDirectoryName(rel) ?? folder.Name;
                if (dir == ".") dir = folder.Name;
                outList.Add(new FileItem { Name = fi.Name, Size = fi.Length, FullPath = fi.FullName, RelPath = rel, Dir = dir, FolderId = folder.Id });
            }
            catch { }
            if ((i & 127) == 0)
            {
                double pct = paths.Count == 0 ? 10 : 1 + 9d * i / paths.Count;
                progress.Report(new ScanProgress(pct, $"Найдено файлов: {outList.Count}", "enumerate"));
            }
        }
        return outList;
    }

    private static IEnumerable<string> EnumeratePathsSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            string[]? dirs = null;
            string[]? files = null;
            try { dirs = Directory.GetDirectories(dir); } catch { }
            try { files = Directory.GetFiles(dir); } catch { }
            if (files != null) foreach (var f in files) yield return f;
            if (dirs != null) foreach (var d in dirs) pending.Push(d);
        }
    }

    private async Task<string> QuickHashAsync(string path, long length, CancellationToken ct)
    {
        await _pause.WaitAsync(ct).ConfigureAwait(false);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        const int chunk = 64 * 1024;
        long[] positions = length > chunk * 2L ? new[] { 0L, Math.Max(0, (length - chunk) / 2), Math.Max(0, length - chunk) } : length > chunk ? new[] { 0L, Math.Max(0, length - chunk) } : new[] { 0L };
        var buffer = new byte[chunk];
        foreach (var pos in positions.Distinct())
        {
            await _pause.WaitAsync(ct).ConfigureAwait(false);
            fs.Position = pos;
            int n = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(chunk, Math.Max(0, length - pos))), ct).ConfigureAwait(false);
            if (n > 0) sha.AppendData(buffer, 0, n);
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    private async Task<string> FullHashAsync(string path, long length, IProgress<ScanProgress> progress, long totalBytes, long doneBefore, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[256 * 1024];
        long local = 0;
        long lastTick = Environment.TickCount64;
        while (true)
        {
            await _pause.WaitAsync(ct).ConfigureAwait(false);
            int n = await fs.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n <= 0) break;
            sha.AppendData(buffer, 0, n);
            local += n;
            long now = Environment.TickCount64;
            if (now - lastTick >= 100)
            {
                double frac = totalBytes <= 0 ? 1 : Math.Clamp((doneBefore + local) / (double)totalBytes, 0, 1);
                progress.Report(new ScanProgress(25 + 75 * frac, $"Точное сравнение SHA-256: {(int)(frac * 100)}%", "sha256", IsPaused));
                lastTick = now;
            }
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
