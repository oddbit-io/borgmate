using Avalonia.Controls;
using Avalonia.Controls.Templates;
using BorgMate.ViewModels;
using BorgMate.Views;

namespace BorgMate;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return param switch
        {
            MainWindowViewModel => new MainWindow(),
            RepositoryListViewModel => new RepositoryListView(),
            ArchiveListViewModel => new ArchiveListView(),
            NotificationsViewModel => new NotificationsView(),
            _ => new TextBlock { Text = "No view for: " + param.GetType().Name }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
