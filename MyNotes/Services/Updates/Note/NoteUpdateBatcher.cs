using MyNotes.Application.Contracts.Notes.Models;
using MyNotes.Application.Notes.Results;
using MyNotes.Application.Results;
using MyNotes.Common.Operations;
using MyNotes.Debugging;

namespace MyNotes.Services.Updates.Note;

internal sealed class NoteUpdateBatcher : IUpdateBatcher<string, NotePatchDto, UpdateNoteResult>
{
  private readonly TimeProvider BatchTimeProvider;
  private readonly IUpdateDispatcher<NotePatchDto, UpdateNoteResult> NoteUpdateDispatcher;

  private readonly TimeSpan _batchTimeSpan = TimeSpan.FromMilliseconds(500);
  private ITimer? _batchTimer;
  private readonly Dictionary<string, NotePatchOperationRequest> _pendingEntries = [];
  private bool HasPendingPatch => _pendingEntries.Count > 0;
  private readonly SemaphoreSlim _pendingSemaphore = new(1, 1);
  private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
  private long _currentCycleId;
  private readonly Lock _timerFlushTaskLock = new();
  private readonly HashSet<Task> _timerFlushTasks = [];

  public NoteUpdateBatcher(TimeProvider timeProvider, IUpdateDispatcher<NotePatchDto, UpdateNoteResult> noteUpdateDispatcher)
  {
    BatchTimeProvider = timeProvider;
    NoteUpdateDispatcher = noteUpdateDispatcher;
  }

  public async Task<UpdateNoteResult> AddOrMergeAsync(string key, NotePatchDto patch, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted), this);

    await _pendingSemaphore.WaitAsync(cancellationToken);

    NotePatchOperationRequest newRequest;

    try
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted), this);
      newRequest = new
      (
        operation: () => NoteUpdateDispatcher.DispatchAsync(patch),
        fallbackValue: new UpdateNoteResult() { Status = AppUpdateStatus.Failed }
      );
      if (!HasPendingPatch)
      {
        long cycleId = Interlocked.Increment(ref _currentCycleId);
        _batchTimer ??= BatchTimeProvider.CreateTimer(OnTimerElapsed, cycleId, _batchTimeSpan, Timeout.InfiniteTimeSpan);
      }

      if (_pendingEntries.TryGetValue(key, out var pendingRequest))
      {
        pendingRequest.Cancel();
      }

      _pendingEntries[key] = newRequest;
    }
    finally
    {
      _pendingSemaphore.Release();
    }

    try
    {
      return await newRequest.Result;
    }
    catch (OperationCanceledException)
    {
      return new() { Status = AppUpdateStatus.Canceled };
    }
    catch
    {
      return new() { Status = AppUpdateStatus.Failed };
    }
  }

  private void OnTimerElapsed(object? state)
  {
    if (state is not long cycleId)
    {
      return;
    }

    Task flushTask = FlushAsyncCore(cycleId);
    TrackTimerFlushTask(flushTask);
  }

  private void TrackTimerFlushTask(Task flushTask)
  {
    lock (_timerFlushTaskLock)
    {
      _timerFlushTasks.Add(flushTask);
    }

    _ = RemoveTimerFlushTaskWhenCompletedAsync(flushTask);
  }

  private async Task RemoveTimerFlushTaskWhenCompletedAsync(Task flushTask)
  {
    try
    {
      await flushTask;
    }
    finally
    {
      lock (_timerFlushTaskLock)
      {
        _timerFlushTasks.Remove(flushTask);
      }
    }
  }

  private async Task FlushAsyncCore(long? expectedCycleId, CancellationToken cancellationToken = default)
  {
    await _flushSemaphore.WaitAsync(cancellationToken);
    try
    {
      NotePatchOperationRequest[] pendingRequests;
      ITimer? detachedTimer;

      await _pendingSemaphore.WaitAsync(cancellationToken);

      try
      {
        // 이전 Cycle의 늦은 Timer 콜백이면 현재 Buffer를 건드리지 않습니다.
        if (expectedCycleId is long cycleId && cycleId != _currentCycleId)
        {
          return;
        }

        detachedTimer = _batchTimer;
        _batchTimer = null;

        pendingRequests = [.. _pendingEntries.Values];
        _pendingEntries.Clear();
      }
      finally
      {
        _pendingSemaphore.Release();
      }

      // 여기부터는 새로운 AddOrMerge가 새 Cycle과 Timer를 만들 수 있습니다.
      detachedTimer?.Dispose();

      foreach (NotePatchOperationRequest request in pendingRequests)
      {
        await request.ExecuteAsync();
      }
    }
    finally
    {
      _flushSemaphore.Release();
    }
  }
  public Task FlushAsync(CancellationToken cancellationToken = default) => FlushAsyncCore(null, cancellationToken);

  private bool _disposeStarted;

  private async ValueTask DisposeAsyncCore()
  {
    ITimer? batchTimer;

    await _pendingSemaphore.WaitAsync();
    try
    {
      batchTimer = _batchTimer;
      _batchTimer = null;
    }
    finally
    {
      _pendingSemaphore.Release();
    }

    // 더 이상 이 Timer에서 새 콜백이 시작되지 않도록 합니다.
    if (batchTimer is not null)
    {
      await batchTimer.DisposeAsync();
    }

    // DisposeAsync가 반환됐다면 이미 시작된 동기 콜백은
    // Flush Task 등록까지 마친 상태여야 합니다.
    Task[] timerFlushTasks;
    lock (_timerFlushTaskLock)
    {
      timerFlushTasks = [.. _timerFlushTasks];
    }

    if (timerFlushTasks.Length > 0)
    {
      await Task.WhenAll(timerFlushTasks);
    }

    // Timer가 만료되기 전에 남아 있던 Pending 요청을 처리합니다.
    await FlushAsync();
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

internal sealed class NotePatchOperationRequest(Func<Task<UpdateNoteResult>> operation, UpdateNoteResult fallbackValue) : AsyncOperationRequest<UpdateNoteResult>(operation, fallbackValue)
{
  public override async Task ExecuteAsync()
  {
    try
    {
      var result = await Operation.Invoke().ConfigureAwait(false);
      ConsoleHelper.WriteLine(true, "{0}: {1}", "NotePatch Operation invoked", result);
      var r = TaskCompletionSource.TrySetResult(result);
    }
    catch (OperationCanceledException)
    {
      TaskCompletionSource.TrySetCanceled();
    }
    catch (Exception ex)
    {
      TaskCompletionSource.TrySetException(ex);
    }
    finally
    {
      if (!TaskCompletionSource.Task.IsCompleted)
      {
        TaskCompletionSource.SetResult(FallbackValue);
      }
    }
  }

  public void Cancel()
  {
    if (TaskCompletionSource.Task.IsCanceled)
    {
      return;
    }
    TaskCompletionSource.TrySetResult(new UpdateNoteResult() { Status = AppUpdateStatus.Suspended });
  }
}