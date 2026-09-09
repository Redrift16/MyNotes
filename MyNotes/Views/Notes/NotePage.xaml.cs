using System.Runtime.InteropServices;

using CommunityToolkit.Mvvm.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Windows.Storage.Pickers;

using MyNotes.Application.Settings.Services;
using MyNotes.Common.Converters.Codecs;
using MyNotes.Common.Helpers;
using MyNotes.Common.Interop;
using MyNotes.Constants;
using MyNotes.Domain.Navigations;
using MyNotes.Domain.Notes;
using MyNotes.Messaging;
using MyNotes.Messaging.Messages;
using MyNotes.Models.Media;
using MyNotes.Models.Notes;
using MyNotes.Models.UI;
using MyNotes.Services.Dialogs;
using MyNotes.Strings;
using MyNotes.ViewModels;
using MyNotes.ViewModels.Media;
using MyNotes.ViewModels.Media.Providers;
using MyNotes.ViewModels.Navigations.Contents.Providers;
using MyNotes.ViewModels.Notes;
using MyNotes.ViewModels.Notes.Providers;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.System;

namespace MyNotes.Views.Notes;

[Debugging.Attributes.ReferenceTracker]
internal sealed partial class NotePage : Page, ITitleBarProvider, IAsyncDisposable
{
  // ViewModel Lease
  private readonly IAsyncViewModelLease<NoteEditorViewModel> EditorViewModelLease;
  private readonly IAsyncViewModelLease<ImageCollectionViewModel> ImageCollectionViewModelLease;

  // ViewModel
  private NoteEditorViewModel EditorViewModel => EditorViewModelLease.ViewModel;
  private NoteViewModel NoteViewModel => EditorViewModel.NoteViewModel;
  private ImageCollectionViewModel ImageCollectionViewModel => ImageCollectionViewModelLease.ViewModel;

  private NoteModel Note => NoteViewModel.Note;
  public UIElement TitleBarElement { get; }

  #region Object Lifetime Management
  public static async Task<NotePage> CreateAsync(NoteModel note)
  {
    var NoteEditorViewModelProvider = App.Services.GetRequiredService<NoteEditorViewModelProvider>();
    var editorViewModelLease = await NoteEditorViewModelProvider.ResolveAsync(note);

    var ImageCollectionViewModelProvider = App.Services.GetRequiredService<ImageCollectionViewModelProvider>();
    var imageCollectionViewModelLease = await ImageCollectionViewModelProvider.ResolveAsync(new ImageCollectionKey(note.Id.Value));

    return new NotePage(editorViewModelLease, imageCollectionViewModelLease);
  }

  private NotePage(IAsyncViewModelLease<NoteEditorViewModel> editorViewModelLease, IAsyncViewModelLease<ImageCollectionViewModel> imageCollectionViewModelLease)
  {
    TrackReference();
    InitializeComponent();
    TitleBarElement = NotePage_TitleBarGrid;

    EditorViewModelLease = editorViewModelLease;
    ImageCollectionViewModelLease = imageCollectionViewModelLease;

    EditorViewModel.AttachDocument(NotePage_TextEditorRichEditBox.Document);

    var appSettingsService = App.Services.GetRequiredService<AppSettingsService>();
    ChangeFlyoutTheme(appSettingsService.Load(ElementThemeSettingsCodec.Default, AppSettingsDescriptors.AppTheme));

    RegisterMessengers();

    _infoBarDismissTimer.Tick += InfoBarDismissTimer_Tick;
    _appWindowSizePotionUpdateTimer.Tick += AppWindowSizePotionUpdateTimer_Tick;

    this.Loaded += NotePage_Loaded;
    this.Unloaded += NotePage_Unloaded;
  }

  private async void NotePage_Loaded(object sender, RoutedEventArgs e)
  {
    var windowId = this.XamlRoot.ContentIslandEnvironment.AppWindowId;
    var hWnd = Win32Interop.GetWindowFromWindowId(windowId);
    var appWindow = AppWindow.GetFromWindowId(windowId);

    appWindow.Closing += AppWindow_Closing;
    appWindow.Changed += AppWindow_Changed;

    Note.IsWindowOpen = true;
    (appWindow.Presenter as OverlappedPresenter)?.IsAlwaysOnTop = Note.IsAlwaysOnTop;

    _newWndProcCallback = (handle, msg, wParam, lParam) =>
    {
      // 시스템에 의한 종료 시 창 복원을 위해 창 닫힘을 기록하지 않음
      switch (msg)
      {
        case (uint)NativeMethods.WindowMessage.WM_CLOSE:
          break;
        case (uint)NativeMethods.WindowMessage.WM_QUERYENDSESSION:
          _isManualClose = false;
          break;
      }

      // 기존 wndProc 호출
      return NativeMethods.CallWindowProc(_oldWndProc, handle, msg, wParam, lParam);
    };

    _newWndProc = Marshal.GetFunctionPointerForDelegate(_newWndProcCallback);
    _oldWndProc = NativeMethods.SetWindowLongPtr(hWnd, GWLP_WNDPROC, _newWndProc);

    if (Note.NavigationId == NavigationId.Empty)
    {
      var dialogService = App.Services.GetRequiredService<DialogService>();
      var noteListViewModelProvider = App.Services.GetRequiredService<NavigationNoteListViewModelProvider>();
      var dialogResponse = await dialogService.ShowSelectNoteParentDialogAsync(XamlRoot);
      var contentDialogResult = dialogResponse.Result;
      switch (contentDialogResult)
      {
        case ContentDialogResult.Primary:
          if (dialogResponse.Data is NavigationId parentId && parentId != NavigationId.Empty)
          {
            //Note.NavigationId = parentId;
            //WeakReferenceMessenger.Default.Send(new ValueChangedMessage<NoteModel>(Note), AppMessageTokens.AddNoteToListToken(navigationViewModel.Navigation));
          }
          break;
        case ContentDialogResult.None:
          NoteViewModel.CloseWindowCommand.Execute(Note);
          break;
      }
    }

    await EditorViewModel.LoadBodyAsync();
  }

  private void NotePage_Unloaded(object sender, RoutedEventArgs e)
  {
    Bindings.StopTracking();
  }

  private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
  {
    sender.Changed -= AppWindow_Changed;
    sender.Closing -= AppWindow_Closing;
    _appWindowSizePotionUpdateTimer.Tick -= AppWindowSizePotionUpdateTimer_Tick;
    if (AllowAppWindowSizePositionUpdate(sender))
    {
      UpdateWindowSizeAndPosition();
    }

    if (_isManualClose)
    {
      Note.IsWindowOpen = false;
    }

    IntPtr hWnd = Win32Interop.GetWindowFromWindowId(sender.Id);
    if (hWnd != IntPtr.Zero)
    {
      // 원래 WndProc으로 복귀
      _ = NativeMethods.SetWindowLongPtr(hWnd, GWLP_WNDPROC, _oldWndProc);
    }
    _newWndProcCallback = null;

  }

  private bool _disposeStarted;
  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposeStarted, true))
    {
      return;
    }
    await DisposeAsyncCore();
    GC.SuppressFinalize(this);
  }

  public async ValueTask DisposeAsyncCore()
  {
    Bindings.StopTracking();

    _infoBarDismissTimer.Tick -= InfoBarDismissTimer_Tick;

    UnregisterMessengers();

    // 에디터 내용을 저장 후 정리
    if (EditorViewModelLease is not null)
    {
      await EditorViewModelLease.DisposeAsync();
    }

    if (ImageCollectionViewModelLease is not null)
    {
      await ImageCollectionViewModelLease.DisposeAsync();
    }
  }
  #endregion
}

partial class NotePage
{
  // 타이틀 바 드래그 영역 계산
  private void SetRegionsForCustomTitleBar()
  {
    if (this.XamlRoot is XamlRoot xamlRoot && xamlRoot.ContentIslandEnvironment.AppWindowId is Microsoft.UI.WindowId appWindowId)
    {
      double scaleFactor = xamlRoot.RasterizationScale;

      // 뒤로 가기 버튼, 메뉴 버튼, 검색 상자 영역 위치와 크기 계산
      var PinButtonPosition = NotePage_PinButton.TransformToVisual(null).TransformBounds(new Rect(0, 0, NotePage_PinButton.ActualWidth, NotePage_PinButton.ActualHeight));
      var MoreButtonPosition = NotePage_MoreButton.TransformToVisual(null).TransformBounds(new Rect(0, 0, NotePage_MoreButton.ActualWidth, NotePage_MoreButton.ActualHeight));
      var TitleRenameTextBoxPosition = NotePage_TitleRenameTextBox.Visibility == Visibility.Visible ? NotePage_TitleRenameTextBox.TransformToVisual(null).TransformBounds(new Rect(0, 0, NotePage_TitleRenameTextBox.ActualWidth, NotePage_TitleRenameTextBox.ActualHeight)) : new Rect(0, 0, 0, 0);
      var MinimizeButtonPosition = NotePage_MinimizeButton.TransformToVisual(null).TransformBounds(new Rect(0, 0, NotePage_MinimizeButton.ActualWidth, NotePage_MinimizeButton.ActualHeight));
      var CloseButtonPosition = NotePage_CloseButton.TransformToVisual(null).TransformBounds(new Rect(0, 0, NotePage_CloseButton.ActualWidth, NotePage_CloseButton.ActualHeight));

      RectInt32 PinButtonRect = PinButtonPosition.AsScaledRectInt32(scaleFactor);
      RectInt32 MoreButtonRect = MoreButtonPosition.AsScaledRectInt32(scaleFactor);
      RectInt32 TitleRenameTextBoxRect = TitleRenameTextBoxPosition.AsScaledRectInt32(scaleFactor);
      RectInt32 MinimizeButtonRect = MinimizeButtonPosition.AsScaledRectInt32(scaleFactor);
      RectInt32 CloseButtonRect = CloseButtonPosition.AsScaledRectInt32(scaleFactor);

      // 제목 표시줄 드래그 제외할 영역 설정
      var _inputNonClientPointerSource = InputNonClientPointerSource.GetForWindowId(appWindowId);
      _inputNonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, [PinButtonRect, MoreButtonRect, TitleRenameTextBoxRect, MinimizeButtonRect, CloseButtonRect]);
    }
  }

  private void ChangeFlyoutTheme(ElementTheme theme)
  {
    switch (theme)
    {
      case ElementTheme.Default:
        VisualStateManager.GoToState(this, nameof(FlyoutThemeDefault), false);
        break;
      case ElementTheme.Light:
        VisualStateManager.GoToState(this, nameof(FlyoutThemeLight), false);
        break;
      case ElementTheme.Dark:
        VisualStateManager.GoToState(this, nameof(FlyoutThemeDark), false);
        break;
    }
  }

  private IntPtr _oldWndProc = IntPtr.Zero;
  private IntPtr _newWndProc = IntPtr.Zero;
  private NativeMethods.WndProcCallback? _newWndProcCallback;
  private readonly int GWLP_WNDPROC = -4;
  private bool _isManualClose = true;

  private readonly DispatcherTimer _appWindowSizePotionUpdateTimer = new() { Interval = TimeSpan.FromSeconds(2) };
  private static bool AllowAppWindowSizePositionUpdate(AppWindow appWindow) => appWindow.Presenter is OverlappedPresenter presenter && presenter.State is OverlappedPresenterState.Restored && !NativeMethods.IsWindowArranged(Win32Interop.GetWindowFromWindowId(appWindow.Id));
  private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
  {
    if (args.DidSizeChange || args.DidPositionChange)
    {
      if (AllowAppWindowSizePositionUpdate(sender))
      {
        _appWindowSizePotionUpdateTimer.Start();
      }
      else
      {
        _appWindowSizePotionUpdateTimer.Stop();
      }
    }

    if (FocusManager.GetFocusedElement(this.XamlRoot) is FrameworkElement focusedElement
      && focusedElement == NotePage_TextEditorRichEditBox)
    {
      NotePage_TitleBarGrid.Focus(FocusState.Programmatic);
    }
    NoteViewModel.ImagePanelMaxHeight = Math.Min(this.ActualHeight * 0.5, 512 * this.XamlRoot.RasterizationScale);
  }

  private void AppWindowSizePotionUpdateTimer_Tick(object? sender, object e) => UpdateWindowSizeAndPosition();

  private void UpdateWindowSizeAndPosition()
  {
    _appWindowSizePotionUpdateTimer.Stop();

    var appWindow = AppWindow.GetFromWindowId(this.XamlRoot.ContentIslandEnvironment.AppWindowId);
    Note.Size = appWindow.Size;
    Note.Position = appWindow.Position;
  }
}

partial class NotePage
{
  #region 상단 타이틀 바 영역
  // 타이틀바 드래그 영역 조정(로드 및 크기 변경 시)
  private void NotePage_TitleBarGrid_Loaded(object sender, RoutedEventArgs e)
  {
    SetRegionsForCustomTitleBar();
  }

  private void NotePage_TitleBarGrid_SizeChanged(object sender, SizeChangedEventArgs e)
  {
    SetRegionsForCustomTitleBar();
  }

  private void NotePage_RenameTitleMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
  {
    if (VisualStateManager.GoToState(this, nameof(TitleBarTitleRename), false))
    {
      NotePage_TitleRenameTextBox.Focus(FocusState.Keyboard);
      NotePage_TitleRenameTextBox.SelectAll();
      NotePage_TitleRenameTextBox.LayoutUpdated += NotePage_TitleRenameTextBox_LayoutUpdated;
    }
  }

  private void NotePage_TitleRenameTextBox_LayoutUpdated(object? sender, object e)
  {
    NotePage_TitleRenameTextBox.LayoutUpdated -= NotePage_TitleRenameTextBox_LayoutUpdated;
    SetRegionsForCustomTitleBar();
  }

  private void NotePage_TitleRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
  {
    NoteViewModel.OldTitle = Note.Title;

    if (VisualStateManager.GoToState(this, nameof(TitleBarTitleNormal), false))
    {
      NotePage_TitleRenameTextBox.LayoutUpdated += NotePage_TitleRenameTextBox_LayoutUpdated;
    }
  }
  #endregion

  #region 에디터 영역
  private async void NotePage_TextEditorRichEditBox_Paste(object sender, TextControlPasteEventArgs e)
  {
    e.Handled = true;
    var clipboardDataPackageView = Clipboard.GetContent();
    var availableFormats = clipboardDataPackageView.AvailableFormats;
    var selection = NotePage_TextEditorRichEditBox.Document.Selection;

    if (availableFormats.Contains(StandardDataFormats.Rtf))
    {
      string rtfText = await clipboardDataPackageView.GetRtfAsync();
      selection.SetText(TextSetOptions.FormatRtf, rtfText);
      int position = selection.GetIndex(TextRangeUnit.Character) + rtfText.Length - 1;
      selection.SetRange(position, position);
    }
    else if (availableFormats.Contains(StandardDataFormats.Text))
    {
      string plainText = await clipboardDataPackageView.GetTextAsync();
      selection.SetText(TextSetOptions.None, plainText);
      int position = selection.GetIndex(TextRangeUnit.Character) + plainText.Length - 1;
      selection.SetRange(position, position);
    }
  }
  #endregion

  #region InfoBar 영역
  private readonly DispatcherTimer _infoBarDismissTimer = new() { Interval = TimeSpan.FromSeconds(2) };

  private void OpenInfoBar(TimeSpan? interval = null, bool showCloseButtonOnAutoClose = false, Action? actionAfterAutoClosed = null)
  {
    NotePage_InfoBar.IsOpen = true;

    if (interval is null)
    {
      NotePage_InfoBar.IsClosable = true;
    }
    else
    {
      NotePage_InfoBar.IsClosable = showCloseButtonOnAutoClose;
      _infoBarDismissTimer.Stop();
      _infoBarDismissTimer.Interval = interval.Value;
      void InfoBarDismissTimer_Tick_WhenAutoClosed(object? sender, object e)
      {
        _infoBarDismissTimer.Tick -= InfoBarDismissTimer_Tick_WhenAutoClosed;
        actionAfterAutoClosed.Invoke();
      }
      if (actionAfterAutoClosed is not null)
      {
        _infoBarDismissTimer.Tick += InfoBarDismissTimer_Tick_WhenAutoClosed;
      }

      _infoBarDismissTimer.Start();
    }
  }

  private void InfoBarDismissTimer_Tick(object? sender, object e)
  {
    NotePage_InfoBar.IsOpen = false;
    _infoBarDismissTimer.Stop();
  }
  #endregion
}

#region 컨트롤 및 키보드 단축키 동작 이벤트 핸들러
partial class NotePage
{
  private async void NotePage_SaveAsMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
  {
    if (sender is MenuFlyoutItem item && item.XamlRoot.ContentIslandEnvironment.AppWindowId is Microsoft.UI.WindowId appWindowId)
    {
      (string Extension, string Kind)? fileType = item.Tag switch
      {
        string tag when tag is "SaveAsPlainText" => (".txt", "text"),
        string tag when tag is "SaveAsRichText" => (".rtf", "rich text"),
        string tag when tag is "SaveAsPDF" => (".pdf", "PDF"),
        _ => null
      };

      if (fileType is not null)
      {
        string suggestedFileName = Note.Title;
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
          suggestedFileName = suggestedFileName.Replace(ch, '_');
        }
        if (string.IsNullOrEmpty(suggestedFileName))
        {
          suggestedFileName = $"MyNote_{DateTime.UtcNow:yyyyMMdd_hhmmss}";
        }

        FileSavePicker picker = new(appWindowId)
        {
          SuggestedFileName = suggestedFileName,
          SuggestedStartLocation = PickerLocationId.Desktop
        };
        picker.FileTypeChoices.Add(item.Text, [fileType.Value.Extension]);
        picker.DefaultFileExtension = fileType.Value.Extension;

        if (await picker.PickSaveFileAsync() is PickFileResult result)
        {
          string savePath = result.Path;
          switch (fileType.Value.Extension)
          {
            case string ex when ex is ".txt":
              NotePage_TextEditorRichEditBox.Document.GetText(TextGetOptions.None | TextGetOptions.IncludeNumbering, out var plainText);
              await File.WriteAllTextAsync(savePath, plainText);
              break;
            case string ex when ex is ".rtf":
              StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(result.Path));
              var rtfFile = await folder.CreateFileAsync(Path.GetFileName(savePath), CreationCollisionOption.ReplaceExisting);
              using (IRandomAccessStream randAccStream = await rtfFile.OpenAsync(FileAccessMode.ReadWrite))
              {
                NotePage_TextEditorRichEditBox.Document.SaveToStream(TextGetOptions.FormatRtf, randAccStream);
              }
              break;
          }
          Button actionButton = new()
          {
            Content = "Show in folder"
          };
          actionButton.Click += SaveAsInfoBarActionButton_Click;
          NotePage_InfoBar.Title = $"Saved as a {fileType.Value.Kind} file.";
          NotePage_InfoBar.ActionButton = actionButton;
          NotePage_InfoBar.Severity = InfoBarSeverity.Success;
          OpenInfoBar(interval: TimeSpan.FromSeconds(7), showCloseButtonOnAutoClose: true,
            actionAfterAutoClosed: () =>
            {
              actionButton.Click -= SaveAsInfoBarActionButton_Click;
              NotePage_InfoBar.ActionButton = null;
            });

          async void SaveAsInfoBarActionButton_Click(object sender, RoutedEventArgs e)
          {
            actionButton.Click -= SaveAsInfoBarActionButton_Click;
            NotePage_InfoBar.ActionButton = null;
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(savePath));
            await Launcher.LaunchFolderAsync(folder);
            NotePage_InfoBar.IsOpen = false;
          }
        }
      }
    }
  }

  private async void NotePage_SaveKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
  {
    args.Handled = true;
    if (EditorViewModel is not null)
    {
      bool success = await EditorViewModel.UpdateNoteBodyAsync();
      NotePage_InfoBar.Title = success ? LocalizedStrings.SavedMessage : LocalizedStrings.FailedMessage;
      NotePage_InfoBar.ActionButton = null;
      NotePage_InfoBar.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
      OpenInfoBar(TimeSpan.FromSeconds(2));
    }
  }

  private void NotePage_FindKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => VisualStateManager.GoToState(this, NotePage_FindReplaceBox.IsOpen ? nameof(EditorSearchNone) : nameof(EditorSearching), false);

  private void NotePage_ReplaceKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
      VisualStateManager.GoToState(this, NotePage_FindReplaceBox.IsOpen ? nameof(EditorSearchNone) : nameof(EditorSearching), false);
  private void NotePage_RenameTitleKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
  {
    VisualStateManager.GoToState(this, nameof(TitleBarTitleRename), false);
    NotePage_TitleRenameTextBox.Focus(FocusState.Keyboard);
    NotePage_TitleRenameTextBox.LayoutUpdated += NotePage_TitleRenameTextBox_LayoutUpdated;
  }
}
#endregion

#region 이미지 패널 항목 드래그 앤드 드롭
partial class NotePage
{
  int _sourceIndex = -1;
  private void NotePage_ImagesGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
  {
    if (e.Items.FirstOrDefault() is ImageViewModel sourceItem)
    {
      _sourceIndex = NotePage_ImagesGridView.Items.IndexOf(sourceItem);
    }
  }

  private async void NotePage_ImagesGridView_Drop(object sender, DragEventArgs e)
  {
    var pointerPosition = e.GetPosition(NotePage_ImagesGridView);
    int dropIndex = -1;
    int count = NotePage_ImagesGridView.Items.Count;
    for (int index = 0; index < count; index++)
    {
      if (NotePage_ImagesGridView.ContainerFromIndex(index) is not GridViewItem container)
      {
        continue;
      }

      var containerBounds = container.TransformToVisual(NotePage_ImagesGridView).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));

      if (containerBounds.Contains(pointerPosition))
      {
        dropIndex = index;
        break;
      }
    }

    if (_sourceIndex != dropIndex && _sourceIndex >= 0 && _sourceIndex < count && dropIndex >= 0 && dropIndex < count)
    {
      if (ImageCollectionViewModel is not null)
      {
        await ImageCollectionViewModel.MoveImageAsync(_sourceIndex, dropIndex);
      }
    }

    _sourceIndex = -1;
  }

  private void NotePage_ImagesGridView_DragOver(object sender, DragEventArgs e)
  {
    e.AcceptedOperation = DataPackageOperation.Move;
  }
}
#endregion

#region 메신저 및 커맨드
partial class NotePage
{
  private void RegisterMessengers()
  {
    WeakReferenceMessenger.Default.Register<NotePage, AppThemeChangedMessage>(this, static (recipient, message) => recipient.ChangeFlyoutTheme(message.Value));

    WeakReferenceMessenger.Default.Register<NotePage, NoteWindowActivationChangedMessage, MessageToken<NoteId>>(this, MessageToken<NoteId>.Create(Note.Id), static (recipient, message) =>
    {
      WindowPresenterState state = message.Value;
      WindowActivationState windowState = state.WindowActivationState;
      OverlappedPresenterState presenterState = state.OverlappedPresenterState;

      recipient.NotePage_TitleBarGrid.Focus(FocusState.Programmatic);
      if (windowState is WindowActivationState.Deactivated)
      {
        if (presenterState is OverlappedPresenterState.Maximized)
        {
          VisualStateManager.GoToState(recipient, nameof(WindowDeactivatedMaximized), false);
        }
        else
        {
          VisualStateManager.GoToState(recipient, nameof(WindowDeactivated), false);
        }
      }
      else
      {
        VisualStateManager.GoToState(recipient, nameof(WindowActivated), false);
      }
    });
  }

  private void UnregisterMessengers()
  {
    WeakReferenceMessenger.Default.UnregisterAll(this);
  }
}
#endregion