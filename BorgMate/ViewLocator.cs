using Avalonia.Controls;
using Avalonia.Controls.Templates;
using BorgMate.Presentation.ViewModels;
using MainWindow = BorgMate.Presentation.Views.MainWindow;
using MainWindowViewModel = BorgMate.Presentation.ViewModels.MainWindowViewModel;
using NotificationsView = BorgMate.Presentation.Views.NotificationsView;
using NotificationsViewModel = BorgMate.Presentation.ViewModels.NotificationsViewModel;
using RepositoriesPageViewModel = BorgMate.Presentation.ViewModels.RepositoriesPageViewModel;
using RepositoryListView = BorgMate.Presentation.Views.RepositoryListView;

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
            RepositoriesPageViewModel => new RepositoryListView(),
            NotificationsViewModel => new NotificationsView(),
            _ => new TextBlock { Text = "No view for: " + param.GetType().Name }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}