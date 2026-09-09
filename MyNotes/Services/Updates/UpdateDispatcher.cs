using System.Threading.Channels;

using MyNotes.Debugging;

namespace MyNotes.Services.Updates;

internal sealed class UpdateDispatcher<TPatch> : IUpdateDispatcher<TPatch> where TPatch : notnull
{
  private readonly IUpdateHandler<TPatch> UpdateHandler;

  private readonly Channel<TPatch> DispatcherChannel = Channel.CreateUnbounded<TPatch>(new UnboundedChannelOptions() { SingleReader = true, SingleWriter = false });

  public UpdateDispatcher(IUpdateHandler<TPatch> updateHandler)
  {
    UpdateHandler = updateHandler;
    _workerTask = RunConsumerWorker();
  }

  public bool TryDispatch(TPatch patch) => DispatcherChannel.Writer.TryWrite(patch);

  private readonly Task _workerTask;
  private Task RunConsumerWorker() => Task.Run(async () =>
  {
    await foreach (TPatch patch in DispatcherChannel.Reader.ReadAllAsync())
    {
      await UpdateHandler.HandleAsync(patch);
    }
  });

  private bool _disposeStarted;

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposeStarted, true))
    {
      return;
    }
    await DisposeAsyncCore().ConfigureAwait(false);
    GC.SuppressFinalize(this);
  }

  private async ValueTask DisposeAsyncCore()
  {
    var completed = DispatcherChannel.Writer.TryComplete();
    ConsoleHelper.WriteLine(true, "{0}: {1}", "Dispatcher Disposing & completed", completed);
    await _workerTask;
  }
}

internal sealed class UpdateDispatcher<TPatch, TResult>(IUpdateHandler<TPatch, TResult> UpdateHandler) : IUpdateDispatcher<TPatch, TResult> where TPatch : notnull where TResult : notnull
{
  public Task<TResult> DispatchAsync(TPatch patch, CancellationToken cancellationToken = default) => UpdateHandler.HandleAsync(patch, cancellationToken);

  private bool _disposeStarted;

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposeStarted, true))
    {
      return;
    }
    GC.SuppressFinalize(this);
  }
}