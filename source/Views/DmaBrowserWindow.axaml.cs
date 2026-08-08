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
        Closing += (s, e) => _vm.CancelLoads();
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
        // The Install button lives inside the ListBox item template, so SelectedPost
        // may not be set yet when the user clicks it. Resolve the post from the
        // button's own DataContext (which is the DmaPostViewModel for that row).
        var postVm = (sender as Avalonia.Controls.Button)?.DataContext as DmaPostViewModel;
        if (postVm != null)
            _vm.SelectedPost = postVm;
        if (_vm.SelectedPost == null)
        {
            Global.logger?.WriteLine("No post selected for install.", Services.LoggerType.Warning);
            return;
        }
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

    /// <summary>
    /// Open the selected mod's DMA profile page in the user's default browser.
    /// </summary>
    private void ViewMod_Click(object? sender, RoutedEventArgs e)
    {
        var url = _vm.SelectedPost?.ProfileUrl;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void CancelInstall_Click(object? sender, RoutedEventArgs e) => _vm.CancelInstall();

    /// <summary>
    /// When the user clicks a mod card in the results list, set it as the selected post
    /// so the right-hand preview panel updates with its image, title, stats, etc.
    /// </summary>
    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Avalonia.Controls.Border border &&
            border.DataContext is DmaPostViewModel postVm)
        {
            _vm.SelectedPost = postVm;
        }
    }

    /// <summary>
    /// When the user clicks a gallery thumbnail, swap the large preview image.
    /// </summary>
    private void GalleryImage_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Avalonia.Controls.Border border && border.DataContext is string url)
        {
            _vm.SelectedPost?.SelectImage(url);
        }
    }
}
