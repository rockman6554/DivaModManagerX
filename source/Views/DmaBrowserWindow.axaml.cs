using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DivaModManager.Models;
using DivaModManager.Services;
using DivaModManager.ViewModels;

namespace DivaModManager.Views;

public partial class DmaBrowserWindow : Window
{
    private readonly DmaBrowserViewModel _vm;

    public DmaBrowserWindow()
    {
        InitializeComponent();
        _vm = new DmaBrowserViewModel();
        DataContext = _vm;
        _vm.DownloadProgress += (name, pct, dl, total) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _vm.ProgressValue = pct * 100;
                _vm.IsLoading = pct < 1;
            });
        _vm.InstallComplete += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _vm.IsLoading = false;
                _vm.ProgressValue = 0;
            });
        Loaded += async (s, e) => await _vm.RefreshAsync();
        // Cancel any in-flight loads when the window closes — prevents the freeze on 2nd open
        Closing += (s, e) => _vm.CancelLoads();
    }

    private async void Search_Click(object? sender, RoutedEventArgs e) => await _vm.RefreshAsync();
    private async void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await _vm.RefreshAsync();
    }
    private async void PrevPage_Click(object? sender, RoutedEventArgs e) => await _vm.PrevPageAsync();
    private async void NextPage_Click(object? sender, RoutedEventArgs e) => await _vm.NextPageAsync();
    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPost == null) return;
        await _vm.InstallSelectedAsync();
    }
    private async void ResultsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.SelectedPost == null) return;
        await _vm.InstallSelectedAsync();
    }

    private void OpenDma_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://divamodarchive.com",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
