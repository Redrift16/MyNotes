using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using MyNotes.Application.Contracts.Navigations.Models;
using MyNotes.Application.Contracts.Notes.Models;
using MyNotes.Application.Contracts.Querying.Models;
using MyNotes.Application.Navigations.Commands;
using MyNotes.Application.Navigations.Services;
using MyNotes.Application.Notes.Commands;
using MyNotes.Application.Notes.Services;
using MyNotes.Application.Results;
using MyNotes.Application.Settings.Services;
using MyNotes.Common.Commands;
using MyNotes.Common.Converters.Codecs;
using MyNotes.Common.Helpers;
using MyNotes.Constants;
using MyNotes.Domain.Navigations;
using MyNotes.Domain.Notes;
using MyNotes.Messaging;
using MyNotes.Messaging.Messages;
using MyNotes.Models;
using MyNotes.Models.Navigations;
using MyNotes.Models.Navigations.Core;
using MyNotes.Models.Navigations.User;
using MyNotes.Models.Notes;
using MyNotes.Services.Windows;
using MyNotes.Templates;
using MyNotes.ViewModels.Notes;
using MyNotes.ViewModels.Notes.Providers;

namespace MyNotes.ViewModels.Navigations.Contents;

internal sealed partial class NavigationNoteListViewModel : AsyncViewModelBase
{
  private readonly AppSettingsService AppSettingsService;
  private readonly NavigationService NavigationService;
  private readonly NoteService NoteService;
  private readonly NoteWindowService NoteWindowService;
  private readonly MainWindowService MainWindowService;
  private readonly IModelFactory<NoteDto, NoteModel> NoteModelFactory;
  private readonly IModelStore<NoteId, NoteModel> NoteModelStore;
  private readonly NotePreviewViewModelProvider NotePreviewViewModelProvider;
  private readonly LeasedNotePreviewViewModelCollection _notePreviewViewModelLeases;
  public IReadOnlyList<NotePreviewViewModel> NotePreviewViewModels => _notePreviewViewModelLeases.ViewModels;
  private readonly INavigationNoteList Navigation;

  #region Object Lifetime Management
  public NavigationNoteListViewModel(AppSettingsService appSettingsService, NavigationService navigationService, NoteService noteService, NoteWindowService noteWindowService, MainWindowService mainWindowService, IModelFactory<NoteDto, NoteModel> noteModelFactory, IModelStore<NoteId, NoteModel> noteModelStore, NotePreviewViewModelProvider notePreviewViewModelProvider, INavigationNoteList navigation)
  {
    AppSettingsService = appSettingsService;
    NavigationService = navigationService;
    NoteService = noteService;
    NoteWindowService = noteWindowService;
    MainWindowService = mainWindowService;
    NoteModelFactory = noteModelFactory;
    NoteModelStore = noteModelStore;
    NotePreviewViewModelProvider = notePreviewViewModelProvider;

    Navigation = navigation;

    SetCommands();
    RegisterMessengers();

    _notePreviewViewModelLeases = new(GetComparer(Navigation.NoteSortKey, Navigation.NoteSortDirection));
    InitializeTask = InitializeAsync();
  }

  protected override async ValueTask DisposeAsyncCore()
  {
    if (Interlocked.Exchange(ref _disposeStarted, true))
    {
      return;
    }

    await _notePreviewViewModelLeases.DisposeAsync();
    Navigation.PropertyChanged -= Navigation_PropertyChanged;
    UnregisterMessengers();
  }
  #endregion

  public Task InitializeTask { get; }
  private async Task InitializeAsync()
  {
    switch (Navigation)
    {
      case NavigationUserLeafNode leafNavigation:
        var leafNotes = (await NoteService.Retrieval.GetNotesByParentAsync(leafNavigation.Id, false)).Select(NoteModelFactory.Create);
        foreach (var note in leafNotes)
        {
          await _notePreviewViewModelLeases.AddAsync(await NotePreviewViewModelProvider.ResolveAsync(note, leafNavigation));
        }
        break;
      case NavigationSearch searchNavigation:
        await foreach (var noteSearchResultDto in NoteService.Retrieval.SearchNotesAsync(searchNavigation.SearchText))
        {
          var hitDto = noteSearchResultDto.HitDto;
          NoteModel searchedNote = NoteModelFactory.Create(noteSearchResultDto.NoteDto);
          var lease = await NotePreviewViewModelProvider.ResolveAsync(searchedNote, searchNavigation);
          await _notePreviewViewModelLeases.AddAsync(lease);
          (lease.ViewModel as NoteSearchPreviewViewModel)?.HighlightPreview(hitDto.BodyMatchRanges);
        }
        break;
      case NavigationBookmarks bookmarksNavigation:
        var bookmarkResultDtos = await NoteService.Retrieval.GetBookmarkedNotesAsync();
        foreach (var bookmarkResultDto in bookmarkResultDtos)
        {
          NoteModel bookmarkedNote = NoteModelFactory.Create(bookmarkResultDto);
          await _notePreviewViewModelLeases.AddAsync(await NotePreviewViewModelProvider.ResolveAsync(bookmarkedNote, bookmarksNavigation));
        }
        break;
      case NavigationTrash trashNavigation:
        var trashResultDtos = await NoteService.Retrieval.GetTrashedNotesAsync();
        foreach (var trashResultDto in trashResultDtos)
        {
          NoteModel trashedNote = NoteModelFactory.Create(trashResultDto);
          await _notePreviewViewModelLeases.AddAsync(await NotePreviewViewModelProvider.ResolveAsync(trashedNote, trashNavigation));
        }
        break;
    }

    Navigation.PropertyChanged += Navigation_PropertyChanged;
  }

  private void Navigation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    switch (e.PropertyName)
    {
      case nameof(INavigationNoteList.NoteSortKey):
      case nameof(INavigationNoteList.NoteSortDirection):
        _notePreviewViewModelLeases.Rearrange(GetComparer(Navigation.NoteSortKey, Navigation.NoteSortDirection));
        break;
    }
  }

  [ObservableProperty]
  public partial Icon Icon { get; set; }

  async partial void OnIconChanged(Icon oldValue, Icon newValue)
  {
    if (Navigation is NavigationUserNode navigationUserNode)
    {
      var updateResult = await NavigationService.Modification.UpdateNavigationAsync(new UpdateNavigationAppCommand()
      {
        PatchDto = new NavigationPatchDto()
        {
          Id = navigationUserNode.Id,
          Icon = (int)newValue
        }
      });
      if (updateResult is AppUpdateStatus.Succeeded)
      {
        navigationUserNode.Icon = newValue;
      }
    }
  }

  private static Comparer<NotePreviewViewModel> GetComparer(NoteSortKey noteSortKey, SortDirection sortDirection) => (noteSortKey, sortDirection) switch
  {
    (NoteSortKey.Modified, SortDirection.Ascending) => Comparer<NotePreviewViewModel>.Create((x, y) => x.Note.Modified.CompareTo(y.Note.Modified)),
    (NoteSortKey.Modified, SortDirection.Descending) => Comparer<NotePreviewViewModel>.Create((x, y) => y.Note.Modified.CompareTo(x.Note.Modified)),
    (NoteSortKey.Created, SortDirection.Ascending) => Comparer<NotePreviewViewModel>.Create((x, y) => x.Note.Created.CompareTo(y.Note.Created)),
    (NoteSortKey.Created, SortDirection.Descending) => Comparer<NotePreviewViewModel>.Create((x, y) => y.Note.Created.CompareTo(x.Note.Created)),
    (NoteSortKey.Title, SortDirection.Ascending) => Comparer<NotePreviewViewModel>.Create((x, y) => x.Note.Title.CompareTo(y.Note.Title)),
    (NoteSortKey.Title, SortDirection.Descending) => Comparer<NotePreviewViewModel>.Create((x, y) => y.Note.Title.CompareTo(x.Note.Title)),
    _ => throw new ArgumentException("Invalid sorting")
  };

  public async Task RemoveNoteFromListAsync(NoteId noteId)
  {
    if (NotePreviewViewModels.FirstOrDefault(vm => vm.Note.Id == noteId) is NotePreviewViewModel viewmodel)
    {
      await _notePreviewViewModelLeases.RemoveAsync(viewmodel);
    }
  }

  #region Commands and Messengers
  public AsyncCommand? AddNoteCommand { get; private set; }

  private void SetCommands()
  {
    AddNoteCommand = new(
      executeFunc: async () =>
      {
        if (Navigation is NavigationUserLeafNode leafNavigation)
        {
          var size = AppSettingsService.Load(SizeInt32SettingsCodec.Default, AppSettingsDescriptors.DefaultNoteSize);
          var position = MainWindowService.GetNewWindowPosition(size) ?? AppSettingsDescriptors.DefaultNoteWindowPosition;
          CreateNoteAppCommand createNoteAppCommand = new()
          {
            NavigationId = leafNavigation.Id,
            Size = size,
            Position = position
          };
          var bundleDto = await NoteService.Creation.AddNoteAsync(createNoteAppCommand);
          if (bundleDto is null)
          {
            return;
          }

          NoteModel noteModel = NoteModelFactory.Create(bundleDto);
          await _notePreviewViewModelLeases.AddAsync(await NotePreviewViewModelProvider.ResolveAsync(noteModel, leafNavigation));
          await NoteWindowService.OpenNoteWindow(noteModel);
        }
      });
  }

  private void RegisterMessengers()
  {
    WeakReferenceMessenger.Default.Register<NavigationNoteListViewModel, NoteTitleChangedMessage, MessageToken<Type>>(this, MessageToken<Type>.Create(typeof(INavigationNoteList)), static (recipient, message) =>
    {
      if (message.Sender is NoteModel note
          && recipient.NotePreviewViewModels.FirstOrDefault(vm => vm.Note == note) is NotePreviewViewModel viewmodel
          && recipient.Navigation.NoteSortKey is NoteSortKey.Title)
      {
        recipient._notePreviewViewModelLeases.ReorderItem(viewmodel);
      }
    });

    WeakReferenceMessenger.Default.Register<NavigationNoteListViewModel, NoteBookmarkedChangedMessage, MessageToken<Type>>(this, MessageToken<Type>.Create(typeof(INavigationNoteList)), async static (recipient, message) =>
    {
      if (message.Sender is NoteModel note)
      {
        switch (recipient.Navigation)
        {
          case NavigationBookmarks bookmarksNavigation:
            var noteViewModel = recipient.NotePreviewViewModels.FirstOrDefault(vm => vm.Note.Id == note.Id);
            if (message.NewValue)
            {
              if (noteViewModel is null)
              {
                await recipient._notePreviewViewModelLeases.AddAsync(await recipient.NotePreviewViewModelProvider.ResolveAsync(note, bookmarksNavigation));
              }
            }
            else
            {
              if (noteViewModel is not null)
              {
                await recipient._notePreviewViewModelLeases.RemoveAsync(noteViewModel);
              }
            }
            break;
        }
      }
    });

    if (Navigation is NavigationUserLeafNode userNavigation)
    {
      WeakReferenceMessenger.Default.Register<NavigationNoteListViewModel, NoteAdditionRequestedMessage, MessageToken<NavigationId>>(this, MessageToken<NavigationId>.Create(userNavigation.Id), async static (recipient, message) =>
      {
        if (recipient.NoteModelStore.TryGet(message.NoteId, out var note))
        {
          await recipient._notePreviewViewModelLeases.AddAsync(await recipient.NotePreviewViewModelProvider.ResolveAsync(note, recipient.Navigation));
        }
      });
    }
  }

  private void UnregisterMessengers() => WeakReferenceMessenger.Default.UnregisterAll(this);
  #endregion
}