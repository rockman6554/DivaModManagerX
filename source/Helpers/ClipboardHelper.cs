using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace DivaModManager.Helpers
{
    /// <summary>
    /// Cross-frontend clipboard helper.
    ///
    /// Avalonia's built-in <c>TopLevel.Clipboard</c> (and the newer
    /// <c>IInputPane.SetClipboardText</c> / <c>Application.Current.Clipboard</c>)
    /// already negotiates Wayland vs X11 automatically when the platform backend
    /// is correctly initialised. We use it as the primary path, then fall back to
    /// the external tools <c>wl-copy</c> (Wayland) and <c>xclip</c> / <c>xsel</c>
    /// (X11) which are present on most Linux distributions. This gives the
    /// best-effort coverage without requiring the user to install anything.
    /// </summary>
    public static class ClipboardHelper
    {
        /// <summary>
        /// Copy <paramref name="text"/> to the system clipboard. Works on Wayland and X11.
        /// </summary>
        /// <returns><c>true</c> if any of the methods succeeded.</returns>
        public static async Task<bool> CopyAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // 1) Try Avalonia's native clipboard first. On Linux it dispatches to the
            //    correct frontend (Wayland or X11) based on the running platform backend.
            try
            {
                var clipboard = GetAvaloniaClipboard();
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                    return true;
                }
            }
            catch
            {
                // Fall through to external tools.
            }

            // 2) Wayland fallback: wl-copy (wayland-clipboard package).
            if (await TryRunClipboardTool("wl-copy", text, isWayland: true)) return true;

            // 3) X11 fallback: xclip then xsel.
            if (await TryRunClipboardTool("xclip", text, isWayland: false)) return true;
            if (await TryRunClipboardTool("xsel", text, isWayland: false)) return true;

            return false;
        }

        /// <summary>
        /// Detect whether the current session is Wayland. Used only for logging/hints.
        /// </summary>
        public static bool IsWaylandSession()
        {
            var wlDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            return !string.IsNullOrEmpty(wlDisplay) ||
                   string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);
        }

        private static IClipboard? GetAvaloniaClipboard()
        {
            // Avalonia 11 exposes the clipboard off the active TopLevel (Window).
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var window = lifetime?.MainWindow;
            return window?.Clipboard;
        }

        /// <summary>
        /// Run an external clipboard tool (wl-copy, xclip, xsel) and pipe <paramref name="text"/> to stdin.
        /// </summary>
        private static async Task<bool> TryRunClipboardTool(string tool, string text, bool isWayland)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = tool,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // xclip and xsel need flags specifying the clipboard selection.
                if (tool == "xclip") psi.ArgumentList.Add("-selection");
                if (tool == "xclip") psi.ArgumentList.Add("clipboard");
                if (tool == "xsel") psi.ArgumentList.Add("--clipboard");
                if (tool == "xsel") psi.ArgumentList.Add("--input");

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                await proc.StandardInput.WriteLineAsync(text);
                proc.StandardInput.Close();
                proc.WaitForExit(2000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
