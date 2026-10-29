using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using BorgMate.Domain.Repositories;
using BorgMate.Persistence.Configuration;
using BorgMate.Presentation.Dialogs;
using BorgMate.Runtime.Orchestration;

namespace BorgMate.Runtime.Security;

/// <summary>Confirms repository relocations for all operations and persists approvals for saved repositories.</summary>
public class RepositoryRelocationConfirmation
{
    private readonly IRepositoryConfigStore _repositoryStore;

    public RepositoryRelocationConfirmation(IRepositoryConfigStore repositoryStore)
    {
        _repositoryStore = repositoryStore;
    }

    public virtual async Task<bool> ConfirmAsync(BorgOperationRequest request, RepositoryRelocation relocation,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.Repository.ConfirmedRelocations.Contains(relocation))
            return true;
        if (!request.Interactive || !await PromptAsync(request.Repository.Name, relocation, ct))
            return false;
        ct.ThrowIfCancellationRequested();

        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            await RememberAsync(request, relocation, ct);
        else
            await Dispatcher.UIThread.InvokeAsync(() => RememberAsync(request, relocation, ct));
        return true;
    }

    protected virtual async Task<bool> PromptAsync(string repositoryName, RepositoryRelocation relocation,
        CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
            DialogHelper.ConfirmRelocationAsync(repositoryName, relocation, ct));
    }

    private async Task RememberAsync(BorgOperationRequest request, RepositoryRelocation relocation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        request.Repository.ConfirmedRelocations = [.. request.Repository.ConfirmedRelocations, relocation];
        if (!request.PersistRelocations)
            return;

        var update = await _repositoryStore.UpdateAsync(request.Repository, ct);
        if (update.IsT1)
            throw new IOException("Could not update the confirmed repository relocation.");
        var save = await _repositoryStore.SaveAsync(ct);
        if (save.IsT1)
            throw new IOException("Could not save the confirmed repository relocation.");
    }
}
