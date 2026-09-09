using System.IO.Pipes;

using Microsoft.Extensions.DependencyInjection;

using MyNotes.Application.Contracts.Notes.Models;
using MyNotes.Application.Notes.Services;
using MyNotes.Application.Settings.Services;
using MyNotes.Constants;
using MyNotes.Debugging;
using MyNotes.Domain.Navigations;
using MyNotes.Infrastructure.Database.Core;
using MyNotes.Infrastructure.Logging;
using MyNotes.Infrastructure.Search.Core;
using MyNotes.Models;
using MyNotes.Models.Notes;
using MyNotes.Services;
using MyNotes.Services.Commands;
using MyNotes.Services.Navigations;
using MyNotes.Services.Windows;

namespace MyNotes;

public sealed partial class App : Microsoft.UI.Xaml.Application, IAsyncDisposable
{
  internal static App Instance => (App)Current;

  #region Object Lifetime Management
  internal App()
  {
    InitializeComponent();

    this.UnhandledException += App_UnhandledException;
    AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

    _ = InitializeServicesAsync();
  }

  private async Task InitializeServicesAsync()
  {
    using var appInitializeScope = Services.CreateScope();
    _ = appInitializeScope.ServiceProvider.GetRequiredService<AppDbContextInitializer>();
    _ = appInitializeScope.ServiceProvider.GetRequiredService<AppSearchContext>();

    var navigationController = appInitializeScope.ServiceProvider.GetRequiredService<NavigationController>();
    await navigationController.InitializationTask;

    var noteService = appInitializeScope.ServiceProvider.GetRequiredService<NoteService>();
    await noteService.InitializationTask;

    InitializationTCS.TrySetResult();
  }

  private readonly TaskCompletionSource InitializationTCS = new();
  public Task InitializationTask => InitializationTCS.Task;

  protected override async void OnLaunched(LaunchActivatedEventArgs args)
  {
    await InitializationTask;

    _ = LaunchArgumentsPipeServerStreamAsync();
    var mainWindowService = Services.GetRequiredService<MainWindowService>();
    var appSettingsService = Services.GetRequiredService<AppSettingsService>();

    AppActivationArguments appActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
    switch (appActivationArguments.Kind)
    {
      case ExtendedActivationKind.Launch or ExtendedActivationKind.StartupTask:
        var noteWindowService = Services.GetRequiredService<NoteWindowService>();
        var noteService = Services.GetRequiredService<NoteService>();
        var noteModelFactory = Services.GetRequiredService<IModelFactory<NoteDto, NoteModel>>();
        var notes = (await noteService.Retrieval.GetOpenNotesAsync()).Select(noteModelFactory.Create).ToList();

        if (notes.Count == 0 || appSettingsService.Load(AppSettingsDescriptors.IsMainWindowOpen))
        {
          var mainWindow = await mainWindowService.GetOrCreate();
          mainWindow.Activate();
        }

        foreach (var note in notes)
        {
          await noteWindowService.OpenNoteWindow(note);
        }
        break;
      case ExtendedActivationKind.File:
        break;
      case ExtendedActivationKind.Protocol:
        break;
      default:
        break;
    }
  }

  private bool _disposeStarted;

  private async ValueTask DisposeAsyncCore()
  {
    this.UnhandledException -= App_UnhandledException;
    AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
    TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;

    await Services.DisposeAsync();
  }

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposeStarted, true))
    {
      return;
    }

    await DisposeAsyncCore().ConfigureAwait(false);
    GC.SuppressFinalize(this);
  }
  #endregion

  internal static ServiceProvider Services { get; } = ConfigureServices();

  private static ServiceProvider ConfigureServices()
  {
    ServiceCollection serviceCollection = new();
    serviceCollection.ConfigureServices();
    serviceCollection.MakeReadOnly();
    return serviceCollection.BuildServiceProvider();
  }

  private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) => WriteExceptionLog(e.Exception);

  private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
  {
    if (e.ExceptionObject is Exception ex)
    {
      WriteExceptionLog(ex);
    }
  }

  private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) => WriteExceptionLog(e.Exception);

  private void WriteExceptionLog(Exception ex)
  {
    ConsoleHelper.WriteLine(true, "{0}: {1}", "Exception", ex);
    var loggingService = Services.GetRequiredService<AppLogger>();
    loggingService.Write(ex);
  }

  public void OpenDebugWindow() => new Views.Windows.DebugWindow().Activate();

  private async Task LaunchArgumentsPipeServerStreamAsync()
  {
    while (!_disposeStarted)
    {
      using NamedPipeServerStream pipeServerStream = new(AppStrings.NamedPipe_LaunchArguments, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

      await pipeServerStream.WaitForConnectionAsync();

      using StreamReader sr = new(pipeServerStream);

      var mainWindowService = Services.GetRequiredService<MainWindowService>();

      string? arg;
      while ((arg = sr.ReadLine()?.Trim()) is not null)
      {
        ConsoleHelper.WriteLine(true, "{0}: {1}", "arg", arg);
        switch (arg)
        {
          case AppStrings.LaunchArgument_JumpList_MainWindow:
            (await mainWindowService.GetOrCreate(NavigationId.Home)).Activate();
            break;
          case AppStrings.LaunchArgument_JumpList_NewNote:
            if (Services.GetRequiredKeyedService<ICommandService>(CommandServiceType.Note) is NoteCommandService noteCommandService)
            {
              await noteCommandService.CreateNewNoteAsync(null);
            }
            break;
          case AppStrings.LaunchArgument_JumpList_Settings:
            (await mainWindowService.GetOrCreate(NavigationId.Settings)).Activate();
            break;
        }
      }
    }
  }
}
