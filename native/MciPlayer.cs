using System.Runtime.InteropServices;
using System.Text;

namespace SoundCloudReleaseTracker;

internal sealed class MciPlayer : IDisposable
{
    private const string Alias = "scpreview";

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr hwndCallback);

    public void Play(string path)
    {
        Stop();
        var escaped = path.Replace("\"", "\"\"");
        Check($"open \"{escaped}\" type mpegvideo alias {Alias}");
        Check($"play {Alias}");
    }

    public void Stop()
    {
        mciSendString($"stop {Alias}", null, 0, IntPtr.Zero);
        mciSendString($"close {Alias}", null, 0, IntPtr.Zero);
    }

    private static void Check(string command)
    {
        var code = mciSendString(command, null, 0, IntPtr.Zero);
        if (code != 0) throw new InvalidOperationException($"Windows audio error: {code}");
    }

    public void Dispose() => Stop();
}
