namespace MyNotes.ViewModels;

internal abstract class AsyncViewModelBase : ViewModelBase, IAsyncDisposable
{
  private bool _disposeStarted;

  protected abstract ValueTask DisposeAsyncCore();

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposeStarted, true))
    {
      return;
    }

    await DisposeAsyncCore().ConfigureAwait(false);
    Dispose(disposing: false);
  }
}