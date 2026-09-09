using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Helpers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Windows.Storage.Pickers;

using MyNotes.Application.Contracts.Notes.Models;
using MyNotes.Application.Notes;
using MyNotes.Application.Notes.Results;
using MyNotes.Application.Notes.Services;
using MyNotes.Application.Results;
using MyNotes.Application.Settings.Services;
using MyNotes.Common.Collections;
using MyNotes.Common.Commands;
using MyNotes.Common.Enums.Modes;
using MyNotes.Common.Helpers;
using MyNotes.Constants;
using MyNotes.Domain.Notes;
using MyNotes.Messaging;
using MyNotes.Messaging.Messages;
using MyNotes.Models.Notes;
using MyNotes.Services.Commands;
using MyNotes.Services.Updates;
using MyNotes.Services.Windows;
using MyNotes.Templates.Media;

using Windows.Storage.Streams;

namespace MyNotes.ViewModels.Notes;

internal sealed partial class NoteEditorViewModel : AsyncViewModelBase
{
  public NoteEditorViewModelState State { get; private set; } = NoteEditorViewModelState.Initializing;

  private readonly IUpdateCoordinator<string, NotePatchDto, UpdateNoteResult> NoteUpdateCoordinator;
  private readonly IUpdateCoordinator<string, NoteViewStatePatchDto> ViewStateUpdateCoordinator;
  private readonly NoteService NoteService;
  private readonly NoteWindowService NoteWindowService;
  private readonly NoteCommandService NoteCommandService;
  private readonly AppSettingsService AppSettingsService;

  private readonly IAsyncViewModelLease<NoteViewModel> NoteViewModelLease;
  public NoteViewModel NoteViewModel => NoteViewModelLease.ViewModel;
  private NoteModel Note => NoteViewModel.Note;

  private RichEditTextDocument Document
  {
    get => field ?? throw new InvalidOperationException("Document가 초기화되지 않았습니다. AttachDocument 메서드를 호출하여 RichEditTextDocument를 연결하세요.");
    set;
  }

  #region Object Lifetime Management
  public NoteEditorViewModel(IUpdateCoordinator<string, NotePatchDto, UpdateNoteResult> noteUpdateCoordinator, IUpdateCoordinator<string, NoteViewStatePatchDto> viewStateUpdateCoordinator, NoteService noteService, NoteWindowService noteWindowService, [FromKeyedServices(CommandServiceType.Note)] ICommandService noteCommandService, AppSettingsService appSettingsService, IAsyncViewModelLease<NoteViewModel> noteViewModelLease)
  {
    NoteUpdateCoordinator = noteUpdateCoordinator;
    ViewStateUpdateCoordinator = viewStateUpdateCoordinator;
    NoteService = noteService;
    NoteWindowService = noteWindowService;
    NoteCommandService = (NoteCommandService)noteCommandService;
    AppSettingsService = appSettingsService;

    NoteViewModelLease = noteViewModelLease;

    InitializeMembers();

    ChangeSystemBackdrop();
    ChangeSystemBackdropExtended();
    SetCommands();

    State = NoteEditorViewModelState.Initialized;
  }

  private void InitializeMembers()
  {
    _selectedPaletteBackgroundColor = PaletteBackgroundColors.FirstOrDefault(b => b.Color == Note.BackgroundColor);
    _backgroundImageAlignmentX = GeometryHelper.GetAlignmentX(Note.BackgroundImageAlignment);
    _backgroundImageAlignmentY = GeometryHelper.GetAlignmentY(Note.BackgroundImageAlignment);
  }

  public void AttachDocument(RichEditTextDocument document)
  {
    if (State is not NoteEditorViewModelState.Initialized)
    {
      throw new InvalidOperationException();
    }

    Document = document;
    State = NoteEditorViewModelState.DocumentReady;
  }

  private readonly SemaphoreSlim _bodyOperationSemaphoreSlim = new(1, 1);

  private Task? _loadBodyTask;
  public Task LoadBodyAsync() => _loadBodyTask ??= LoadBodyAsyncCore();

  private async Task LoadBodyAsyncCore()
  {
    await _bodyOperationSemaphoreSlim.WaitAsync();
    try
    {
      if (State is not NoteEditorViewModelState.DocumentReady)
      {
        return;
      }

      State = NoteEditorViewModelState.Loading;

      // Editor BodyText
      Document.LoadFromStream(TextSetOptions.FormatRtf, await StreamHelper.ToRandomAccessStreamAsync(Note.Body));
      State = NoteEditorViewModelState.Loaded;
      _bodyEditorBatchTimer.Tick += BodyEditorBatchTimer_Tick;

      Note.PropertyChanged += Note_PropertyChanged;
    }
    catch
    {
      State = NoteEditorViewModelState.Failed;
    }
    finally
    {
      _bodyOperationSemaphoreSlim.Release();
    }
  }

  protected override async ValueTask DisposeAsyncCore()
  {
    await _bodyOperationSemaphoreSlim.WaitAsync();
    try
    {
      var previousState = State;
      State = NoteEditorViewModelState.Disposing;

      _bodyEditorBatchTimer.Stop();
      _bodyEditorBatchTimer.Tick -= BodyEditorBatchTimer_Tick;

      Note.PropertyChanged -= Note_PropertyChanged;

      if (previousState is NoteEditorViewModelState.Loaded && !await DeleteNotePermanentlyWhenEmpty())
      {
        await UpdateNoteBodyAsyncCore(CancellationToken.None);
        await NoteService.Modification.CommitSearchIndexAsync();
      }
    }
    finally
    {
      await NoteViewModelLease.DisposeAsync();
      State = NoteEditorViewModelState.Disposed;
      _bodyOperationSemaphoreSlim.Release();
    }
  }
  #endregion

  private async Task<bool> DeleteNotePermanentlyWhenEmpty()
  {
    if (AppSettingsService.Load(AppSettingsDescriptors.DeleteEmptyNote))
    {
      Document.GetText(TextGetOptions.UseLf, out string bodyPlainText);
      if (string.IsNullOrEmpty(Note.Title) && string.IsNullOrWhiteSpace(bodyPlainText))
      {
        await NoteCommandService.DeleteNoteAsync(Note, DeleteMode.Permanent);
        return true;
      }
    }

    return false;
  }
}

// Body(본문) 속성 이외의 Note, NoteViewState 속성 관련
partial class NoteEditorViewModel
{
  private static readonly IReadOnlyDictionary<string, PatchDescriptor<NoteModel, string, NotePatchDto>> NotePatchDescriptors = new Dictionary<string, PatchDescriptor<NoteModel, string, NotePatchDto>>()
  {
    [nameof(NoteModel.Body)] = new()
    {
      Key = nameof(NoteModel.Body),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NotePatchDto()
      {
        Id = noteModel.Id,
        Body = noteModel.Body
      }
    },
    [nameof(NoteModel.BackgroundColor)] = new()
    {
      Key = nameof(NoteModel.BackgroundColor),
      BatchMode = UpdateBatchMode.Batched,
      CreatePatch = (noteModel) => new NotePatchDto()
      {
        Id = noteModel.Id,
        BackgroundColor = noteModel.BackgroundColor.ToString()
      }
    },
    [nameof(NoteModel.BackgroundImagePath)] = new()
    {
      Key = nameof(NoteModel.BackgroundImagePath),
      BatchMode = UpdateBatchMode.Batched,
      CreatePatch = (noteModel) => new NotePatchDto()
      {
        Id = noteModel.Id,
        BackgroundImagePath = noteModel.BackgroundImagePath
      }
    },
  };

  private static readonly IReadOnlyDictionary<string, PatchDescriptor<NoteModel, string, NoteViewStatePatchDto>> ViewStatePatchDescriptors = new Dictionary<string, PatchDescriptor<NoteModel, string, NoteViewStatePatchDto>>()
  {
    [nameof(NoteModel.BackgroundImageStretch)] = new()
    {
      Key = nameof(NoteModel.BackgroundImageStretch),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        BackgroundImageStretch = (int)noteModel.BackgroundImageStretch
      }
    },
    [nameof(NoteModel.BackgroundImageAlignment)] = new()
    {
      Key = nameof(NoteModel.BackgroundImageAlignment),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        BackgroundImageAlignment = noteModel.BackgroundImageAlignment
      }
    },
    [nameof(NoteModel.BackgroundImageOpacity)] = new()
    {
      Key = nameof(NoteModel.BackgroundImageOpacity),
      BatchMode = UpdateBatchMode.Batched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        BackgroundImageOpacity = Math.Round(noteModel.BackgroundImageOpacity, 2, MidpointRounding.AwayFromZero)
      }
    },
    [nameof(NoteModel.BackgroundImageBlur)] = new()
    {
      Key = nameof(NoteModel.BackgroundImageBlur),
      BatchMode = UpdateBatchMode.Batched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        BackgroundImageBlur = Math.Round(noteModel.BackgroundImageBlur, 2, MidpointRounding.AwayFromZero)
      }
    },
    [nameof(NoteModel.BackdropKind)] = new()
    {
      Key = nameof(NoteModel.BackdropKind),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        BackdropKind = noteModel.BackdropKind
      }
    },
    [nameof(NoteModel.BackdropTintOpacity)] = new()
    {
      Key = nameof(NoteModel.BackdropTintOpacity),
      BatchMode = UpdateBatchMode.Batched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        BackdropTintOpacity = Math.Round(noteModel.BackdropTintOpacity, 2, MidpointRounding.AwayFromZero)
      }
    },
    [nameof(NoteModel.BackdropLuminosityOpacity)] = new()
    {
      Key = nameof(NoteModel.BackdropLuminosityOpacity),
      BatchMode = UpdateBatchMode.Batched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        BackdropLuminosityOpacity = Math.Round(noteModel.BackdropLuminosityOpacity, 2, MidpointRounding.AwayFromZero)
      }
    },
    [nameof(NoteModel.ShowImagePanel)] = new()
    {
      Key = nameof(NoteModel.ShowImagePanel),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        ShowImagePanel = noteModel.ShowImagePanel
      }
    },
    [nameof(NoteModel.ImagePanelHeight)] = new()
    {
      Key = nameof(NoteModel.ImagePanelHeight),
      BatchMode = UpdateBatchMode.Batched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        ImagePanelHeight = noteModel.ImagePanelHeight
      }
    },
    [nameof(NoteModel.Size)] = new()
    {
      Key = nameof(NoteModel.Size),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        Width = noteModel.Size.Width,
        Height = noteModel.Size.Height
      }
    },
    [nameof(NoteModel.Position)] = new()
    {
      Key = nameof(NoteModel.Position),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        PositionX = noteModel.Position.X,
        PositionY = noteModel.Position.Y
      }
    },
    [nameof(NoteModel.IsTextEditorReadOnly)] = new()
    {
      Key = nameof(NoteModel.IsTextEditorReadOnly),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        IsTextEditorReadOnly = noteModel.IsTextEditorReadOnly
      }
    },
    [nameof(NoteModel.IsWindowOpen)] = new()
    {
      Key = nameof(NoteModel.IsWindowOpen),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        IsWindowOpen = noteModel.IsWindowOpen
      }
    },
    [nameof(NoteModel.IsAlwaysOnTop)] = new()
    {
      Key = nameof(NoteModel.IsAlwaysOnTop),
      BatchMode = UpdateBatchMode.Unbatched,
      CreatePatch = (noteModel) => new NoteViewStatePatchDto()
      {
        Id = noteModel.Id,
        IsAlwaysOnTop = noteModel.IsAlwaysOnTop
      }
    },
  };

  private async Task<UpdateNoteResult> UpdateAsync(string propertyName) => NotePatchDescriptors.TryGetValue(propertyName, out var notePatchDescriptor)
    ? await NoteUpdateCoordinator.Submit(notePatchDescriptor.Key, notePatchDescriptor.CreatePatch(Note), notePatchDescriptor.BatchMode)
    : new UpdateNoteResult() { Status = AppUpdateStatus.Failed };

  // Note 속성 변경 시 변경 속성과 값 Patch
  // 뷰(NotePage 및 NoteWindow)에 속성 바인딩(TwoWay)으로 전달할 수 없는 동작 정의 및 실행
  private async void Note_PropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is null)
    {
      return;
    }

    if (ViewStatePatchDescriptors.TryGetValue(e.PropertyName, out var viewStatePatchDescriptor))
    {
      ViewStateUpdateCoordinator.Submit(viewStatePatchDescriptor.Key, viewStatePatchDescriptor.CreatePatch(Note), viewStatePatchDescriptor.BatchMode);
    }

    // 뷰에 반영(TwoWay 바인딩 시) 
    switch (e.PropertyName)
    {
      case nameof(Note.BackgroundColor):
        SelectedPaletteBackgroundColor = PaletteBackgroundColors.FirstOrDefault(b => b.Color == Note.BackgroundColor);
        ChangeSystemBackdropExtended();
        var updateResult = await UpdateAsync(nameof(NoteModel.BackgroundColor));
        break;
      case nameof(Note.BackdropKind):
        ChangeSystemBackdrop();
        ChangeSystemBackdropExtended();
        if (Note.BackdropKind is not BackdropKind.None)
        {
          Note.ShowBackgroundImage = false;
        }
        break;
      case nameof(Note.BackdropTintOpacity):
        ChangeSystemBackdropExtended();
        break;
      case nameof(Note.BackdropLuminosityOpacity):
        ChangeSystemBackdropExtended();
        break;
    }
  }

  // Background 관련 뷰모델 속성 정의
  #region Background & Backdrop
  public IReadOnlyList<SolidColorBrush> PaletteBackgroundColors { get; } = [.. AppColors.DefaultPaletteColors.Select(c => new SolidColorBrush(c.ToColor()))];

  private SolidColorBrush? _selectedPaletteBackgroundColor;
  public SolidColorBrush? SelectedPaletteBackgroundColor
  {
    get => _selectedPaletteBackgroundColor;
    set
    {
      if (SetProperty(ref _selectedPaletteBackgroundColor, value) && value is not null)
      {
        Note.BackgroundColor = value.Color;
      }
    }
  }

  private AlignmentX _backgroundImageAlignmentX;
  public AlignmentX BackgroundImageAlignmentX
  {
    get => _backgroundImageAlignmentX;
    set
    {
      if (Enum.IsDefined(value))
      {
        SetProperty(ref _backgroundImageAlignmentX, value);
        Note.BackgroundImageAlignment = GeometryHelper.GetAlignmentPosition(value, BackgroundImageAlignmentY);
      }
    }
  }

  private AlignmentY _backgroundImageAlignmentY;
  public AlignmentY BackgroundImageAlignmentY
  {
    get => _backgroundImageAlignmentY;
    set
    {
      if (Enum.IsDefined(value))
      {
        SetProperty(ref _backgroundImageAlignmentY, value);
        Note.BackgroundImageAlignment = GeometryHelper.GetAlignmentPosition(BackgroundImageAlignmentX, value);
      }
    }
  }

  public void ChangeSystemBackdrop()
  {
    NoteWindowService.TryExecuteOnWindow(Note.Id, (noteWindow) =>
    {
      noteWindow.SystemBackdrop = Note.BackdropKind switch
      {
        BackdropKind.None => null,
        BackdropKind.Acrylic => new ExtendedAcrylicBackdrop(),
        BackdropKind.Mica => new ExtendedMicaBackdrop(),
        _ => throw new InvalidOperationException()
      };
    });
  }

  public void ChangeSystemBackdropExtended()
  {
    NoteWindowService.TryExecuteOnWindow(Note.Id, (noteWindow) =>
    {
      if (noteWindow.SystemBackdrop is ExtendedSystemBackdrop backdrop)
      {
        backdrop.TintColor = Note.BackgroundColor;
        backdrop.TintOpacity = Note.BackdropTintOpacity;
        backdrop.LuminosityOpacity = Note.BackdropLuminosityOpacity;
        backdrop.FallbackColor = Common.Helpers.ColorHelper.GetFallbackColor(Note.BackgroundColor, Note.BackdropTintOpacity);
      }
    });
  }
  #endregion

}

// TextEditor(RichEditDocument), 본문(Body 속성) 및 본문 서식 관련
partial class NoteEditorViewModel
{
  private bool _isUpdatingSelectionFormatStates = false;
  public void UpdateSelectionFormatStates()
  {
    _isUpdatingSelectionFormatStates = true;
    var characterFormat = Document.Selection.CharacterFormat;
    var paragraphFormat = Document.Selection.ParagraphFormat;

    IsSelectionBold = characterFormat.Bold is FormatEffect.On;
    IsSelectionItalic = characterFormat.Italic is FormatEffect.On;
    IsSelectionUnderlined = characterFormat.Underline is UnderlineType.Single;
    IsSelectionStrikethrough = characterFormat.Strikethrough is FormatEffect.On;
    SelectionFontSizeText = characterFormat.Size > 0 ? characterFormat.Size.ToString() : string.Empty;
    SelectionFontColor = characterFormat.ForegroundColor;
    SelectionHighlightColor = characterFormat.BackgroundColor;
    IsSelectionParagraphList = paragraphFormat.ListType is not (MarkerType.Undefined or MarkerType.None);
    SelectionMarkerType = paragraphFormat.ListType;
    SelectionMarkerStyle = paragraphFormat.ListStyle;
    IsSelectionMarkerStyleEnabled = SelectionMarkerType is not (MarkerType.None or MarkerType.Undefined or MarkerType.Bullet);
    _isUpdatingSelectionFormatStates = false;
  }

  #region Bold, Italic, Underline, Strikethrough
  public bool IsSelectionBold
  {
    get;
    set
    {
      if (SetProperty(ref field, value))
      {
        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle;
        }
      }
    }
  }

  public bool IsSelectionItalic
  {
    get;
    set
    {
      if (SetProperty(ref field, value))
      {
        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.CharacterFormat.Italic = FormatEffect.Toggle;
        }
      }
    }
  }

  public bool IsSelectionUnderlined
  {
    get;
    set
    {
      if (SetProperty(ref field, value))
      {
        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.CharacterFormat.Underline = (Document.Selection.CharacterFormat.Underline == UnderlineType.Single) ? UnderlineType.None : UnderlineType.Single;
        }
      }
    }
  }

  public bool IsSelectionStrikethrough
  {
    get;
    set
    {
      if (SetProperty(ref field, value))
      {
        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.CharacterFormat.Strikethrough = FormatEffect.Toggle;
        }
      }
    }
  }
  #endregion

  #region Bullets and Numbering
  public bool IsSelectionParagraphList
  {
    get;
    set
    {
      if (SetProperty(ref field, value))
      {
        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.ParagraphFormat.ListType = value ? _recentMarkerType : MarkerType.None;
          Document.Selection.ParagraphFormat.ListStyle = value ? _recentMarkerStyle : MarkerStyle.Undefined;
        }
      }
    }
  }

  public static readonly BijectiveMap<MarkerType, string> _markerTypeMap = new()
  {
    { MarkerType.None,  "\u25cc" },
    { MarkerType.Bullet, "\u2022" },
    { MarkerType.Arabic, "1" },
    { MarkerType.LowercaseEnglishLetter, "a" },
    { MarkerType.UppercaseEnglishLetter,  "A" },
    { MarkerType.LowercaseRoman, "\u2170" },
    { MarkerType.UppercaseRoman, "\u2160" },
    { MarkerType.CircledNumber, "\u2460" }
  };
  public IReadOnlyBijectiveMap<MarkerType, string> MarkerTypeMap => _markerTypeMap;

  public static readonly BijectiveMap<MarkerStyle, string> _markerStyleMap = new()
  {
    { MarkerStyle.Parenthesis, ")" },
    { MarkerStyle.Parentheses, "( )" },
    { MarkerStyle.Period, "." },
    { MarkerStyle.Minus,  "-" },
  };
  public IReadOnlyBijectiveMap<MarkerStyle, string> MarkerStyleMap => _markerStyleMap;

  private MarkerType _recentMarkerType = MarkerType.Bullet;
  private MarkerStyle _recentMarkerStyle = MarkerStyle.Parenthesis;

  public MarkerType SelectionMarkerType
  {
    get;
    set
    {
      if (SetProperty(ref field, value))
      {
        SelectedMarkerType = MarkerTypeMap.TryGetRight(value, out var markerType) ? markerType : null;
        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.ParagraphFormat.ListType = value;
          if (value is not MarkerType.None)
          {
            _recentMarkerType = value;
          }
        }
      }
    }
  }

  public string? SelectedMarkerType
  {
    get;
    set
    {
      if (SetProperty(ref field, value) && value is not null)
      {
        SelectionMarkerType = MarkerTypeMap.TryGetLeft(value, out var markerType) ? markerType : MarkerType.Undefined;
      }
    }
  }

  public MarkerStyle SelectionMarkerStyle
  {
    get;
    set
    {
      if (SetProperty(ref field, value))
      {
        SelectedMarkerStyle = SelectionMarkerType switch
        {
          MarkerType.None or MarkerType.Undefined or MarkerType.Bullet => null,
          _ => MarkerStyleMap.TryGetRight(value, out var markerStyle) ? markerStyle : null
        };

        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.ParagraphFormat.ListStyle = value;
          _recentMarkerStyle = value;
        }
      }
    }
  }

  public string? SelectedMarkerStyle
  {
    get;
    set
    {
      if (SetProperty(ref field, value) && value is not null)
      {
        SelectionMarkerStyle = MarkerStyleMap.TryGetLeft(value, out var markerStyle) ? markerStyle : MarkerStyle.Undefined;
      }
    }
  }

  [ObservableProperty]
  public partial bool IsSelectionMarkerStyleEnabled { get; private set; }

  #region Font Size
  public readonly ImmutableList<string> FontSizes = ["8", "9", "10.5", "12", "14", "16", "18", "20", "24", "28", "32", "36", "48", "60", "72"];

  public string SelectionFontSizeText
  {
    get;
    set
    {
      if (ValidateFontSize(ref value))
      {
        SetProperty(ref field, value);
        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.CharacterFormat.Size = SelectionFontSize;
        }
      }
      else
      {
        SetProperty(ref field, string.Empty);
      }

      DecreaseSelectionFontSizeCommand?.NotifyCanExecuteChanged();
      IncreaseSelectionFontSizeCommand?.NotifyCanExecuteChanged();
    }
  } = string.Empty;

  public float SelectionFontSize => float.TryParse(SelectionFontSizeText, out float fontSize) ? fontSize : 10.5f;

  private static readonly float _minEditorFontSize = 5.0f;
  private static readonly float _maxEditorFontSize = 512.0f;

  private static bool ValidateFontSize(ref string fontSizeText)
  {
    if (!string.IsNullOrWhiteSpace(fontSizeText)
      && float.TryParse(fontSizeText, out float fontSize)
      && fontSize >= _minEditorFontSize
      && fontSize <= _maxEditorFontSize)
    {
      float eps = 1e-6f;
      float truncated = (float)Math.Truncate(fontSize * 100) / 100f;
      float fraction = Math.Abs(fontSize - (float)Math.Floor(fontSize));

      if (Math.Abs(fraction - 0.0f) < eps || Math.Abs(fraction - 0.5f) < eps)
      {
        if (Math.Abs(fontSize - truncated) < eps)
        {
          return true;
        }
      }
    }

    fontSizeText = string.Empty;
    return false;
  }
  #endregion

  #region Font Color
  public IReadOnlyList<SolidColorBrush> PaletteFontColors { get; } = [.. AppColors.DefaultPaletteColors.Select(c => new SolidColorBrush(c.ToColor()))];

  public SolidColorBrush? SelectedPaletteFontColor
  {
    get;
    set
    {
      if (SetProperty(ref field, value) && value is not null)
      {
        SelectionFontColor = value.Color;
      }
    }
  }

  [ObservableProperty]
  public partial Color RecentFontColor { get; set; } = Colors.Black;

  public Color SelectionFontColor
  {
    get;
    set
    {
      if (SetProperty(ref field, value))
      {
        SelectedPaletteFontColor = PaletteFontColors.FirstOrDefault(b => b.Color == value);
        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.CharacterFormat.ForegroundColor = value;
          RecentFontColor = value;
        }
      }
    }
  }
  #endregion

  #region Text Highlight Color
  public IReadOnlyList<SolidColorBrush> PaletteHighlightColors { get; } = [.. AppColors.DefaultPaletteColors.Select(c => new SolidColorBrush(c.ToColor()))];

  public SolidColorBrush? SelectedPaletteHighlightColor
  {
    get;
    set
    {
      if (SetProperty(ref field, value) && value is not null)
      {
        SelectionHighlightColor = value.Color;
      }
    }
  }

  [ObservableProperty]
  public partial Color RecentHighlightColor { get; set; } = Colors.Transparent;

  public Color SelectionHighlightColor
  {
    get;
    set
    {
      if (SetProperty(ref field, value))
      {
        SelectedPaletteHighlightColor = PaletteHighlightColors.FirstOrDefault(b => b.Color == value);
        ChangeSelectionHighlightColorToAutomaticCommand?.NotifyCanExecuteChanged();

        if (!_isUpdatingSelectionFormatStates)
        {
          Document.Selection.CharacterFormat.BackgroundColor = value;
          RecentHighlightColor = value;
        }
      }
    }
  }
  #endregion
}

// Commands
partial class NoteEditorViewModel
{
  public Command UpdateSelectionCommand { get; private set; }
  public Command UpdateTextChangingCommand { get; private set; }
  public AsyncCommand UpdateTextChangedCommand { get; private set; }
  public Command DecreaseSelectionFontSizeCommand { get; private set; }
  public Command IncreaseSelectionFontSizeCommand { get; private set; }
  public Command ChangeSelectionFontColorCommand { get; private set; }
  public Command ChangeSelectionHighlightColorCommand { get; private set; }
  public Command ChangeSelectionHighlightColorToAutomaticCommand { get; private set; }
  public Command EnterEditModeCommand { get; private set; }
  public AsyncCommand BrowseBackgroundImageCommand { get; private set; }
  public Command ChangeBackdropTintOpacityToDefaultCommand { get; private set; }
  public Command ChangeBackdropLuminosityOpacityToDefaultCommand { get; private set; }
  public Command ChangeBackgroundImageOpacityToDefaultCommand { get; private set; }

  public Command ChangeBackgroundImageBlurToDefaultCommand { get; private set; }

  private int _previousSelectionIndex = 0;
  private int _currentSelectionIndex = 0;

  private bool _shouldChangePreview = false;
  public readonly int PreviewTextMaxLength = 500;

  private readonly DispatcherTimer _bodyEditorBatchTimer = new() { Interval = TimeSpan.FromMilliseconds(2000) };
  private Task _updateNoteBodyTask = Task.CompletedTask;

  private void BodyEditorBatchTimer_Tick(object? sender, object e) => _updateNoteBodyTask = UpdateNoteBodyAsync();

  public async Task<bool> UpdateNoteBodyAsync(CancellationToken cancellationToken = default)
  {
    await _bodyOperationSemaphoreSlim.WaitAsync(cancellationToken);
    try
    {
      return State is NoteEditorViewModelState.Loaded && await UpdateNoteBodyAsyncCore(cancellationToken);
    }
    finally
    {
      _bodyOperationSemaphoreSlim.Release();
    }
  }

  private async Task<bool> UpdateNoteBodyAsyncCore(CancellationToken cancellationToken = default)
  {
    _bodyEditorBatchTimer.Stop();

    try
    {
      using IRandomAccessStream randomAccessStream = new InMemoryRandomAccessStream();
      Document.SaveToStream(TextGetOptions.FormatRtf, randomAccessStream);
      Note.Body = await StreamHelper.ToByteArrayAsync(randomAccessStream, cancellationToken);
      var updateResult = await UpdateAsync(nameof(NoteModel.Body));
      if (updateResult.Status is AppUpdateStatus.Succeeded)
      {
        if (_shouldChangePreview)
        {
          WeakReferenceMessenger.Default.Send(new NotePreviewUpdateRequestedMessage(Note.Id), MessageToken<NoteId>.Create(Note.Id));
        }
        return true;
      }
    }
    catch
    {
    }

    _shouldChangePreview = false;
    return false;
  }

  [MemberNotNull(nameof(UpdateSelectionCommand), nameof(UpdateTextChangingCommand), nameof(UpdateTextChangedCommand), nameof(DecreaseSelectionFontSizeCommand), nameof(IncreaseSelectionFontSizeCommand), nameof(ChangeSelectionFontColorCommand), nameof(ChangeSelectionHighlightColorCommand), nameof(ChangeSelectionHighlightColorToAutomaticCommand), nameof(EnterEditModeCommand), nameof(BrowseBackgroundImageCommand), nameof(ChangeBackdropTintOpacityToDefaultCommand), nameof(ChangeBackdropLuminosityOpacityToDefaultCommand), nameof(ChangeBackgroundImageOpacityToDefaultCommand), nameof(ChangeBackgroundImageBlurToDefaultCommand))]
  private void SetCommands()
  {
    UpdateSelectionCommand = new()
    {
      ExecuteAction = () =>
      {
        _previousSelectionIndex = Document.Selection.GetIndex(0);
        UpdateSelectionFormatStates();
      }
    };

    UpdateTextChangingCommand = new()
    {
      ExecuteAction = () =>
      {
        _currentSelectionIndex = Document.Selection.GetIndex(0);
        _shouldChangePreview = _previousSelectionIndex <= PreviewTextMaxLength && _currentSelectionIndex <= PreviewTextMaxLength;
      }
    };

    UpdateTextChangedCommand = new()
    {
      ExecuteFunc = async () =>
      {
        UpdateSelectionFormatStates();
        await _updateNoteBodyTask;
        _bodyEditorBatchTimer.Start();
      }
    };

    DecreaseSelectionFontSizeCommand = new()
    {
      ExecuteAction = () =>
      {
        var newFontSize = SelectionFontSize switch
        {
          5 => 5,
          > 5 and <= 12 => SelectionFontSize - 0.5f,
          <= 20 => SelectionFontSize - 1.0f,
          <= 32 => SelectionFontSize.LessThanNearestMultiple(2),
          <= 64 => SelectionFontSize.LessThanNearestMultiple(4),
          <= 512 => SelectionFontSize.LessThanNearestMultiple(8),
          _ => 0
        };
        Document.Selection.CharacterFormat.Size = newFontSize;
        SelectionFontSizeText = newFontSize.ToString();
      },
      CanExecuteFunc = () => SelectionFontSize > _minEditorFontSize
    };

    IncreaseSelectionFontSizeCommand = new()
    {
      ExecuteAction = () =>
      {
        var newFontSize = SelectionFontSize switch
        {
          >= 5 and < 12 => SelectionFontSize + 0.5f,
          < 20 => SelectionFontSize + 1.0f,
          < 32 => SelectionFontSize.GreaterThanNearestMultiple(2),
          < 64 => SelectionFontSize.GreaterThanNearestMultiple(4),
          < 512 => SelectionFontSize.GreaterThanNearestMultiple(8),
          512 => 512,
          _ => 0
        };
        Document.Selection.CharacterFormat.Size = newFontSize;
        SelectionFontSizeText = newFontSize.ToString();
      },
      CanExecuteFunc = () => SelectionFontSize < _maxEditorFontSize
    };

    ChangeSelectionFontColorCommand = new()
    {
      ExecuteAction = () => Document.Selection.CharacterFormat.ForegroundColor = RecentFontColor
    };

    ChangeSelectionHighlightColorCommand = new()
    {
      ExecuteAction = () => Document.Selection.CharacterFormat.BackgroundColor = RecentHighlightColor
    };

    ChangeSelectionHighlightColorToAutomaticCommand = new()
    {
      ExecuteAction = () => SelectionHighlightColor = Colors.Transparent,
      CanExecuteFunc = () => SelectionHighlightColor != Colors.White
    };

    EnterEditModeCommand = new()
    {
      ExecuteAction = () => Note.IsTextEditorReadOnly = false
    };

    BrowseBackgroundImageCommand = new()
    {
      ExecuteFunc = async () =>
      {
        if (NoteWindowService.TryGetWindowInfo(Note.Id, out _, out var appWindow))
        {
          PickFileResult pickFileResult = await new FileOpenPicker(appWindow.Id).PickSingleFileAsync();
          if (pickFileResult is not null)
          {
            Note.BackgroundImagePath = pickFileResult.Path;
            await UpdateAsync(nameof(NoteModel.BackgroundImagePath));
          }
        }
      }
    };

    ChangeBackdropTintOpacityToDefaultCommand = new()
    {
      ExecuteAction = () => Note.BackdropTintOpacity = NoteSettingsDescriptors.NoteBackdropTintOpacity
    };

    ChangeBackdropLuminosityOpacityToDefaultCommand = new()
    {
      ExecuteAction = () => Note.BackdropLuminosityOpacity = NoteSettingsDescriptors.NoteBackdropLuminosityOpacity
    };

    ChangeBackgroundImageOpacityToDefaultCommand = new()
    {
      ExecuteAction = () => Note.BackgroundImageOpacity = NoteSettingsDescriptors.NoteBackgroundImageOpacity
    };

    ChangeBackgroundImageBlurToDefaultCommand = new()
    {
      ExecuteAction = () => Note.BackgroundImageBlur = NoteSettingsDescriptors.NoteBackgroundImageBlur
    };
  }
  #endregion
}

internal enum NoteEditorViewModelState
{
  Initializing,
  Initialized,
  DocumentReady,
  Loading,
  Loaded,
  Failed,
  Disposing,
  Disposed
}