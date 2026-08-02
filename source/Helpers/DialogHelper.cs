using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace DivaModManager.Helpers
{
    /// <summary>
    /// Lightweight modal dialog helpers built on a plain Avalonia <see cref="Window"/>.
    ///
    /// Avalonia 11's core does not ship a <c>ContentDialog</c>/<c>MessageBox</c>, so we use a
    /// minimal custom window. This keeps us free of extra NuGet dependencies while giving the
    /// user real, visible confirmations and error messages (the log panel alone is not enough).
    /// </summary>
    public static class DialogHelper
    {
        /// <summary>
        /// Show a confirmation dialog with OK/Cancel. Returns true if the user accepted.
        /// </summary>
        public static async Task<bool> ShowConfirmAsync(Window? owner, string title, string message,
            string okText = "OK", string cancelText = "Cancel")
        {
            var result = false;
            var dlg = BuildDialog(title, message, okText, cancelText, isDanger: false,
                onOk: () => result = true);
            await ShowAsync(dlg, owner);
            return result;
        }

        /// <summary>
        /// Show a confirmation dialog styled for destructive actions (red OK button).
        /// </summary>
        public static async Task<bool> ShowConfirmDestructiveAsync(Window? owner, string title, string message,
            string okText = "Delete", string cancelText = "Cancel")
        {
            var result = false;
            var dlg = BuildDialog(title, message, okText, cancelText, isDanger: true,
                onOk: () => result = true);
            await ShowAsync(dlg, owner);
            return result;
        }

        /// <summary>
        /// Show an error dialog with a single OK button.
        /// </summary>
        public static async Task ShowErrorAsync(Window? owner, string title, string message)
        {
            var dlg = BuildDialog(title, message, "OK", null, isDanger: true);
            await ShowAsync(dlg, owner);
        }

        /// <summary>
        /// Show an informational/success dialog with a single OK button.
        /// </summary>
        public static async Task ShowInfoAsync(Window? owner, string title, string message)
        {
            var dlg = BuildDialog(title, message, "OK", null, isDanger: false);
            await ShowAsync(dlg, owner);
        }

        /// <summary>
        /// Show a single-line text input dialog. Enter accepts, Esc cancels.
        /// Returns the entered text, or null if the user cancelled.
        /// </summary>
        public static async Task<string?> ShowInputAsync(Window? owner, string title, string message,
            string watermark = "", string? initial = null, string okText = "OK", string cancelText = "Cancel")
        {
            string? result = null;

            var msg = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#E8E8EC")),
                FontSize = 13
            };

            var input = new TextBox
            {
                Text = initial ?? string.Empty,
                Watermark = watermark,
                AcceptsReturn = false,
                FontSize = 13
            };

            var okBtn = new Button
            {
                Content = okText,
                Background = new SolidColorBrush(Color.Parse("#39C5BB")),
                Foreground = Brushes.Black,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(20, 8),
                CornerRadius = new CornerRadius(6),
                MinWidth = 90,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsDefault = true
            };
            var cancelBtn = new Button
            {
                Content = cancelText,
                Padding = new Thickness(20, 8),
                CornerRadius = new CornerRadius(6),
                MinWidth = 90,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsCancel = true
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { cancelBtn, okBtn }
            };

            var win = new Window
            {
                Title = title,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 12,
                    Children = { msg, input, buttons }
                },
                Width = 560,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse("#161618")),
                MinWidth = 380
            };

            void Accept() { result = input.Text; win.Close(); }
            okBtn.Click += (s, e) => Accept();
            cancelBtn.Click += (s, e) => win.Close();
            input.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
                else if (e.Key == Key.Escape) { win.Close(); e.Handled = true; }
            };
            win.Opened += (s, e) =>
            {
                input.Focus();
                input.SelectAll();
            };

            await ShowAsync(win, owner);
            return result;
        }

        private static Window BuildDialog(string title, string message,
            string okText, string? cancelText, bool isDanger, System.Action? onOk = null)
        {
            var accent = isDanger ? "#F87171" : "#39C5BB";

            var msg = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#E8E8EC")),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var okBtn = new Button
            {
                Content = okText,
                Background = new SolidColorBrush(Color.Parse(accent)),
                Foreground = Brushes.Black,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(20, 8),
                CornerRadius = new CornerRadius(6),
                MinWidth = 90,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { okBtn }
            };

            if (!string.IsNullOrEmpty(cancelText))
            {
                var cancelBtn = new Button
                {
                    Content = cancelText,
                    Padding = new Thickness(20, 8),
                    CornerRadius = new CornerRadius(6),
                    MinWidth = 90,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
                buttons.Children.Insert(0, cancelBtn);
                cancelBtn.Click += (s, e) => { /* close via window */ };
                // Wire close: store on the window's Tag
            }

            var panel = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 0,
                Children = { msg, buttons }
            };

            var win = new Window
            {
                Title = title,
                Content = panel,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse("#161618")),
                MinWidth = 360
            };

            okBtn.Click += (s, e) => { onOk?.Invoke(); win.Close(); };
            if (buttons.Children.Count > 1)
            {
                var cancelBtn = (Button)buttons.Children[0];
                cancelBtn.Click += (s, e) => win.Close();
            }

            return win;
        }

        private static async Task ShowAsync(Window dialog, Window? owner)
        {
            owner ??= (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                await dialog.ShowDialog(owner!);
        }
    }
}
