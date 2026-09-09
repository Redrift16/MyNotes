using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

using MyNotes.Application.Settings.Services;
using MyNotes.Common.Commands;
using MyNotes.Constants;
using MyNotes.Domain.Navigations;
using MyNotes.Models.Navigations;
using MyNotes.Models.Navigations.Core;
using MyNotes.Models.Navigations.User;
using MyNotes.Services.Commands;
using MyNotes.Services.Navigations;
using MyNotes.ViewModels.Navigations;
using MyNotes.ViewModels.Navigations.Items;
using MyNotes.ViewModels.Navigations.Items.Providers;

namespace MyNotes.ViewModels;

internal sealed partial class MainViewModel : ViewModelBase
{
  private readonly NavigationController NavigationController;
  private readonly NavigationViewModelProvider NavigationViewModelProvider;
  private readonly NavigationCommandService NavigationCommandService;
  private readonly AppSettingsService AppSettingsService;

  #region Object Lifetime Management
  public MainViewModel(NavigationController navigationController, NavigationViewModelProvider navigationViewModelProvider, [FromKeyedServices(CommandServiceType.Navigation)] ICommandService navigationCommandService, AppSettingsService appSettingsService)
  {
    // DI
    NavigationController = navigationController;
    NavigationViewModelProvider = navigationViewModelProvider;
    NavigationCommandService = (NavigationCommandService)navigationCommandService;
    AppSettingsService = appSettingsService;

    _headerMenuItemLeases = new(NavigationController.PrimaryCoreNavigations.Select(NavigationViewModelProvider.Resolve));
    _userRootNavigationViewModelLease = NavigationViewModelProvider.Resolve(NavigationController.UserRootNavigation);
    _footerMenuItemLeases = new(NavigationController.SecondaryCoreNavigations.Select(NavigationViewModelProvider.Resolve));
    MenuItems = new([.. HeaderMenuItems, UserRootNavigationViewModel]);

    NavigationController.CurrentNavigationChanged += NavigationController_CurrentNavigationChanged;

    _isNavigationPaneOpen = AppSettingsService.Load(AppSettingsDescriptors.IsMainNavigationPaneOpen);

    SetCommands();
  }

  protected override void Dispose(bool disposing)
  {
    if (Disposed)
    {
      return;
    }

    if (disposing)
    {
      NavigationController.CurrentNavigationChanged -= NavigationController_CurrentNavigationChanged;
      NavigationController.ResetNavigation();
      _headerMenuItemLeases.Dispose();
      _userRootNavigationViewModelLease.Dispose();
      _footerMenuItemLeases.Dispose();
    }

    base.Dispose(disposing);
  }
  #endregion
}

// Navigation
partial class MainViewModel : ViewModelBase
{
  // Header
  private readonly LeasedNavigationViewModelCollection _headerMenuItemLeases;
  public IReadOnlyList<NavigationViewModelBase> HeaderMenuItems => _headerMenuItemLeases.ViewModels;

  // User
  public readonly IViewModelLease<NavigationViewModelBase> _userRootNavigationViewModelLease;
  public UserRootGroupNavigationViewModel UserRootNavigationViewModel => (UserRootGroupNavigationViewModel)_userRootNavigationViewModelLease.ViewModel;

  // Footer
  private readonly LeasedNavigationViewModelCollection _footerMenuItemLeases;
  public IReadOnlyList<NavigationViewModelBase> FooterMenuItems => _footerMenuItemLeases.ViewModels;

  public ReadOnlyObservableCollection<NavigationViewModelBase> MenuItems { get; }

  [ObservableProperty]
  public partial NavigationViewModelBase? SelectedNavigationViewModel { get; set; }
  partial void OnSelectedNavigationViewModelChanged(NavigationViewModelBase? value)
  {
    if (value is not null)
    {
      NavigationController.NavigateTo(value.Navigation);
    }
  }

  public event TypedEventHandler<object, INavigation?>? CurrentNavigationChanged
  {
    add => NavigationController.CurrentNavigationChanged += value;
    remove => NavigationController.CurrentNavigationChanged -= value;
  }

  private void NavigationController_CurrentNavigationChanged(object sender, INavigation? args)
  {
    SyncNavigation();
    CanNavigateBack = NavigationController.NavigationBackStack.Count > 0;
  }

  public void NavigateTo(INavigation navigation)
  {
    AddListCommand.NotifyCanExecuteChanged();
    AddGroupCommand.NotifyCanExecuteChanged();
    NavigationController.NavigateTo(navigation);
  }

  public bool NavigateTo(NavigationId navigationId)
  {
    NavigationViewModelBase? viewmodel =
      HeaderMenuItems.FirstOrDefault(vm => vm.Navigation is NavigationCoreNode coreNode && coreNode.Id == navigationId)
      ?? FooterMenuItems.FirstOrDefault(vm => vm.Navigation is NavigationCoreNode coreNode && coreNode.Id == navigationId)
      ?? UserRootNavigationViewModel.FirstDescendantOrDefault(vm => vm.Navigation is NavigationUserNode userNode && userNode.Id == navigationId, false)
      ?? (HeaderMenuItems.Count > 0 ? HeaderMenuItems[0] : null);

    if (viewmodel is not null)
    {
      NavigateTo(viewmodel.Navigation);
      return true;
    }

    return false;
  }

  public void NavigateBack() => NavigationController.NavigateBack();

  public async Task MoveNavigationAsync(NavigationUserNode sourceNavigation, NavigationUserNode targetNavigation)
  {
    await NavigationCommandService.MoveToAsync(sourceNavigation, targetNavigation);
    SyncNavigation();
  }

  public void SyncNavigation()
  {
    if (SelectedNavigationViewModel?.Navigation != NavigationController.CurrentNavigation)
    {
      switch (NavigationController.CurrentNavigation)
      {
        case INavigationNode node:
          using (var lease = NavigationViewModelProvider.Acquire(node))
          {
            if (lease is not null)
            {
              SelectedNavigationViewModel = lease.ViewModel;
            }
          }
          break;
        default:
          SelectedNavigationViewModel = null;
          break;
      }
    }
  }

  [ObservableProperty]
  public partial bool CanNavigateBack { get; private set; } = false;

  private bool _isNavigationPaneOpen;
  public bool IsNavigationPaneOpen
  {
    get => _isNavigationPaneOpen;
    set
    {
      if (SetProperty(ref _isNavigationPaneOpen, value))
      {
        AppSettingsService.Save(AppSettingsDescriptors.IsMainNavigationPaneOpen, _isNavigationPaneOpen);
      }
    }
  }
}

partial class MainViewModel : ViewModelBase
{
  public AsyncCommand<NavigationUserNode> AddListCommand { get; private set; }

  public AsyncCommand<NavigationUserNode> AddGroupCommand { get; private set; }

  public Command ToggleNavigationPaneCommand { get; private set; }

  public Command<string> SearchNoteCommand { get; private set; }

  [MemberNotNull(nameof(AddListCommand), nameof(AddGroupCommand), nameof(ToggleNavigationPaneCommand), nameof(SearchNoteCommand))]
  private void SetCommands()
  {
    AddListCommand = new()
    {
      ExecuteFunc = (targetNavigation) => NavigationCommandService.AddNavigationAsync(targetNavigation, false)
    };

    AddGroupCommand = new()
    {
      ExecuteFunc = (targetNavigation) => NavigationCommandService.AddNavigationAsync(targetNavigation, true)
    };

    ToggleNavigationPaneCommand = new()
    {
      ExecuteAction = () => IsNavigationPaneOpen = !IsNavigationPaneOpen
    };

    SearchNoteCommand = new()
    {
      ExecuteAction = async (searchText) =>
      {
        if (string.IsNullOrEmpty(searchText))
        {
          return;
        }

        NavigationSearch navigationSearch = new()
        {
          SearchText = searchText,
          Title = $"Search Results for {searchText}"
        };

        NavigateTo(navigationSearch);
      }
    };
  }
}