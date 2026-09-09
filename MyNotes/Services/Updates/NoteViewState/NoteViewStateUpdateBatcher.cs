using MyNotes.Application.Contracts.Notes.Models;
using MyNotes.Debugging;

namespace MyNotes.Services.Updates.NoteViewState;

internal sealed class NoteViewStateUpdateBatcher : IUpdateBatcher<string, NoteViewStatePatchDto>
{
  private readonly TimeProvider BatchTimeProvider;
  private readonly IUpdateDispatcher<NoteViewStatePatchDto> ViewStateUpdateDispatcher;

  private readonly TimeSpan _batchTimeSpan = TimeSpan.FromMilliseconds(500);
  private ITimer? _batchTimer;
  private readonly Dictionary<string, NoteViewStatePatchDto> _pendingEntries = [];
  private bool HasPendingPatch => _pendingEntries.Count > 0;
  private readonly Lock _pendingLock = new();

  public NoteViewStateUpdateBatcher(TimeProvider timeProvider, IUpdateDispatcher<NoteViewStatePatchDto> viewStatePersistenceDispatcher)
  {
    BatchTimeProvider = timeProvider;
    ViewStateUpdateDispatcher = viewStatePersistenceDispatcher;
  }

  public void AddOrMerge(string key, NoteViewStatePatchDto patch)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted), this);

    lock (_pendingLock)
    {
      if (!HasPendingPatch)
      {
        _batchTimer ??= BatchTimeProvider.CreateTimer(OnTimerElapsed, this, _batchTimeSpan, Timeout.InfiniteTimeSpan);
      }

      _pendingEntries[key] = patch;
    }
  }

  private async void OnTimerElapsed(object? state) => Flush();

  public void Flush()
  {
    lock (_pendingLock)
    {
      if (HasPendingPatch)
      {
        var patch = NoteViewStatePatchDto.Composite(_pendingEntries.Values);
        ViewStateUpdateDispatcher.TryDispatch(patch);
        _pendingEntries.Clear();
        _batchTimer?.Dispose();
      }

      _batchTimer = null;
    }
  }

  private bool _disposeStarted;

  private async ValueTask DisposeAsyncCore()
  {
    if (_batchTimer is not null)
    {
      await _batchTimer.DisposeAsync();
      Flush();
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposeStarted, true))
    {
      return;
    }
    await DisposeAsyncCore().ConfigureAwait(false);
    GC.SuppressFinalize(this);
  }
}