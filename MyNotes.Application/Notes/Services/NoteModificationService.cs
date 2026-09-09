using MyNotes.Application.Contracts.Converters;
using MyNotes.Application.Contracts.Notes.Models;
using MyNotes.Application.Contracts.Notes.Persistence;
using MyNotes.Application.Contracts.Persistence;
using MyNotes.Application.Notes.Commands;
using MyNotes.Application.Notes.Results;
using MyNotes.Application.Results;
using MyNotes.Common.Enums.Modes;
using MyNotes.Common.Helpers;
using MyNotes.Domain.Notes;

namespace MyNotes.Application.Notes.Services;

internal sealed partial class NoteModificationService
{
  private readonly INoteRepository NoteRepository;
  private readonly INoteSearcher NoteSearcher;
  private readonly IRtfTextConverter RtfTextConverter;

  public NoteModificationService(INoteRepository noteRepositoryService, INoteSearcher noteSearcher, IRtfTextConverter rtfTextConverter)
  {
    NoteRepository = noteRepositoryService;
    NoteSearcher = noteSearcher;
    RtfTextConverter = rtfTextConverter;
  }

  public async Task<UpdateNoteResult> UpdateNoteAsync(UpdateNoteAppCommand appCommand, CancellationToken cancellationToken = default)
  {
    NotePatchDto notePatchDto = appCommand.PatchDto;
    var id = notePatchDto.Id;
    if (notePatchDto.IsEmpty)
    {
      return new() { Status = AppUpdateStatus.Unchanged };
    }

    DateTimeOffset modified = DateTimeOffset.UtcNow;
    var updateResult = await NoteRepository.UpdateNoteAsync(notePatchDto, modified, cancellationToken);

    if (updateResult is PersistenceMutationStatus.Failed)
    {
      return new() { Status = AppUpdateStatus.Failed };
    }

    if (updateResult is PersistenceMutationStatus.Expired or PersistenceMutationStatus.Unchanged)
    {
      return new() { Status = AppUpdateStatus.Unchanged };
    }

    if (updateResult is PersistenceMutationStatus.NotFound)
    {
      return new() { Status = AppUpdateStatus.TargetNotFound };
    }

    if (notePatchDto.Title.HasValue || notePatchDto.Body.HasValue)
    {
      NoteProjectionDto noteProjectionDto = await NoteRepository.GetNoteFieldValuesAsync(id, NoteProjectionFields.Title | NoteProjectionFields.Body, cancellationToken);
      if (noteProjectionDto.Title.TryGet(out var title) && noteProjectionDto.Body.TryGet(out var body))
      {
        using var stream = await StreamHelper.ToRandomAccessStreamAsync(body);
        await NoteSearcher.WriteNoteIndexAsync(NoteMappers.ToSearchDocumentDto(id, title, RtfTextConverter.ToPlainText(stream)), cancellationToken);
      }
    }

    return new()
    {
      Status = AppUpdateStatus.Succeeded,
      Modified = modified
    };
  }

  public async Task<AppUpdateStatus> UpdateNoteViewStateAsync(UpdateNoteViewStateAppCommand appCommand, CancellationToken cancellationToken = default)
  {
    NoteViewStatePatchDto noteViewStatePatchDto = appCommand.PatchDto;
    if (noteViewStatePatchDto.IsEmpty)
    {
      return AppUpdateStatus.Unchanged;
    }

    return await NoteRepository.UpdateNoteViewStateAsync(noteViewStatePatchDto, cancellationToken) switch
    {
      PersistenceMutationStatus.Applied => AppUpdateStatus.Succeeded,
      PersistenceMutationStatus.Expired or PersistenceMutationStatus.Unchanged => AppUpdateStatus.Unchanged,
      PersistenceMutationStatus.NotFound => AppUpdateStatus.TargetNotFound,
      PersistenceMutationStatus.Failed => AppUpdateStatus.Failed,
      _ => throw new InvalidOperationException()
    };
  }

  public async Task<AppUpdateStatus> DeleteNoteAsync(DeleteNoteAppCommand deleteNoteAppCommand, CancellationToken cancellationToken = default)
  {
    NoteId noteId = deleteNoteAppCommand.Id;
    DeleteMode deleteMode = deleteNoteAppCommand.DeleteMode;
    PersistenceMutationStatus deleteResult = await NoteRepository.DeleteNoteAsync(noteId, deleteMode, cancellationToken);

    if (deleteResult is PersistenceMutationStatus.Failed)
    {
      return AppUpdateStatus.Failed;
    }

    if (deleteResult is PersistenceMutationStatus.Expired or PersistenceMutationStatus.Unchanged)
    {
      return AppUpdateStatus.Unchanged;
    }

    if (deleteResult is PersistenceMutationStatus.NotFound)
    {
      return AppUpdateStatus.TargetNotFound;
    }

    if (deleteMode is DeleteMode.Permanent)
    {
      await NoteSearcher.DeleteNoteIndexAsync(noteId, cancellationToken);
    }

    return AppUpdateStatus.Succeeded;
  }

  public Task CommitSearchIndexAsync(CancellationToken cancellationToken = default) => NoteSearcher.CommitAsync(cancellationToken);
}
