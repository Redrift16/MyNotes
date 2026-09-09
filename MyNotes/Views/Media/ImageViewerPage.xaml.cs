using Microsoft.Extensions.DependencyInjection;

using MyNotes.Application.Settings.Services;
using MyNotes.Constants;
using MyNotes.Models.Media;
using MyNotes.ViewModels;
using MyNotes.ViewModels.Media;
using MyNotes.ViewModels.Media.Providers;

namespace MyNotes.Views.Media;

[Debugging.Attributes.ReferenceTracker]
internal sealed partial class ImageViewerPage : Page, ITitleBarProvider, IAsyncDisposable
{
  private readonly AppSettingsService AppSettingsService;

  public UIElement TitleBarElement { get; }

  private readonly IAsyncViewModelLease<ImageCollectionViewModel> ViewModelLease;
  private ImageCollectionViewModel ViewModel => ViewModelLease.ViewModel;

  public static readonly DependencyProperty SelectedImageProperty = DependencyProperty.Register("SelectedImage", typeof(ImageViewModel), typeof(ImageViewerPage), new PropertyMetadata(null));
  public ImageViewModel? SelectedImage
  {
    get => (ImageViewModel?)GetValue(SelectedImageProperty);
    set => SetValue(SelectedImageProperty, value);
  }

  public static readonly DependencyProperty IsAlwaysOnTopProperty = DependencyProperty.Register("IsAlwaysOnTop", typeof(bool), typeof(ImageViewerPage), new PropertyMetadata(false, OnIsAlwaysOnTopChanged));
  public bool IsAlwaysOnTop
  {
    get => (bool)GetValue(IsAlwaysOnTopProperty);
    set => SetValue(IsAlwaysOnTopProperty, value);
  }

  public static void OnIsAlwaysOnTopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
  {
    if (d is ImageViewerPage control && e.NewValue is bool newValue)
    {
      var appWindow = AppWindow.GetFromWindowId(control.XamlRoot.ContentIslandEnvironment.AppWindowId);
      if (appWindow.Presenter is OverlappedPresenter presenter)
      {
        presenter.IsAlwaysOnTop = newValue;
      }
    }
  }

  public static readonly DependencyProperty IsFilmstripVisibleProperty = DependencyProperty.Register("IsFilmstripVisible", typeof(bool), typeof(ImageViewerPage), new PropertyMetadata(true, OnIsFilmstripVisibleChanged));
  public bool IsFilmstripVisible
  {
    get => (bool)GetValue(IsFilmstripVisibleProperty);
    set => SetValue(IsFilmstripVisibleProperty, value);
  }

  public static void OnIsFilmstripVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
  {
    if (d is ImageViewerPage control && e.NewValue is bool newValue)
    {
      control.AppSettingsService.Save(AppSettingsDescriptors.ShowImageViewerFilmstrip, newValue);
    }
  }

  public async static Task<ImageViewerPage> CreateAsync(ImageCollectionKey collectionKey)
  {
    var viewModelProvider = App.Services.GetRequiredService<ImageCollectionViewModelProvider>();
    var viewModelLease = await viewModelProvider.ResolveAsync(collectionKey);
    return new ImageViewerPage(viewModelLease);
  }

  private ImageViewerPage(IAsyncViewModelLease<ImageCollectionViewModel> viewModelLease)
  {
    TrackReference();
    InitializeComponent();

    ViewModelLease = viewModelLease;
    AppSettingsService = App.Services.GetRequiredService<AppSettingsService>();
    IsFilmstripVisible = AppSettingsService.Load(AppSettingsDescriptors.ShowImageViewerFilmstrip);

    TitleBarElement = ImageViewerPage_TitleBar;
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

    if (ViewModelLease is not null)
    {
      await ViewModelLease.DisposeAsync();
    }
  }

  public async Task ChangeImageSelection(ImageDescriptor imageDescriptor)
  {
    await ViewModel.InitializationTask;
    SelectedImage = ViewModel.ImageViewModels.FirstOrDefault(vm => vm.ImageDescriptor == imageDescriptor);
  }
}
