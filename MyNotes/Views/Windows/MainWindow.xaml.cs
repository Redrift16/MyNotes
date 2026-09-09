using Microsoft.Extensions.DependencyInjection;

using MyNotes.Application.Settings.Services;
using MyNotes.Common.Converters.Codecs;
using MyNotes.Common.Interop;
using MyNotes.Constants;
using MyNotes.Domain.Navigations;
using MyNotes.Strings;
using MyNotes.Views.Navigations;

namespace MyNotes.Views.Windows;

[Debugging.Attributes.ReferenceTracker]
internal sealed partial class MainWindow : Window
{
  // ServiceProvider(DI)로 주입받은 뷰모델/서비스 필드
  private readonly AppSettingsService AppSettingsService;

  // 창 핸들 및 AppWindow Presenter 필드
  private readonly IntPtr _hWnd;

  private readonly TaskCompletionSource LoadTCS = new();
  public Task LoadTask => LoadTCS.Task;

  #region Object Lifetime Management
  public MainWindow(NavigationId? _initialNavigationId = null)
  {
    TrackReference();
    InitializeComponent();
    AppSettingsService = App.Services.GetRequiredService<AppSettingsService>();

    // 타이틀 및 아이콘 설정
    this.ExtendsContentIntoTitleBar = true;
    AppWindow.Title = LocalizedStrings.MainWindowTitle;
    AppWindow.SetIcon(AppStrings.AppIconPath);
    AppWindow.SetTaskbarIcon(AppStrings.AppIconPath);

    // DPI 스케일 가져오기
    _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    double scaleFactor = NativeMethods.GetWindowScaleFactor(_hWnd);

    // 창 최소 크기 지정
    var minimumWindowSize = AppSettingsDescriptors.MainWindowMinimumSize;
    var presenter = AppWindow.Presenter as OverlappedPresenter;
    presenter?.PreferredMinimumWidth = (int)(minimumWindowSize.Width * scaleFactor);
    presenter?.PreferredMinimumHeight = (int)(minimumWindowSize.Height * scaleFactor);

    // 높은(48epx) 캡션 컨트롤 지원
    AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

    // 창 활성화 및 크기 변경 시
    this.Activated += MainWindow_Activated;

    // 창 종료 시 (AppWindow는 hWnd 기준, Window는 XAML 기준)
    // ├─ AppWindow.Closing          // 취소 가능, UI 상태 신뢰 가능
    // ├─ XAML Window.Closed         // 시각 요소/논리 요소 정리
    // └─ Win32 DestroyWindow        // 내부 Win32 창 파괴
    //    └─ AppWindow.Destroying    // 창이 제거된 직후
    AppWindow.Closing += AppWindow_Closing;
    this.Closed += MainWindow_Closed;

    // 창 초기 크기 지정
    var windowSize = AppSettingsService.Load(SizeInt32SettingsCodec.Default, AppSettingsDescriptors.MainWindowSize);
    if (windowSize.Width < minimumWindowSize.Width && windowSize.Height < minimumWindowSize.Height)
    {
      windowSize = AppSettingsDescriptors.MainWindowSize.DefaultValue;
    }

    AppWindow.Resize(new((int)(windowSize.Width * scaleFactor), (int)(windowSize.Height * scaleFactor)));

    // 창 초기 위치 지정
    var windowPosition = AppSettingsService.Load(PointInt32SettingsCodec.Default, AppSettingsDescriptors.MainWindowPosition);
    List<RectInt32> areas = new();
    foreach (var monitor in NativeMethods.GetActiveMonitorsInfo())
    {
      areas.Add(new()
      {
        X = monitor.rcWork.Left - AppSettingsDescriptors.WindowBorderMargin.DefaultValue,
        Y = monitor.rcWork.Top - AppSettingsDescriptors.WindowBorderMargin.DefaultValue,
        Width = monitor.rcWork.Right,
        Height = monitor.rcWork.Bottom,
      });
    }

    if (ContainsPointInAreas(areas, windowPosition))
    {
      AppWindow.Move(windowPosition);
    }

    _appWindowSizePotionUpdateTimer.Tick += AppWindowSizePositionUpdateTimer_Tick;
    AppWindow.Changed += AppWindow_Changed;

    // 제목 표시줄 테마 설정
    var theme = AppSettingsService.Load(ElementThemeSettingsCodec.Default, AppSettingsDescriptors.AppTheme);
    AppWindow.TitleBar.PreferredTheme = theme switch
    {
      ElementTheme.Light => TitleBarTheme.Light,
      ElementTheme.Dark => TitleBarTheme.Dark,
      _ => TitleBarTheme.UseDefaultAppMode
    };

    // MainWindow 시작 플래그
    AppSettingsService.Save(AppSettingsDescriptors.IsMainWindowOpen, true);

    MainPage contentPage = new(_initialNavigationId);
    this.Content = contentPage;
    this.SetTitleBar(contentPage.TitleBarElement);

    LoadTCS.TrySetResult();
  }

  private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
  {
    // MainWindow 종료 플래그
    AppSettingsService.Save(AppSettingsDescriptors.IsMainWindowOpen, false);
  }

  public bool IsClosed { get; private set; } = false;

  private void MainWindow_Closed(object sender, WindowEventArgs args)
  {
    IsClosed = true;
    _appWindowSizePotionUpdateTimer.Tick -= AppWindowSizePositionUpdateTimer_Tick;
    if (AllowAppWindowSizePositionUpdate)
    {
      UpdateWindowSizeAndPosition();
    }

    AppWindow.Changed -= AppWindow_Changed;
    this.Activated -= MainWindow_Activated;
    AppWindow.Closing -= AppWindow_Closing;
    this.Closed -= MainWindow_Closed;
  }
  #endregion

  public void SetNavigation(NavigationId? navigationId) => (this.Content as MainPage)?.SetNavigation(navigationId);

  #region 크기(dpi-awareness) 및 위치(per-monitor)
  private readonly DispatcherTimer _appWindowSizePotionUpdateTimer = new() { Interval = TimeSpan.FromSeconds(2) };
  private bool AllowAppWindowSizePositionUpdate => AppWindow.Presenter is OverlappedPresenter presenter && presenter.State is OverlappedPresenterState.Restored && !NativeMethods.IsWindowArranged(_hWnd);
  private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
  {
    if (args.DidSizeChange || args.DidPositionChange)
    {
      if (AllowAppWindowSizePositionUpdate)
      {
        _appWindowSizePotionUpdateTimer.Start();
      }
      else
      {
        _appWindowSizePotionUpdateTimer.Stop();
      }
    }
  }

  private void AppWindowSizePositionUpdateTimer_Tick(object? sender, object e) => UpdateWindowSizeAndPosition();

  private void UpdateWindowSizeAndPosition()
  {
    _appWindowSizePotionUpdateTimer.Stop();
    var windowSize = AppWindow.Size;
    var windowPosition = AppWindow.Position;

    // 창 크기 저장
    double scaleFactor = NativeMethods.GetWindowScaleFactor(_hWnd);
    AppSettingsService.Save(SizeInt32SettingsCodec.Default, AppSettingsDescriptors.MainWindowSize, new SizeInt32((int)(windowSize.Width / scaleFactor), (int)(windowSize.Height / scaleFactor)));

    // 창 위치 및 디스플레이 저장
    AppSettingsService.Save(PointInt32SettingsCodec.Default, AppSettingsDescriptors.MainWindowPosition, windowPosition);
    AppSettingsService.Save(AppSettingsDescriptors.MainWindowDisplay, NativeMethods.GetMonitorInfoForWindow(_hWnd)?.szDevice ?? string.Empty);
  }

  public static bool ContainsPointInAreas(List<RectInt32> areas, PointInt32 point)
  {
    foreach (var rect in areas)
    {
      if (rect.X <= point.X && rect.Y <= point.Y && point.X < rect.Width && point.Y < rect.Height)
      {
        return true;
      }
    }

    return false;
  }
  #endregion

  private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
  {
    (this.Content as MainPage)?.SetRegionsForCustomTitleBarOnActivationState(args.WindowActivationState);
  }
}