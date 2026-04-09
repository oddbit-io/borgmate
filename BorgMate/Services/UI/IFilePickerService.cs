using System.Collections.Generic;
using System.Threading.Tasks;

namespace BorgMate.Services.UI;

public interface IFilePickerService
{
    Task<string?> PickFolderAsync(string title = "Select Folder");
    Task<IReadOnlyList<string>> PickFoldersAsync(string title = "Select Folders");
    Task<string?> PickFileAsync(string title = "Select File");
}
