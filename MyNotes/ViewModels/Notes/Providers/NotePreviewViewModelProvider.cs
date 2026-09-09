using Microsoft.Extensions.DependencyInjection;

using MyNotes.Models.Navigations;
using MyNotes.Models.Notes;

namespace MyNotes.ViewModels.Notes.Providers;

internal sealed class NotePreviewViewModelProvider(IServiceScopeFactory ScopeFactory, NoteViewModelProvider NoteViewModelProvider) : IAsyncViewModelProvider<NoteModel, INavigationNoteList, NotePreviewViewModel>
{
  public async Task<IAsyncViewModelLease<NotePreviewViewModel>> ResolveAsync(NoteModel note, INavigationNoteList navigation)
  {
    var noteViewModelLease = await NoteViewModelProvider.ResolveAsync(note);
    var scope = ScopeFactory.CreateAsyncScope();
    try
    {
      var viewmodel = navigation switch
      {
        NavigationSearch => ActivatorUtilities.CreateInstance<NoteSearchPreviewViewModel>(scope.ServiceProvider, noteViewModelLease),
        _ => ActivatorUtilities.CreateInstance<NotePreviewViewModel>(scope.ServiceProvider, noteViewModelLease)
      };
      return new ViewModelLease()
      {
        ViewModel = viewmodel,
        ReleaseFunc = async () =>
        {
          await viewmodel.DisposeAsync();
          await scope.DisposeAsync();
        }
      };
    }
    catch
    {
      await noteViewModelLease.DisposeAsync();
      await scope.DisposeAsync();
      throw;
    }
  }

  Task<IAsyncViewModelLease<NotePreviewViewModel>?> IAsyncViewModelProvider<NoteModel, INavigationNoteList, NotePreviewViewModel>.AcquireAsync(NoteModel model) => throw new InvalidOperationException();

  private class ViewModelLease : IAsyncViewModelLease<NotePreviewViewModel>
  {
    public required NotePreviewViewModel ViewModel { get; init; }
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
}