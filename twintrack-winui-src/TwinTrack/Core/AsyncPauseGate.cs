namespace TwinTrack.Core;
public sealed class AsyncPauseGate
{
    private volatile TaskCompletionSource<bool> _resume = Completed();
    public bool IsPaused { get; private set; }
    private static TaskCompletionSource<bool> Completed(){ var t=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); t.TrySetResult(true); return t; }
    public void Pause(){ if(IsPaused)return; IsPaused=true; _resume=new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public void Resume(){ if(!IsPaused)return; IsPaused=false; _resume.TrySetResult(true); }
    public async ValueTask WaitAsync(CancellationToken ct){ var task=_resume.Task; if(task.IsCompleted)return; await task.WaitAsync(ct).ConfigureAwait(false); }
}
