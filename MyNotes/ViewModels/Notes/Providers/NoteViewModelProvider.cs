using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;

using MyNotes.Common.Lifetime;
using MyNotes.Models.Notes;

namespace MyNotes.ViewModels.Notes.Providers;

internal sealed class NoteViewModelProvider(IServiceProvider serviceProvider) : IAsyncViewModelProvider<NoteModel, NoteViewModel>
{
  private readonly ConcurrentDictionary<NoteModel, ViewModelCache> ResolveTable = new();

  private readonly Func<NoteModel, ViewModelCache> _cacheFactory = noteModel => new ViewModelCache(() =>
  {
    AsyncServiceScope serviceScope = serviceProvider.CreateAsyncScope();
    return new ReferenceCountedViewModelScope
    (
      referenceCounter: new ReferenceCounter<NoteViewModel>(ActivatorUtilities.CreateInstance<NoteViewModel>(serviceScope.ServiceProvider, noteModel)),
      serviceScope: serviceScope
    );
  });

  public async Task<IAsyncViewModelLease<NoteViewModel>> ResolveAsync(NoteModel noteModel)
  {
    while (true)
    {
      var cache = ResolveTable.GetOrAdd(noteModel, nav => _cacheFactory(nav));

      await cache.Semaphore.WaitAsync();
      try
      {
        if (ResolveTable.TryGetValue(noteModel, out ViewModelCache? currentCache) && ReferenceEquals(currentCache, cache))
        {
          if (cache.ReferenceCounter.TryAcquire(out var viewModel))
          {
            return CreateLease(noteModel, viewModel, cache);
          }

          ResolveTable.TryRemove(noteModel, out _);
        }
      }
      finally
      {
        cache.Semaphore.Release();
      }
    }
  }

  public async Task<IAsyncViewModelLease<NoteViewModel>?> AcquireAsync(NoteModel noteModel)
  {
    if (ResolveTable.TryGetValue(noteModel, out var cache))
    {
      await cache.Semaphore.WaitAsync();
      try
      {
        if (cache.ReferenceCounter.TryAcquire(out var viewmodel))
        {
          if (!viewmodel.Disposed)
          {
            return CreateLease(noteModel, viewmodel, cache);
          }
          else
          {
            ResolveTable.TryRemove(noteModel, out _);
          }
        }
      }
      finally
      {
        cache.Semaphore.Release();
      }
    }
    return null;
  }

  private ViewModelLease CreateLease(NoteModel noteModel, NoteViewModel viewmodel, ViewModelCache cache) => new()
  {
    ViewModel = viewmodel,
    ReleaseFunc = async () =>
    {
      await cache.Semaphore.WaitAsync();
      try
      {
        if (cache.ReferenceCounter.ReleaseOrDetach(out _))
        {
          await viewmodel.DisposeAsync();
          await cache.ServiceScope.DisposeAsync();
          ResolveTable.TryRemove(noteModel, out _);
        }
      }
      finally
      {
        cache.Semaphore.Release();
      }
    }
  };

  private class ViewModelLease : IAsyncViewModelLease<NoteViewModel>
  {
    public required NoteViewModel ViewModel { get; init; }
    public required Func<Task> ReleaseFunc { get; init; }

    private bool _disposeStarted;
    private async ValueTask DisposeAsyncCore() => await ReleaseFunc.Invoke();

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

  private sealed class ViewModelCache(Func<ReferenceCountedViewModelScope> countedScopeFactory)
  {
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    private readonly Lazy<ReferenceCountedViewModelScope> _countedScope = new(countedScopeFactory);

    public ReferenceCounter<NoteViewModel> ReferenceCounter => _countedScope.Value.ReferenceCounter;

    public AsyncServiceScope ServiceScope => _countedScope.Value.ServiceScope;
  }

  public sealed class ReferenceCountedViewModelScope(ReferenceCounter<NoteViewModel> referenceCounter, AsyncServiceScope serviceScope)
  {
    public ReferenceCounter<NoteViewModel> ReferenceCounter { get; } = referenceCounter;

    public AsyncServiceScope ServiceScope { get; } = serviceScope;
  }
}