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

public partial class GameBananaBrowserWindow : Window
{
    private readonly GameBananaBrowserViewModel _vm;
    private CancellationTokenSource? _loadCts;

    public GameBananaBrowserWindow()
    {
        InitializeComponent();
        _vm = new GameBananaBrowserViewModel();
        DataContext = _vm;
        _vm.DownloadProgress += (name, pct, dl, total) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _vm.ProgressValue = pct * 100;
                if (_vm.IsInstalling)
                {
                    _vm.InstallStatus = $"Downloading {name}… {pct * 100:0}%";
                    _vm.InstallStatusColor = "#39C5BB";
                }
            });
        _vm.InstallComplete += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _vm.IsLoading = false;
                if (!_vm.IsInstalling)
                    _vm.ProgressValue = 0;
            });
        Loaded += async (s, e) =>
        {
            SearchBox.Focus();
            await _vm.RefreshAsync();
        };
        // Cancel any in-flight loads when the window closes — prevents the freeze on 2nd open
        Closing += (s, e) =>
        {
            _loadCts?.Cancel();
            _vm.CancelLoads();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
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
        // The Install button lives inside the ListBox item template, so SelectedRecord
        // may not be set yet when the user clicks it. Resolve the record from the
        // button's own DataContext (which is the GameBananaRecordViewModel for that row).
        var recVm = (sender as Avalonia.Controls.Button)?.DataContext as GameBananaRecordViewModel;
        if (recVm != null)
            _vm.SelectedRecord = recVm;
        if (_vm.SelectedRecord == null)
        {
            Global.logger?.WriteLine("No mod selected for install.", Services.LoggerType.Warning);
            return;
        }
        await _vm.InstallSelectedAsync();
    }

    private async void ResultsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.SelectedRecord == null) return;
        await _vm.InstallSelectedAsync();
    }

    private void OpenGameBanana_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://gamebanana.com/games/16522",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
