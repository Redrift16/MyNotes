using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Windows.Storage.Pickers;

using MyNotes.Application.Notes.Commands;
using MyNotes.Application.Notes.Services;
using MyNotes.Common.Commands;
using MyNotes.Common.Mappers;
using MyNotes.Constants;
using MyNotes.Debugging;
using MyNotes.Domain.Notes;
using MyNotes.Models.Media;
using MyNotes.Services.Windows;
using MyNotes.ViewModels.Media.Providers;

namespace MyNotes.ViewModels.Media;

internal sealed partial class ImageCollectionViewModel : AsyncViewModelBase
{
  private readonly NoteImageService NoteImageService;
  private readonly ImageViewModelProvider ImageViewModelProvider;

  private ImageCollectionKey CollectionKey { get; }

  private NoteId NoteId { get; }

  #region Object Lifetime Management
  public ImageCollectionViewModel(NoteImageService noteImageService, ImageViewModelProvider imageViewModelProvider, ImageCollectionKey key)
  {
    NoteImageService = noteImageService;
    ImageViewModelProvider = imageViewModelProvider;

    CollectionKey = key;
    NoteId = NoteId.Create(CollectionKey.Value);

    InitializationTask = InitializeAsync();
    SetCommands();
  }

  public Task InitializationTask { get; }
  private async Task InitializeAsync()
  {
    var imageDtos = await NoteImageService.GetImagesByNoteIdAsync(NoteId);
    _imageViewModelLeases = new(imageDtos.Select(i => ImageViewModelProvider.Resolve(NoteImageMapper.ToModel(i))));
    CalculateImageCount();
  }

  protected override async ValueTask DisposeAsyncCore()
  {
    _imageViewModelLeases?.Dispose();
  }
  #endregion

  private LeasedImageViewModelCollection? _imageViewModelLeases;
  public IReadOnlyList<ImageViewModel> ImageViewModels => _imageViewModelLeases?.ViewModels ?? throw new InvalidOperationException("객체가 초기화되지 않음");

  [ObservableProperty]
  public partial bool HasImages { get; private set; }

  [ObservableProperty]
  public partial int ImageCount { get; private set; }

  private void CalculateImageCount()
  {
    ImageCount = ImageViewModels.Count;
    HasImages = ImageCount > 0;
  }

  public async Task<bool> MoveImageAsync(int sourceIndex, int targetIndex)
  {
    if (ImageViewModels is null)
    {
      return false;
    }

    if (sourceIndex < 0 || sourceIndex >= ImageViewModels.Count || targetIndex < 0 || targetIndex >= ImageViewModels.Count)
    {
      return false;
    }

    await NoteImageService.MoveImageAsync(new MoveNoteImageAppCommand()
    {
      SourceId = ImageViewModels[sourceIndex].ImageDescriptor.Id,
      TargetId = ImageViewModels[targetIndex].ImageDescriptor.Id
    });

    _imageViewModelLeases?.Move(sourceIndex, targetIndex);

    return true;
  }

  public void RemoveImageFromCollection(ImageViewModel imageViewModel)
  {
    _imageViewModelLeases?.Remove(imageViewModel);
    CalculateImageCount();
  }
}

internal sealed partial class ImageCollectionViewModel
{
  public AsyncCommand<Microsoft.UI.WindowId>? InsertImageCommand { get; private set; }

  [MemberNotNull(nameof(InsertImageCommand))]
  private void SetCommands()
  {
    InsertImageCommand = new()
    {
      ExecuteFunc = async (windowId) =>
      {
        // FilePicker 열기
        FileOpenPicker picker = new(windowId)
        {
          ViewMode = PickerViewMode.Thumbnail,
          SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };

        foreach (var fileType in AppStrings.BitmapImageFileTypeFilter)
        {
          picker.FileTypeFilter.Add(fileType);
        }

        foreach (var pickFileResult in await picker.PickMultipleFilesAsync())
        {
          try
          {
            // 원본 이미지 파일 가져오기
            var imageDto = await NoteImageService.AttachImageAsync(new AttachNoteImageAppCommand()
            {
              NoteId = NoteId.Create(CollectionKey.Value),
              OriginalFilePath = pickFileResult.Path
            });

            var imageDescriptor = NoteImageMapper.ToModel(imageDto);
            _imageViewModelLeases?.Add(ImageViewModelProvider.Resolve(imageDescriptor));
          }
          catch (Exception e)
          {
            ConsoleHelper.WriteLine(true, "{0}: {1}", "File Exception", e.Message);
          }
        }

        CalculateImageCount();
      }
    };
  }
}
