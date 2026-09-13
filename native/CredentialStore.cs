using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SoundCloudReleaseTracker;

internal static class CredentialStore
{
    private const string SecretTarget = "SoundCloudReleaseTracker:SoundCloudClientSecret";
    private const string ClientTokenTarget = "SoundCloudReleaseTracker:ClientAccessToken";
    private const string ClientTokenExpiryTarget = "SoundCloudReleaseTracker:ClientAccessTokenExpiry";
    private const string ClientTokenIdTarget = "SoundCloudReleaseTracker:ClientAccessTokenClientId";

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static void WriteSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Client Secret пуст.");

        WriteValue(SecretTarget, secret, "Release Radar SoundCloud API secret");
    }

    public static string ReadSecret() => ReadValue(SecretTarget);

    public static void DeleteSecret()
    {
        DeleteValue(SecretTarget);
        DeleteClientAccessToken();
    }

    public static void SaveClientAccessToken(string clientId, string accessToken, DateTime expiresUtc)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken))
            return;

        WriteValue(ClientTokenIdTarget, clientId, "Release Radar SoundCloud client id for cached token");
        WriteValue(ClientTokenTarget, accessToken, "Release Radar SoundCloud access token");
        WriteValue(
            ClientTokenExpiryTarget,
            expiresUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            "Release Radar SoundCloud token expiry");
    }

    public static bool TryReadClientAccessToken(
        string clientId,
        out string accessToken,
        out DateTime expiresUtc)
    {
        accessToken = "";
        expiresUtc = DateTime.MinValue;

        try
        {
            var storedClientId = ReadValue(ClientTokenIdTarget);
            if (!string.Equals(storedClientId, clientId, StringComparison.Ordinal))
                return false;

            var token = ReadValue(ClientTokenTarget);
            var expiryRaw = ReadValue(ClientTokenExpiryTarget);

            if (string.IsNullOrWhiteSpace(token) ||
                !DateTime.TryParse(
                    expiryRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var expiry))
                return false;

            expiry = expiry.ToUniversalTime();

            // Keep a small safety margin, but do not refresh a valid token early enough
            // to create unnecessary OAuth traffic.
            if (DateTime.UtcNow >= expiry.AddSeconds(-30))
            {
                DeleteClientAccessToken();
                return false;
            }

            accessToken = token;
            expiresUtc = expiry;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void DeleteClientAccessToken()
    {
        DeleteValue(ClientTokenTarget);
        DeleteValue(ClientTokenExpiryTarget);
        DeleteValue(ClientTokenIdTarget);
    }

    private static void WriteValue(string target, string value, string comment)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);

            var cred = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = target,
                Comment = comment,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref cred, 0))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Не удалось сохранить защищённые данные.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static string ReadValue(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var ptr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorNotFound)
                return "";

            throw new Win32Exception(
                err,
                "Не удалось прочитать защищённые данные.");
        }

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                return "";

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    private static void DeleteValue(string target)
    {
        if (CredDelete(target, CredTypeGeneric, 0))
            return;

        var err = Marshal.GetLastWin32Error();
        if (err != ErrorNotFound)
            throw new Win32Exception(
                err,
                "Не удалось удалить защищённые данные.");
    }
}
