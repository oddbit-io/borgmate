using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BorgMate.Models;
using BorgMate.ViewModels;

namespace BorgMate.Views;

public partial class BrowseArchiveWindow : ModalWindow
{
    public string? SelectedDestination { get; private set; }
    public List<string>? SelectedPaths { get; private set; }
    public long SelectedSize { get; private set; }

    public BrowseArchiveWindow()
    {
        InitializeComponent();

        FileList.SelectionChanged += (_, _) => FileList.SelectedIndex = -1;

        RestoreButton.Click += async (_, _) =>
        {
            if (DataContext is not BrowseArchiveViewModel vm) return;

            var path = await vm.PickRestoreFolderAsync();
            if (path is null) return;

            var paths = vm.GetSelectedPaths();
            if (paths.Count == 0) return;

            SelectedPaths = paths;
            SelectedSize = vm.GetSelectedSize();
            SelectedDestination = path;
            Close();
        };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is BrowseArchiveViewModel vm)
            await vm.LoadContentsCommand.ExecuteAsync(null);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is BrowseArchiveViewModel vm)
            vm.Detach();
        DataContext = null;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        GC.Collect();
    }

    private void OnExpandToggle(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ArchiveFileNode node }
            && DataContext is BrowseArchiveViewModel vm)
        {
            vm.ToggleExpand(node);
        }
    }
}
