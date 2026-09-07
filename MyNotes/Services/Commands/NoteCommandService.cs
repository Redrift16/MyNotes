using CommunityToolkit.Mvvm.Messaging;

using MyNotes.Application.Contracts.Notes.Models;
using MyNotes.Application.Notes.Commands;
using MyNotes.Application.Notes.Services;
using MyNotes.Application.Results;
using MyNotes.Application.Settings.Services;
using MyNotes.Common.Converters.Codecs;
using MyNotes.Common.Enums.Modes;
using MyNotes.Common.Helpers;
using MyNotes.Common.Interop;
using MyNotes.Common.Mappers;
using MyNotes.Constants;
using MyNotes.Domain.Navigations;
using MyNotes.Domain.Notes;
using MyNotes.Messaging;
using MyNotes.Messaging.Messages;
using MyNotes.Models;
using MyNotes.Models.Navigations;
using MyNotes.Models.Notes;
using MyNotes.Services.Dialogs;
using MyNotes.Services.Shell;
using MyNotes.Services.Windows;
using MyNotes.ViewModels.Navigations.Contents.Providers;
using MyNotes.ViewModels.Navigations.Items;
using MyNotes.ViewModels.Navigations.Items.Providers;
using MyNotes.ViewModels.Notes;
using MyNotes.ViewModels.Notes.Providers;

namespace MyNotes.Services.Commands;

internal sealed class NoteCommandService
  (NoteService NoteService,
  NoteWindowService NoteWindowService,
  IModelFactory<NoteDto, NoteModel> NoteModelFactory,
  NoteViewModelProvider NoteViewModelProvider,
  NavigationViewModelProvider NavigationViewModelProvider,
  NavigationNoteListViewModelProvider NotePreviewListViewModelProvider,
  MainWindowService MainWindowService,
  DialogService DialogService,
  JumpListService JumpListService,
  AppSettingsService AppSettingsService)
  : ICommandService
{
  public Task OpenNoteWindowAsync(NoteModel noteModel) => NoteWindowService.OpenNoteWindow(noteModel);

  public void MinimizeNoteWindow(NoteId noteId)
  {
    if (NoteWindowService.TryGetWindowInfo(noteId, out _, out var appWindow))
    {
      var presenter = appWindow?.Presenter as OverlappedPresenter;
      presenter?.Minimize();
    }
  }

  public void CloseNoteWindow(NoteId noteId)
  {
    if (NoteWindowService.TryGetWindowInfo(noteId, out IntPtr hWnd, out _))
    {
      NativeMethods.SendMessage(hWnd, (uint)NativeMethods.WindowMessage.WM_SYSCOMMAND, (IntPtr)NativeMethods.SystemCommand.SC_CLOSE, IntPtr.Zero);
    }
  }

  public void PinNoteWindow(NoteId noteId, bool isPinned)
  {
    if (NoteWindowService.TryGetWindowInfo(noteId, out _, out var appWindow)
      && appWindow?.Presenter is OverlappedPresenter presenter)
    {
      presenter.IsAlwaysOnTop = isPinned;
    }
  }

  public async Task MoveNoteToListAsync(NoteModel sourceNoteModel, NavigationId targetNavigationId)
  {
    NavigationId oldNavigationId = sourceNoteModel.NavigationId;

    if (sourceNoteModel.NavigationId == targetNavigationId)
    {
      return;
    }

    sourceNoteModel.NavigationId = targetNavigationId;

    UpdateNoteAppCommand updateAppCommand = new()
    {
      PatchDto = new NotePatchDto()
      {
        Id = sourceNoteModel.Id,
        NavigationId = targetNavigationId,
      }
    };

    var updateResult = await NoteService.Modification.UpdateNoteAsync(updateAppCommand);

    if (updateResult.Status is AppUpdateStatus.Succeeded)
    {
      using var navigationLease = NavigationViewModelProvider.Acquire(oldNavigationId);
      if (navigationLease?.ViewModel is UserListNavigationViewModel sourceViewModel)
      {
        sourceNoteModel.Modified = updateResult.Modified ?? throw new InvalidOperationException();
        await using var previewListLease = await NotePreviewListViewModelProvider.AcquireByIdAsync(oldNavigationId);
        if (previewListLease is not null)
        {
          await previewListLease.ViewModel.RemoveNoteFromListAsync(sourceNoteModel.Id);
        }
      }
    }
  }

  public async Task CreateNewNoteAsync(NavigationId? navigationId = null)
  {
    if (navigationId is NavigationId targetNavigationId)
    {
      using var navigationLease = NavigationViewModelProvider.Acquire(targetNavigationId);
      if (navigationLease?.ViewModel is UserListNavigationViewModel navigationViewModel)
      {
        var size = AppSettingsService.Load(SizeInt32SettingsCodec.Default, AppSettingsDescriptors.DefaultNoteSize);
        var position = MainWindowService.GetNewWindowPosition(size) ?? AppSettingsDescriptors.DefaultNoteWindowPosition;

        CreateNoteAppCommand appCommand = new()
        {
          NavigationId = navigationViewModel.Navigation.Id,
          Size = size,
          Position = position
        };

        if (await NoteService.Creation.AddNoteAsync(appCommand) is NoteDto newNoteDto)
        {
          NoteModel newNoteModel = NoteModelFactory.Create(newNoteDto);
          await using var noteViewModelLease = await NoteViewModelProvider.ResolveAsync(newNoteModel);
          NoteViewModel newNoteViewModel = noteViewModelLease.ViewModel;
          await NoteWindowService.OpenNoteWindow(newNoteModel);
          WeakReferenceMessenger.Default.Send(new NoteAdditionRequestedMessage(newNoteModel.Id), MessageToken<NavigationId>.Create(navigationViewModel.Navigation.Id));
        }
      }
    }
    else
    {
      //todo: JumpList에 의해 생성되는 등 Parent Navigation이 정해지지 않았을 때 노트 생성 로직 구현

      //NoteId newNoteId = await NoteService.GetUniqueNoteIdAsync();
      //NoteModel newNoteModel = NoteModelStore.Resolve(newNoteId, () => NoteModelFactory.CreateDefault(newNoteId));
      //NoteViewModel newNoteViewModel = NoteViewModelProvider.Resolve(newNoteModel);
      //await NoteWindowService.OpenNoteWindow(newNoteModel);
    }
  }

  public async Task RenameNoteTitleAsync(NoteModel noteModel, string oldTitle)
  {
    string newTitle = noteModel.Title;
    UpdateNoteAppCommand appCommand = new()
    {
      PatchDto = new NotePatchDto()
      {
        Id = noteModel.Id,
        Title = new(newTitle)
      }
    };

    var updateResult = await NoteService.Modification.UpdateNoteAsync(appCommand);

    if (updateResult.Status is AppUpdateStatus.Succeeded)
    {
      noteModel.Modified = updateResult.Modified ?? throw new InvalidOperationException();
      WeakReferenceMessenger.Default.Send(new NoteTitleChangedMessage(noteModel, nameof(NoteModel.Title), oldTitle, newTitle), MessageToken<Type>.Create(typeof(INavigationNoteList)));
      await JumpListService.EditJumpListItemAsync(NoteMappers.ToDomain(noteModel));
      return;
    }

    noteModel.Title = oldTitle;
  }

  public async Task ToggleBookmarkNoteAsync(NoteModel noteModel)
  {
    var oldState = noteModel.IsBookmarked;
    var newState = !oldState;
    UpdateNoteAppCommand appCommand = new()
    {
      PatchDto = new NotePatchDto()
      {
        Id = noteModel.Id,
        IsBookmarked = new(newState)
      }
    };
    var updateResult = await NoteService.Modification.UpdateNoteAsync(appCommand);
    if (updateResult.Status is AppUpdateStatus.Succeeded)
    {
      noteModel.IsBookmarked = newState;
      WeakReferenceMessenger.Default.Send(new NoteBookmarkedChangedMessage(noteModel, nameof(NoteModel.IsBookmarked), oldState, newState), MessageToken<Type>.Create(typeof(INavigationNoteList)));
      noteModel.Modified = updateResult.Modified ?? throw new InvalidOperationException();
    }
  }

  public async Task ConfirmAndDeleteNoteAsync(NoteModel noteModel)
  {
    if (MainWindowService.TryGetCurrentWindow(out var mainWindow)
      && mainWindow.Content.XamlRoot is XamlRoot xamlRoot)
    {
      var preferredDeleteMode = DeleteMode.MoveToTrash;
      var dialogResponse = await DialogService.ShowConfirmDeleteDialogAsync(xamlRoot, "Note", noteModel.Title, preferredDeleteMode);
      if (dialogResponse.Result == ContentDialogResult.Primary)
      {
        await DeleteNoteAsync(noteModel, dialogResponse.Data);
      }
    }
  }

  public async Task DeleteNoteAsync(NoteModel noteModel, DeleteMode deleteMode)
  {
    DeleteNoteAppCommand deleteCommand = new()
    {
      Id = noteModel.Id,
      DeleteMode = deleteMode
    };

    var deleteResult = await NoteService.Modification.DeleteNoteAsync(deleteCommand);
    if (deleteResult is AppUpdateStatus.Succeeded)
    {
      noteModel.IsDeleted = true;

      await using var previewListLease = await NotePreviewListViewModelProvider.AcquireByIdAsync(noteModel.NavigationId);
      if (previewListLease is not null)
      {
        await previewListLease.ViewModel.RemoveNoteFromListAsync(noteModel.Id);
      }
    }
  }

  public async Task DeleteNoteManuallyAsync(NoteModel noteModel)
  {
    DeleteNoteAppCommand appCommand = new()
    {
      Id = noteModel.Id,
      DeleteMode = DeleteMode.Permanent
    };
    if (await NoteService.Modification.DeleteNoteAsync(appCommand) is AppUpdateStatus.Succeeded)
    {
      await using var previewListLease = await NotePreviewListViewModelProvider.AcquireByIdAsync(noteModel.NavigationId);
      if (previewListLease is not null)
      {
        await previewListLease.ViewModel.RemoveNoteFromListAsync(noteModel.Id);
      }
    }
  }

  public Task AddNoteToJumpList(NoteModel noteModel) => JumpListService.AddToJumpListAsync(NoteMappers.ToDomain(noteModel));
}
