using Windows.ApplicationModel.Resources;

namespace MyNotes.Strings;

internal static partial class LocalizedStrings
{
  public static ResourceLoader DefaultResourceLoader { get; } = ResourceLoader.GetForViewIndependentUse();

  public static string MainWindowTitle => DefaultResourceLoader.GetString("MainWindowTitle");

  public static string ImageViewerWindowTitle => DefaultResourceLoader.GetString("ImageViewerWindowTitle");
  public static string FileSavePickerOriginalFileFormat => DefaultResourceLoader.GetString("FileSavePickerOriginalFileFormat");

  public static string NavigationHomeTitle => DefaultResourceLoader.GetString("NavigationHome_Title");
  public static string NavigationBookmarksTitle => DefaultResourceLoader.GetString("NavigationBookmarks_Title");
  public static string NavigationTrashTitle => DefaultResourceLoader.GetString("NavigationTrash_Title");
  public static string NavigationSettingsTitle => DefaultResourceLoader.GetString("NavigationSettings_Title");

  public static string AppLanguageUseSystem => DefaultResourceLoader.GetString("AppLanguage_UseSystem");

  public static string NavigationUserCompositeNodeDisplayName => DefaultResourceLoader.GetString("NavigationUserCompositeNode_DisplayName");
  public static string NavigationUserLeafNodeDisplayName => DefaultResourceLoader.GetString("NavigationUserLeafNode_DisplayName");
  public static string NavigationUserRootNodeDisplayName => DefaultResourceLoader.GetString("NavigationUserRootNode_DisplayName");

  public static string NoteSortKeyCreated => DefaultResourceLoader.GetString("NoteSortKey_Created");
  public static string NoteSortKeyModified => DefaultResourceLoader.GetString("NoteSortKey_Modified");
  public static string NoteSortKeyTitle => DefaultResourceLoader.GetString("NoteSortKey_Title");

  public static string SortDirectionAscending => DefaultResourceLoader.GetString("SortDirection_Ascending");
  public static string SortDirectionDescending => DefaultResourceLoader.GetString("SortDirection_Descending");

  public static string PreviewLayoutTypeGrid => DefaultResourceLoader.GetString("PreviewLayoutType_Grid");
  public static string PreviewLayoutTypeList => DefaultResourceLoader.GetString("PreviewLayoutType_List");

  public static string PreviewTileSizeSmallest => DefaultResourceLoader.GetString("PreviewTileSize_Smallest");
  public static string PreviewTileSizeSmaller => DefaultResourceLoader.GetString("PreviewTileSize_Smaller");
  public static string PreviewTileSizeSmall => DefaultResourceLoader.GetString("PreviewTileSize_Small");
  public static string PreviewTileSizeMedium => DefaultResourceLoader.GetString("PreviewTileSize_Medium");
  public static string PreviewTileSizeLarge => DefaultResourceLoader.GetString("PreviewTileSize_Large");
  public static string PreviewTileSizeLarger => DefaultResourceLoader.GetString("PreviewTileSize_Larger");
  public static string PreviewTileSizeLargest => DefaultResourceLoader.GetString("PreviewTileSize_Largest");

  public static string PreviewTileRatioShorter => DefaultResourceLoader.GetString("PreviewTileRatio_Shorter");
  public static string PreviewTileRatioShort => DefaultResourceLoader.GetString("PreviewTileRatio_Short");
  public static string PreviewTileRatioSquare => DefaultResourceLoader.GetString("PreviewTileRatio_Square");
  public static string PreviewTileRatioTall => DefaultResourceLoader.GetString("PreviewTileRatio_Tall");
  public static string PreviewTileRatioTaller => DefaultResourceLoader.GetString("PreviewTileRatio_Taller");

  public static string JumpListNewNote => DefaultResourceLoader.GetString("JumpList_NewNote");
  public static string JumpListMainWindow => DefaultResourceLoader.GetString("JumpList_MainWindow");
  public static string JumpListSettings => DefaultResourceLoader.GetString("JumpList_Settings");
  public static string SavedMessage => DefaultResourceLoader.GetString("Message_Saved");
  public static string FailedMessage => DefaultResourceLoader.GetString("Message_Failed");
}

partial class LocalizedStrings
{
  public static ResourceLoader CreateUserNavigationDialogResourceLoader { get; } = ResourceLoader.GetForViewIndependentUse("CreateUserNavigationDialog");

  public static string CreateUserNavigationDialogTitleText => CreateUserNavigationDialogResourceLoader.GetString("ContentDialog_TitleText");
  public static string CreateUserNavigationDialogSubTitleTextBlockText => CreateUserNavigationDialogResourceLoader.GetString("SubTitleTextBlock_Text");
}
partial class LocalizedStrings
{
  public static ResourceLoader ConfirmDeleteDialogResourceLoader { get; } = ResourceLoader.GetForViewIndependentUse("ConfirmDeleteDialog"); 
  
  public static string ConfirmDeleteDialogAffixTitleTextBlockText => ConfirmDeleteDialogResourceLoader.GetString("ContentDialog_TitleAffixText"); }
partial class LocalizedStrings
{
  public static ResourceLoader UpdateUserNavigationDialogResourceLoader { get; } = ResourceLoader.GetForViewIndependentUse("UpdateUserNavigationDialog");

  public static string UpdateUserNavigationDialogTitleText => UpdateUserNavigationDialogResourceLoader.GetString("ContentDialog_TitleText");
  public static string UpdateUserNavigationDialogSubTitleTextBlockText => UpdateUserNavigationDialogResourceLoader.GetString("SubTitleTextBlock_Text");
}