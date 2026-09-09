using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using MyNotes.Application.Contracts.Converters;
using MyNotes.Common.Helpers;
using MyNotes.Constants;
using MyNotes.Domain.Notes;
using MyNotes.Messaging;
using MyNotes.Messaging.Messages;
using MyNotes.Models.Notes;

namespace MyNotes.ViewModels.Notes;

internal partial class NotePreviewViewModel : AsyncViewModelBase
{
  protected readonly IRtfTextConverter RtfTextConverter;

  protected readonly IAsyncViewModelLease<NoteViewModel> NoteViewModelLease;
  public NoteViewModel NoteViewModel => NoteViewModelLease.ViewModel;
  public NoteModel Note => NoteViewModel.Note;

  private readonly int _previewTextMaxLength = 500;

  public NotePreviewViewModel(IRtfTextConverter rtfTextConverter, IAsyncViewModelLease<NoteViewModel> noteViewModelLease)
  {
    NoteViewModelLease = noteViewModelLease;

    RtfTextConverter = rtfTextConverter;
    _ = SetPreviewAsync();
    RegisterMessengers();
    // TODO: 노트 본문 변경 시 일반 미리보기는 다시 계산하고,
    // 검색 미리보기는 검색 결과와 강조 범위를 함께 재계산할 수 있도록 갱신 흐름을 구현할 것.
  }

  protected override async ValueTask DisposeAsyncCore()
  {
    UnregisterMessengers();
    await NoteViewModelLease.DisposeAsync();
  }

  protected string _preview = string.Empty;
  public string Preview
  {
    get => _preview;
    set => SetProperty(ref _preview, value);
  }

  private async Task<string> GetPreviewAsync() => RtfTextConverter.GetPreview(await StreamHelper.ToRandomAccessStreamAsync(Note.Body), 0, _previewTextMaxLength);

  private async Task SetPreviewAsync() => Preview = await GetPreviewAsync();

  private void RegisterMessengers()
  {
    WeakReferenceMessenger.Default.Register<NotePreviewViewModel, NotePreviewUpdateRequestedMessage, MessageToken<NoteId>>(this, MessageToken<NoteId>.Create(Note.Id), async static (recipient, message) => await recipient.SetPreviewAsync());
  }

  private void UnregisterMessengers() => WeakReferenceMessenger.Default.UnregisterAll(this);
}
