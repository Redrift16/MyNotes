using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;

using MyNotes.Common.Lifetime;
using MyNotes.Domain.Navigations;
using MyNotes.Models.Navigations;

namespace MyNotes.ViewModels.Navigations.Contents.Providers;

internal sealed class NavigationNoteListViewModelProvider(IServiceProvider serviceProvider) : IAsyncViewModelProvider<INavigationNoteList, NavigationNoteListViewModel>
{
  private readonly ConcurrentDictionary<INavigationNoteList, ViewModelCache> ResolveTable = new();

  private readonly Func<INavigationNoteList, ViewModelCache> _cacheFactory = navigation => new ViewModelCache(() =>
  {
    AsyncServiceScope serviceScope = serviceProvider.CreateAsyncScope();
    return new ReferenceCountedViewModelScope
    (
      referenceCounter: new ReferenceCounter<NavigationNoteListViewModel>(ActivatorUtilities.CreateInstance<NavigationNoteListViewModel>(serviceScope.ServiceProvider, navigation)),
      serviceScope: serviceScope
    );
  });

  public async Task<IAsyncViewModelLease<NavigationNoteListViewModel>> ResolveAsync(INavigationNoteList navigation)
  {
    while (true)
    {
      var cache = ResolveTable.GetOrAdd(navigation, _cacheFactory);

      await cache.Semaphore.WaitAsync();
      try
      {
        if (ResolveTable.TryGetValue(navigation, out ViewModelCache? currentCache) && ReferenceEquals(currentCache, cache))
        {
          if (cache.ReferenceCounter.TryAcquire(out var viewModel))
          {
            return await CreateLease(navigation, viewModel, cache);
          }

          ResolveTable.TryRemove(navigation, out _);
        }
      }
      finally
      {
        cache.Semaphore.Release();
      }
    }
  }

  public async Task<IAsyncViewModelLease<NavigationNoteListViewModel>?> AcquireAsync(INavigationNoteList navigation)
  {
    if (ResolveTable.TryGetValue(navigation, out var cache))
    {
      await cache.Semaphore.WaitAsync();
      try
      {
        if (cache.ReferenceCounter.TryAcquire(out var viewmodel))
        {
          if (!viewmodel.Disposed)
          {
            return await CreateLease(navigation, viewmodel, cache);
          }
          else
          {
            ResolveTable.TryRemove(navigation, out _);
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

  public async Task<IAsyncViewModelLease<NavigationNoteListViewModel>?> AcquireByIdAsync(NavigationId navigationId)
  {
    foreach (var nav in ResolveTable.Keys.ToArray())
    {
      if (nav is INavigationNode node && node.Id == navigationId)
      {
        return await AcquireAsync(nav);
      }
    }

    return null;
  }

  private async Task<ViewModelLease> CreateLease(INavigationNoteList navigation, NavigationNoteListViewModel viewmodel, ViewModelCache cache)
  {
    await viewmodel.InitializeTask;
    return new ViewModelLease()
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
            ResolveTable.TryRemove(navigation, out _);
          }
        }
        finally
        {
          cache.Semaphore.Release();
        }
      }
    };
  }

  private class ViewModelLease : IAsyncViewModelLease<NavigationNoteListViewModel>
  {
    public required NavigationNoteListViewModel ViewModel { get; init; }
    public required Func<Task> ReleaseFunc { get; init; }

    private bool _disposeStarted;
    private async ValueTask DisposeAsyncCore() => await ReleaseFunc();

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

    public ReferenceCounter<NavigationNoteListViewModel> ReferenceCounter => _countedScope.Value.ReferenceCounter;

    public AsyncServiceScope ServiceScope => _countedScope.Value.ServiceScope;
  }

  public sealed class ReferenceCountedViewModelScope(ReferenceCounter<NavigationNoteListViewModel> referenceCounter, AsyncServiceScope serviceScope)
  {
    public ReferenceCounter<NavigationNoteListViewModel> ReferenceCounter { get; } = referenceCounter;

    public AsyncServiceScope ServiceScope { get; } = serviceScope;
  }
}
