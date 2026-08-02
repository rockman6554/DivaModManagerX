using System.Collections.Specialized;
using System.Linq;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DivaModManager.ViewModels;

namespace DivaModManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Auto-scroll the log list to the bottom whenever new entries arrive so the
        // latest messages (often errors) are always visible instead of scrolled off.
        Loaded += (s, e) => HookLogAutoScroll();
    }

    private void HookLogAutoScroll()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.LogEntries.CollectionChanged += (sender, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
            {
                // Defer to next frame so the ListBox has a chance to render the new item.
                Dispatcher.UIThread.Post(() =>
                {
                    if (LogList.ItemCount > 0)
                        LogList.ScrollIntoView(LogList.ItemCount - 1);
                }, priority: Avalonia.Threading.DispatcherPriority.Background);
            }
        };
    }
}
