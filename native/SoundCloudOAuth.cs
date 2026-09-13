using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SoundCloudReleaseTracker;

internal sealed record SoundCloudLoginResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresUtc,
    string Username,
    string UserUrn
);

internal static class SoundCloudOAuth
{
    public const string DefaultRedirectUri = "http://127.0.0.1:8765/callback";

    public static async Task<SoundCloudLoginResult> LoginAsync(
        string clientId,
        string clientSecret,
        string redirectUri,
        Action<string>? status,
        CancellationToken ct)
    {
        clientId = clientId.Trim();
        clientSecret = clientSecret.Trim();
        redirectUri = redirectUri.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Для входа нужны Client ID и Client Secret приложения SoundCloud.");

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect) ||
            !IPAddress.TryParse(redirect.Host, out var ip) ||
            !IPAddress.IsLoopback(ip))
            throw new InvalidOperationException(
                "Для desktop-входа в этой версии Redirect URI должен быть loopback-адресом, например http://127.0.0.1:8765/callback.");

        var verifierBytes = RandomNumberGenerator.GetBytes(64);
        var verifier = Base64Url(verifierBytes);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));

        var listener = new TcpListener(ip, redirect.Port);
        listener.Start(1);
        try
        {
            var authorizeUrl =
                "https://secure.soundcloud.com/authorize" +
                $"?client_id={Uri.EscapeDataString(clientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                "&response_type=code" +
                $"&code_challenge={Uri.EscapeDataString(challenge)}" +
                "&code_challenge_method=S256" +
                $"&state={Uri.EscapeDataString(state)}";

            status?.Invoke("Открываю официальный SoundCloud в браузере…");
            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

            status?.Invoke("Ожидаю подтверждение входа в браузере…");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));

            using var tcp = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(requestLine))
                throw new InvalidOperationException("Не удалось получить OAuth callback.");

            var parts = requestLine.Split(' ');
            if (parts.Length < 2)
                throw new InvalidOperationException("Некорректный OAuth callback.");

            var callbackUri = new Uri($"http://{redirect.Host}:{redirect.Port}{parts[1]}");
            var query = ParseQuery(callbackUri.Query);

            await DrainHeadersAsync(reader, timeout.Token);

            var returnedState = query.TryGetValue("state", out var rs) ? rs : "";
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(state),
                    Encoding.UTF8.GetBytes(returnedState)))
            {
                await WriteBrowserResponseAsync(stream, false, "Проверка безопасности state не пройдена.");
                throw new InvalidOperationException("OAuth state не совпал. Вход отменён.");
            }

            if (query.TryGetValue("error", out var error))
            {
                var description = query.TryGetValue("error_description", out var d) ? d : error;
                await WriteBrowserResponseAsync(stream, false, description);
                throw new InvalidOperationException($"SoundCloud отклонил вход: {description}");
            }

            if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            {
                await WriteBrowserResponseAsync(stream, false, "SoundCloud не вернул authorization code.");
                throw new InvalidOperationException("SoundCloud не вернул authorization code.");
            }

            status?.Invoke("Получаю токен SoundCloud…");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SoundCloudReleaseTracker/5.1");

            using var tokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = verifier,
                ["code"] = code
            });

            using var tokenResponse = await http.PostAsync(
                "https://secure.soundcloud.com/oauth/token", tokenContent, timeout.Token);
            var tokenBody = await tokenResponse.Content.ReadAsStringAsync(timeout.Token);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                await WriteBrowserResponseAsync(stream, false, "Не удалось завершить вход в SoundCloud.");
                throw new InvalidOperationException(
                    $"SoundCloud OAuth {(int)tokenResponse.StatusCode}: {Trim(tokenBody)}");
            }

            using var tokenDoc = JsonDocument.Parse(tokenBody);
            var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString() ?? "";
            var refreshToken = tokenDoc.RootElement.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString() ?? ""
                : "";
            var expiresSeconds = tokenDoc.RootElement.TryGetProperty("expires_in", out var ei) &&
                                 ei.TryGetInt32(out var sec)
                ? sec
                : 3600;

            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("SoundCloud не вернул access token.");

            using var meRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.soundcloud.com/me");
            meRequest.Headers.TryAddWithoutValidation("Authorization", $"OAuth {accessToken}");
            using var meResponse = await http.SendAsync(meRequest, timeout.Token);
            var meBody = await meResponse.Content.ReadAsStringAsync(timeout.Token);
            if (!meResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"SoundCloud /me {(int)meResponse.StatusCode}: {Trim(meBody)}");

            using var meDoc = JsonDocument.Parse(meBody);
            var username = meDoc.RootElement.TryGetProperty("username", out var un)
                ? un.GetString() ?? "SoundCloud user"
                : "SoundCloud user";
            var userUrn = meDoc.RootElement.TryGetProperty("urn", out var urn)
                ? urn.GetString() ?? ""
                : "";

            await WriteBrowserResponseAsync(
                stream, true,
                $"Вход выполнен как {WebUtility.HtmlEncode(username)}. Можно вернуться в SoundCloud Release Tracker.");

            return new SoundCloudLoginResult(
                accessToken,
                refreshToken,
                DateTime.UtcNow.AddSeconds(Math.Max(300, expiresSeconds)),
                username,
                userUrn);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task DrainHeadersAsync(StreamReader reader, CancellationToken ct)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) return;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var raw = query.StartsWith('?') ? query[1..] : query;
        foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace("+", " "));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace("+", " ")) : "";
            result[key] = value;
        }
        return result;
    }

    private static async Task WriteBrowserResponseAsync(NetworkStream stream, bool success, string message)
    {
        var title = success ? "SoundCloud подключён" : "Ошибка подключения";
        var body = $"""
<!doctype html>
<html lang="ru">
<head><meta charset="utf-8"><title>{title}</title></head>
<body style="font-family:Segoe UI,Arial,sans-serif;max-width:720px;margin:60px auto;padding:0 24px">
<h1>{title}</h1>
<p>{message}</p>
<p>Это окно можно закрыть.</p>
</body>
</html>
""";
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Trim(string value)
    {
        var cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return cleaned[..Math.Min(cleaned.Length, 400)];
    }
}
