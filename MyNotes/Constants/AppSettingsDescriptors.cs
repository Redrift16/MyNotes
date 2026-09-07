using MyNotes.Common.Structures;
using MyNotes.Domain.Navigations;
using MyNotes.Models.Navigations.Preferences;
using MyNotes.Models.UI;

namespace MyNotes.Constants;

internal static class AppSettingsDescriptors
{
  // Settings - Appearance
  public static SettingsDescriptor<ElementTheme> AppTheme { get; } = new()
  {
    Key = "AppTheme",
    DefaultValue = ElementTheme.Default
  };
  public static SettingsDescriptor<AppLanguage> AppLanguage { get; } = new()
  {
    Key = "AppLanguage",
    DefaultValue = new AppLanguage()
  };

  // Settings - General
  public static SettingsDescriptor<InitialPageType> InitialPageType { get; } = new()
  {
    Key = "InitialPageType",
    DefaultValue = Models.Navigations.Preferences.InitialPageType.Home
  };
  public static SettingsDescriptor<Guid> InitialPageId { get; } = new()
  {
    Key = "InitialPageId",
    DefaultValue = NavigationGuids.HomeId
  };
  public static SettingsDescriptor<bool> ConfirmBeforeDeleting { get; } = new()
  {
    Key = "ConfirmBeforeDeleting",
    DefaultValue = true
  };

  // Settings - Note
  public static SettingsDescriptor<SizeInt32> DefaultNoteSize { get; } = new()
  {
    Key = "DefaultNoteSize",
    DefaultValue = new SizeInt32(500, 500)
  };

  public static SettingsDescriptor<bool> DeleteEmptyNote { get; } = new()
  {
    Key = "DeleteEmptyNote",
    DefaultValue = true
  };

  // Settings - List and Group
  public static SettingsDescriptor<bool> ShowNoteCount { get; } = new()
  {
    Key = "ShowNoteCount",
    DefaultValue = true
  };
  public static SettingsDescriptor<GroupIconBadge> GroupIconBadge { get; } = new()
  {
    Key = "GroupIconBadge",
    DefaultValue = Models.Navigations.Preferences.GroupIconBadge.Folder
  };

  public static SettingsDescriptor<bool> AllowCustomNoteSortOrder { get; } = new()
  {
    Key = "AllowCustomSortOrder",
    DefaultValue = true
  };

  public static SettingsDescriptor<bool> AllowCustomPreviewLayout { get; } = new()
  {
    Key = "AllowCustomPreviewLayout",
    DefaultValue = true
  };

  // Windows
  public static SettingsDescriptor<bool> IsMainWindowOpen { get; } = new()
  {
    Key = "IsMainWindowOpen",
    DefaultValue = true
  };
  public static SizeInt32 MainWindowMinimumSize { get; } = new(600, 600);

  public static SettingsDescriptor<SizeInt32> MainWindowSize { get; } = new()
  {
    Key = "MainWindowSize",
    DefaultValue = new(600, 800)
  };
  public static SettingsDescriptor<PointInt32> MainWindowPosition { get; } = new()
  {
    Key = "MainWindowPosition",
    DefaultValue = new(40, 40)
  };
  public static SettingsDescriptor<string> MainWindowDisplay { get; } = new()
  {
    Key = "MainWindowDisplay",
    DefaultValue = string.Empty
  };
  public static SettingsDescriptor<int> WindowBorderMargin { get; } = new()
  {
    Key = "WindowBorderMargin",
    DefaultValue = 20
  };

  public static SettingsDescriptor<bool> ShowImageViewerFilmstrip { get; } = new()
  {
    Key = "ShowImageViewerFilmstrip",
    DefaultValue = true
  };

  // Settings - Note
  public static SizeInt32 NoteWindowMinimumSize { get; } = new(400, 300);
  public static SettingsDescriptor<int> NoteBodyUpdateFrequency { get; } = new()
  {
    Key = "NoteBodyUpdateFrequency",
    DefaultValue = 2
  };

  public static PointInt32 DefaultNoteWindowPosition { get; } = new(32, 32);

  public static SizeInt32 ImageViewerWindowMinimumSize { get; } = new(600, 600);
}