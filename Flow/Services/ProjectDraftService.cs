using Flow.Models;

namespace Flow.Services;

public class ProjectDraftService
{
    private readonly DraftStorageService _storageService;

    public ProjectDraftService(string? storageDirectory = null)
    {
        _storageService = string.IsNullOrWhiteSpace(storageDirectory)
            ? new DraftStorageService()
            : new DraftStorageService(storageDirectory);
    }

    public string DraftFilePath => _storageService.DraftFilePath;

    public bool HasDraft() => _storageService.HasDraft();

    public void SaveDraft(SequenceProject project, string? sourceProjectPath = null) =>
        _storageService.Save(project, sourceProjectPath);

    public ProjectDraft? LoadDraft() => _storageService.Load();

    public bool TryLoadDraft(out SequenceProject project)
    {
        var draft = LoadDraft();
        if (draft == null)
        {
            project = new SequenceProject();
            return false;
        }

        project = draft.Project;
        return true;
    }

    public void DeleteDraft() => _storageService.Delete();
}
