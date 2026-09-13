using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SoundCloudReleaseTracker;

internal sealed record AutoSetupResult(string ClientId, string ClientSecret);

internal static class SoundCloudAutoSetup
{
    private const string ScriptUrl =
        "https://raw.githubusercontent.com/soundcloud/api/a1afaea44b8a3ba6397a12dac4c6b583d1c8d9d8/scripts/sc-api-auth.mjs";

    // Git blob SHA from the official soundcloud/api repository for the pinned script above.
    private const string ExpectedGitBlobSha1 = "1ebb4b2373097259ce8024d7b352b0ea4e3a7566";

    public static async Task<AutoSetupResult> RunAsync(Action<string>? status, CancellationToken ct)
    {
        var nodePath = Path.Combine(AppContext.BaseDirectory, "tools", "node.exe");
        if (!File.Exists(nodePath))
            throw new InvalidOperationException(
                "В комплекте не найден защищённый Node.js runtime. Переустановите приложение из полного ZIP.");

        status?.Invoke("Загружаю официальный helper SoundCloud…");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SoundCloudReleaseTracker/5.4");
        var scriptBytes = await http.GetByteArrayAsync(ScriptUrl, ct);

        var actualBlobSha = ComputeGitBlobSha1(scriptBytes);
        if (!string.Equals(actualBlobSha, ExpectedGitBlobSha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Проверка официального SoundCloud helper не пройдена. Ничего не запускается. Обновите приложение.");

        var toolsDir = Path.Combine(AppConfig.AppDir, "official-tools");
        Directory.CreateDirectory(toolsDir);
        var scriptPath = Path.Combine(toolsDir, "sc-api-auth.mjs");
        await File.WriteAllBytesAsync(scriptPath, scriptBytes, ct);

        status?.Invoke("Открываю официальную страницу активации SoundCloud…");

        var psi = new ProcessStartInfo
        {
            FileName = nodePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("--remote");
        var appName = "Music Monitor " + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        psi.ArgumentList.Add("--name");
        psi.ArgumentList.Add(appName);
        psi.ArgumentList.Add("--description");
        psi.ArgumentList.Add("Personal desktop app for discovering public SoundCloud tracks by genre and BPM");
        psi.ArgumentList.Add("--website");
        psi.ArgumentList.Add("https://github.com/Westbamm/soundcloud-release-tracker.-README");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить официальный SoundCloud helper.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(12));

        try
        {
            status?.Invoke("Подтверди подключение на странице SoundCloud…");
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var msg = SanitizeError(stderr);
            if (string.IsNullOrWhiteSpace(msg)) msg = "Официальный SoundCloud helper завершился с ошибкой.";
            throw new InvalidOperationException(msg);
        }

        var clientId = "";
        var clientSecret = "";
        foreach (var rawLine in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("client_id=", StringComparison.Ordinal))
                clientId = line["client_id=".Length..].Trim();
            else if (line.StartsWith("client_secret=", StringComparison.Ordinal))
                clientSecret = line["client_secret=".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException(
                "SoundCloud не вернул Client ID/Secret. Проверь, доступна ли регистрация API-приложений для твоего аккаунта.");

        status?.Invoke("Сохраняю ключ безопасно в Windows Credential Manager…");
        return new AutoSetupResult(clientId, clientSecret);
    }

    private static string ComputeGitBlobSha1(byte[] content)
    {
        var prefix = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        var combined = new byte[prefix.Length + content.Length];
        Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
        Buffer.BlockCopy(content, 0, combined, prefix.Length, content.Length);
        return Convert.ToHexString(SHA1.HashData(combined)).ToLowerInvariant();
    }

    private static string SanitizeError(string stderr)
    {
        var text = (stderr ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (text.Contains("application_creation_not_available", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Artist Pro", StringComparison.OrdinalIgnoreCase))
            return "SoundCloud не разрешил создать API-приложение для этого аккаунта. Для регистрации сейчас нужен Artist Pro.";

        if (text.Contains("application_name_not_allowed", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("application name is not allowed", StringComparison.OrdinalIgnoreCase))
            return "SoundCloud отклонил имя API-приложения. Попробуйте подключение ещё раз — приложение создаст новое нейтральное имя.";

        if (text.Length > 700) text = text[..700] + "…";
        return text;
    }
}
