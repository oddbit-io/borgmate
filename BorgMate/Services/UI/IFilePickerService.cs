using System.Collections.Generic;
using System.Threading.Tasks;

namespace BorgMate.Services.UI;

public interface IFilePickerService
{
    Task<string?> PickFolderAsync(string title);
    Task<IReadOnlyList<string>> PickFoldersAsync(string title);
    Task<string?> PickFileAsync(string title);
}
