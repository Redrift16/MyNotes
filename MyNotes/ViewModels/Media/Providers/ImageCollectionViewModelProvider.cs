using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;

using MyNotes.Common.Lifetime;
using MyNotes.Models.Media;

namespace MyNotes.ViewModels.Media.Providers;

internal class ImageCollectionViewModelProvider(IServiceProvider serviceProvider) : IAsyncViewModelProvider<ImageCollectionKey, ImageCollectionViewModel>
{
  private readonly ConcurrentDictionary<ImageCollectionKey, ViewModelCache> ResolveTable = new();
  private readonly Func<ImageCollectionKey, ViewModelCache> _cacheFactory = key => new
  (
    referenceCounterFactory: () => new ReferenceCounter<ImageCollectionViewModel>(ActivatorUtilities.CreateInstance<ImageCollectionViewModel>(serviceProvider, key))
  );

  public async Task<IAsyncViewModelLease<ImageCollectionViewModel>> ResolveAsync(ImageCollectionKey key)
  {
    while (true)
    {
      var cache = ResolveTable.GetOrAdd(key, _cacheFactory.Invoke);

      await cache.Semaphore.WaitAsync();
      try
      {
        if (ResolveTable.TryGetValue(key, out ViewModelCache? currentCache) && ReferenceEquals(currentCache, cache))
        {
          if (cache.ReferenceCounter.TryAcquire(out var viewModel))
          {
            return await CreateLease(key, viewModel, cache);
          }

          ResolveTable.TryRemove(key, out _);
        }
      }
      finally
      {
        cache.Semaphore.Release();
      }
    }
  }

  private async Task<ViewModelLease> CreateLease(ImageCollectionKey key, ImageCollectionViewModel viewmodel, ViewModelCache cache)
  {
    await viewmodel.InitializationTask;
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
            ResolveTable.TryRemove(key, out _);
          }
        }
        finally
        {
          cache.Semaphore.Release();
        }
      }
    };
  }

  public async Task<IAsyncViewModelLease<ImageCollectionViewModel>?> AcquireAsync(ImageCollectionKey key)
  {
    if (ResolveTable.TryGetValue(key, out var cache))
    {
      await cache.Semaphore.WaitAsync();
      try
      {
        if (cache.ReferenceCounter.TryAcquire(out var viewmodel))
        {
          if (!viewmodel.Disposed)
          {
            return await CreateLease(key, viewmodel, cache);
          }
          else
          {
            ResolveTable.TryRemove(key, out _);
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

  private sealed class ViewModelLease() : IAsyncViewModelLease<ImageCollectionViewModel>
  {
    public required ImageCollectionViewModel ViewModel { get; init; }
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

  private sealed class ViewModelCache(Func<ReferenceCounter<ImageCollectionViewModel>> referenceCounterFactory)
  {
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    private readonly Lazy<ReferenceCounter<ImageCollectionViewModel>> _referenceCounter = new(referenceCounterFactory);

    public ReferenceCounter<ImageCollectionViewModel> ReferenceCounter => _referenceCounter.Value;
  }
}