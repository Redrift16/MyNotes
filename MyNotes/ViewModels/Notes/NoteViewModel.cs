using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;

using MyNotes.Application.Contracts.Converters;
using MyNotes.Application.Contracts.Notes.Models;
using MyNotes.Application.Notes.Services;
using MyNotes.Application.Settings.Services;
using MyNotes.Common.Commands;
using MyNotes.Constants;
using MyNotes.Debugging;
using MyNotes.Domain.Navigations;
using MyNotes.Models.Notes;
using MyNotes.Services.Commands;
using MyNotes.Services.Updates;

namespace MyNotes.ViewModels.Notes;

internal sealed partial class NoteViewModel : AsyncViewModelBase
{
  private readonly NoteCommandService NoteCommandService;
  private readonly NavigationCommandService NavigationCommandService;
  private readonly IUpdateCoordinator<string, NoteViewStatePatchDto> NoteUpdateCoordinator;
  private readonly IMemoryCache MemoryCache;

  public NoteModel Note { get; }

  #region Object Lifetime Management
  public NoteViewModel([FromKeyedServices(CommandServiceType.Note)] ICommandService noteCommandService, [FromKeyedServices(CommandServiceType.Navigation)] ICommandService navigationCommandService, IUpdateCoordinator<string, NoteViewStatePatchDto> updateCoordinator, IMemoryCache memoryCache, NoteModel note)
  {
    // DI
    NoteCommandService = (NoteCommandService)noteCommandService;
    NavigationCommandService = (NavigationCommandService)navigationCommandService;
    NoteUpdateCoordinator = updateCoordinator;
    MemoryCache = memoryCache;

    Note = note;

    BackgroundImage = Note.ShowBackgroundImage ? GetBackgroundImage(Note.BackgroundImagePath) : null;

    Note.PropertyChanged += Note_PropertyChanged;
    SetCommands();
  }

  protected override async ValueTask DisposeAsyncCore()
  {
    Note.PropertyChanged -= Note_PropertyChanged;
  }
  #endregion

  #region Note 내부 속성 변경 시 데이터베이스에 반영 및 기타 로직 실행

  private static readonly IReadOnlyDictionary<string, PatchDescriptor<NoteModel, string, NoteViewStatePatchDto>> ViewStatePatchDescriptors = new Dictionary<string, PatchDescriptor<NoteModel, string, NoteViewStatePatchDto>>()
  {
    [nameof(NoteModel.ShowBackgroundImage)] = new()
    {
      Key = nameof(NoteModel.ShowBackgroundImage),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        ShowBackgroundImage = noteModel.ShowBackgroundImage
      }
    },
  };

  private async void Note_PropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is null)
    {
      return;
    }

    if (ViewStatePatchDescriptors.TryGetValue(e.PropertyName, out var persistenceDescriptor))
    {
      NoteUpdateCoordinator.Submit(persistenceDescriptor.Key, persistenceDescriptor.CreatePatch(Note), persistenceDescriptor.BatchMode);
    }

    // 뷰에 반영(TwoWay 바인딩 시) 
    switch (e.PropertyName)
    {
      case nameof(Note.ShowBackgroundImage):
        if (Note.ShowBackgroundImage)
        {
          Note.BackdropKind = BackdropKind.None;
          BackgroundImage = MemoryCache.TryGetValue(_backgroundImageMemoryCacheKey, out BitmapImage? cachedImage)
            ? cachedImage
            : GetBackgroundImage(Note.BackgroundImagePath);
        }
        else
        {
          if (BackgroundImage is not null)
          {
            MemoryCache.Set(_backgroundImageMemoryCacheKey, BackgroundImage, new MemoryCacheEntryOptions() { AbsoluteExpirationRelativeToNow = _backgroundImageMemoryCacheRetentionTime });
            BackgroundImage = null;
          }
        }
        break;
      case nameof(Note.BackgroundImagePath):
        BackgroundImage = Note.ShowBackgroundImage ? GetBackgroundImage(Note.BackgroundImagePath) : null;
        break;
    }
  }
  #endregion

  #region Background Image

  private readonly object _backgroundImageMemoryCacheKey = new();
  private readonly TimeSpan _backgroundImageMemoryCacheRetentionTime = TimeSpan.FromSeconds(5);
  [ObservableProperty]
  public partial BitmapImage? BackgroundImage { get; set; }

  private static BitmapImage? GetBackgroundImage(string? imagePath)
  {
    if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
    {
      return null;
    }

    try
    {
      BitmapImage image = new()
      {
        UriSource = new Uri(imagePath),
        DecodePixelType = DecodePixelType.Logical
      };
      return image;
    }
    catch (Exception)
    {
      return null;
    }
  }
  #endregion

  #region Body Images
  [ObservableProperty]
  public partial double ImagePanelMaxHeight { get; set; } = 120.0;
  #endregion
}

partial class NoteViewModel
{
  public AsyncCommand OpenWindowCommand { get; private set; }
  public Command MinimizeWindowCommand { get; private set; }
  public Command CloseWindowCommand { get; private set; }
  public Command PinWindowCommand { get; private set; }
  public AsyncCommand<NavigationId> MoveToListCommand { get; private set; }
  public AsyncCommand CreateNewNoteCommand { get; private set; }
  public AsyncCommand ViewListCommand { get; private set; }
  public AsyncCommand RenameNoteTitleCommand { get; private set; }
  public AsyncCommand ToggleBookmarkNoteCommand { get; private set; }
  public AsyncCommand RemoveNoteCommand { get; private set; }
  public AsyncCommand AddNoteToJumpListCommand { get; private set; }

  public string OldTitle { get; set; } = string.Empty;

  [MemberNotNull(nameof(OpenWindowCommand), nameof(MinimizeWindowCommand), nameof(CloseWindowCommand), nameof(PinWindowCommand), nameof(MoveToListCommand), nameof(CreateNewNoteCommand), nameof(ViewListCommand), nameof(RenameNoteTitleCommand), nameof(ToggleBookmarkNoteCommand), nameof(RemoveNoteCommand), nameof(AddNoteToJumpListCommand))]
  private void SetCommands()
  {
    OpenWindowCommand = new()
    {
      ExecuteFunc = () => NoteCommandService.OpenNoteWindowAsync(Note)
    };

    MinimizeWindowCommand = new()
    {
      ExecuteAction = () => NoteCommandService.MinimizeNoteWindow(Note.Id)
    };

    CloseWindowCommand = new()
    {
      ExecuteAction = () => NoteCommandService.CloseNoteWindow(Note.Id)
    };

    PinWindowCommand = new()
    {
      ExecuteAction = () => NoteCommandService.PinNoteWindow(Note.Id, Note.IsAlwaysOnTop)
    };

    MoveToListCommand = new()
    {
      ExecuteFunc = (targetNavigationId) => NoteCommandService.MoveNoteToListAsync(Note, targetNavigationId)
    };

    CreateNewNoteCommand = new()
    {
      ExecuteFunc = () => NoteCommandService.CreateNewNoteAsync(Note.NavigationId)
    };

    ViewListCommand = new()
    {
      ExecuteFunc = () => NavigationCommandService.ViewNavigationListPageAsync(Note.NavigationId)
    };

    RenameNoteTitleCommand = new()
    {
      ExecuteFunc = () => NoteCommandService.RenameNoteTitleAsync(Note, OldTitle)
    };

    ToggleBookmarkNoteCommand = new()
    {
      ExecuteFunc = () => NoteCommandService.ToggleBookmarkNoteAsync(Note)
    };

    RemoveNoteCommand = new()
    {
      ExecuteFunc = () => NoteCommandService.ConfirmAndDeleteNoteAsync(Note)
    };

    AddNoteToJumpListCommand = new()
    {
      ExecuteFunc = () => NoteCommandService.AddNoteToJumpList(Note)
    };
  }
}
