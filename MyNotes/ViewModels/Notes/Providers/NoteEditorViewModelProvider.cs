using Microsoft.Extensions.DependencyInjection;

using MyNotes.Models.Notes;

namespace MyNotes.ViewModels.Notes.Providers;

internal sealed class NoteEditorViewModelProvider(IServiceScopeFactory ScopeFactory, NoteViewModelProvider NoteViewModelProvider) : IAsyncViewModelProvider<NoteModel, NoteEditorViewModel>
{
  public async Task<IAsyncViewModelLease<NoteEditorViewModel>> ResolveAsync(NoteModel note)
  {
    var noteViewModelLease = await NoteViewModelProvider.ResolveAsync(note);
    var scope = ScopeFactory.CreateAsyncScope();
    try
    {
      var viewmodel = ActivatorUtilities.CreateInstance<NoteEditorViewModel>(scope.ServiceProvider, noteViewModelLease);
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

  Task<IAsyncViewModelLease<NoteEditorViewModel>?> IAsyncViewModelProvider<NoteModel, NoteEditorViewModel>.AcquireAsync(NoteModel model) => throw new InvalidOperationException();

  private class ViewModelLease : IAsyncViewModelLease<NoteEditorViewModel>
  {
    public required NoteEditorViewModel ViewModel { get; init; }
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
