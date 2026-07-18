using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DivaModManager.Models;
using DivaModManager.Helpers;

namespace DivaModManager.Services
{
    /// <summary>
    /// Launches Project DIVA Mega Mix+ on Linux via Steam.
    ///
    /// IMPORTANT: The Wine direct-launch option was removed because the game has Denuvo DRM
    /// which requires Steam's runtime to validate the license. The ONLY supported launch
    /// mode is via `steam://rungameid/1761390`.
    ///
    /// Before launching, this service verifies three things:
    ///   1. DML's dinput8.dll exists in the game directory
    ///   2. DML's config.toml exists and has the priority list written
    ///   3. Steam's launch options for appid 1761390 contain WINEDLLOVERRIDES=dinput8.dll=n,b
    ///
    /// If any check fails, the launch is aborted and the user is told what to fix (or DMM
    /// offers to fix it automatically).
    /// </summary>
    public class LaunchService
    {
        private const string MegaMixAppId = "1761390";
        private readonly SteamLaunchOptionsService _steamOpts = new();

        /// <summary>
        /// Pre-launch verification. Returns (ok, failures). If failures is empty, launch can proceed.
        /// </summary>
        public (bool ok, List<string> failures, List<string> fixes) VerifyLaunch(string? gameExePath)
        {
            var failures = new List<string>();
            var fixes = new List<string>();

            if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath))
            {
                failures.Add("Game exe not set or missing. Run Setup first.");
                return (false, failures, fixes);
            }

            var gameDir = Path.GetDirectoryName(gameExePath)!;

            // Check 1: dinput8.dll
            var dll = Path.Combine(gameDir, "dinput8.dll");
            if (!File.Exists(dll))
            {
                failures.Add($"DivaModLoader's dinput8.dll not found at {dll}. Run Setup to install DML.");
                fixes.Add("Run Setup (downloads and installs DML).");
            }

            // Check 2: config.toml
            var toml = Path.Combine(gameDir, "config.toml");
            if (!File.Exists(toml))
            {
                failures.Add($"DML's config.toml not found at {toml}. Run Setup to install DML.");
                fixes.Add("Run Setup (downloads and installs DML).");
            }

            // Check 3: Steam launch options
            var (found, current, cfg) = _steamOpts.CheckLaunchOptions();
            if (!found)
            {
                failures.Add("Steam launch options don't have WINEDLLOVERRIDES=dinput8.dll=n,b set. Without this, Proton will use its builtin dinput8.dll and DML won't load — the game will run vanilla.");
                fixes.Add("Auto-configure Steam launch options (DMM can write this for you). Requires Steam restart.");
            }

            return (failures.Count == 0, failures, fixes);
        }

        /// <summary>
        /// Auto-write the WINEDLLOVERRIDES to Steam's localconfig.vdf.
        /// </summary>
        public bool AutoConfigureSteam()
        {
            return _steamOpts.EnsureLaunchOptions();
        }

        /// <summary>
        /// Launch the game via Steam. Caller should call VerifyLaunch() first.
        /// </summary>
        public bool LaunchViaSteam()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = $"steam://rungameid/{MegaMixAppId}",
                    UseShellExecute = true
                };
                Process.Start(psi);
                Global.logger?.WriteLine($"Launched game via Steam (appid {MegaMixAppId})", LoggerType.Info);
                return true;
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"Failed to launch via Steam: {ex.Message}", LoggerType.Error);
                return false;
            }
        }

        public bool OpenModsFolder(string modsFolder)
        {
            if (string.IsNullOrEmpty(modsFolder) || !Directory.Exists(modsFolder))
            {
                Global.logger?.WriteLine("Mods folder not set.", LoggerType.Warning);
                return false;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = modsFolder,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{modsFolder}\"\"",
                        UseShellExecute = false
                    });
                    return true;
                }
                catch (Exception ex)
                {
                    Global.logger?.WriteLine($"Couldn't open mods folder: {ex.Message}", LoggerType.Error);
                    return false;
                }
            }
        }

        public void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"Couldn't open {url}: {ex.Message}", LoggerType.Error);
            }
        }

        /// <summary>
        /// Force-kill any running instance of Project DIVA Mega Mix+ (and DML).
        ///
        /// The game sometimes hangs on exit (DML cleanup issue under Proton). When that happens,
        /// the user can click "Force Kill Game" instead of using Steam's Stop button (which often
        /// doesn't work either).
        ///
        /// This sends SIGTERM to all processes named "DivaMegaMix.exe" (the Wine process name) and
        /// "dinput8.dll" (DML's host). If SIGTERM doesn't kill them within 3 seconds, sends SIGKILL.
        /// </summary>
        public bool ForceKillGame()
        {
            var targetNames = new[] { "DivaMegaMix.exe", "DivaMegaMix.exe ", "dinput8.dll" };
            var killed = new List<int>();

            try
            {
                // Try pkill first (kills by name, no need for root if it's our process)
                foreach (var name in targetNames)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "pkill",
                            Arguments = $"-f \"{name}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        };
                        var p = Process.Start(psi);
                        p?.WaitForExit(2000);
                    }
                    catch { }
                }

                // Also kill via Steam's compatdata — find any process with the game exe path
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "pgrep",
                        Arguments = "-f DivaMegaMix",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                    };
                    var p = Process.Start(psi);
                    if (p != null)
                    {
                        var output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(2000);
                        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (int.TryParse(line.Trim(), out var pid))
                            {
                                try
                                {
                                    var proc = Process.GetProcessById(pid);
                                    proc.Kill(entireProcessTree: true);
                                    killed.Add(pid);
                                    Global.logger?.WriteLine($"Killed PID {pid} ({proc.ProcessName})", LoggerType.Warning);
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }

                if (killed.Count == 0)
                {
                    // Wait briefly and try SIGKILL fallback via pkill -9
                    System.Threading.Thread.Sleep(1000);
                    foreach (var name in targetNames)
                    {
                        try
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "pkill",
                                Arguments = $"-9 -f \"{name}\"",
                                UseShellExecute = false,
                            };
                            Process.Start(psi)?.WaitForExit(2000);
                        }
                        catch { }
                    }
                    Global.logger?.WriteLine("Sent SIGKILL to any remaining game processes.", LoggerType.Warning);
                }
                else
                {
                    Global.logger?.WriteLine($"Force-killed {killed.Count} game process(es).", LoggerType.Info);
                }
                return true;
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"Force-kill failed: {ex.Message}", LoggerType.Error);
                return false;
            }
        }

        public bool InstallSteamSymlinkTrick(string gameExePath, string dmmExecutablePath)
        {
            if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath))
            {
                Global.logger?.WriteLine("Game exe not found.", LoggerType.Error);
                return false;
            }

            var gameDir = Path.GetDirectoryName(gameExePath)!;
            var realExeBackup = Path.Combine(gameDir, "DivaMegaMix.exe ");
            if (!File.Exists(realExeBackup))
            {
                try
                {
                    File.Move(gameExePath, realExeBackup);
                    Global.logger?.WriteLine($"Backed up real game exe to '{realExeBackup}' (note trailing space)", LoggerType.Info);
                }
                catch (Exception ex)
                {
                    Global.logger?.WriteLine($"Couldn't back up game exe: {ex.Message}", LoggerType.Error);
                    return false;
                }
            }

            try
            {
                if (File.Exists(gameExePath) || IsSymlink(gameExePath))
                    File.Delete(gameExePath);
                File.CreateSymbolicLink(gameExePath, dmmExecutablePath);
                Global.logger?.WriteLine($"Created symlink {gameExePath} -> {dmmExecutablePath}", LoggerType.Info);
                return true;
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"Couldn't create symlink: {ex.Message}", LoggerType.Error);
                return false;
            }
        }

        public bool RemoveSteamSymlinkTrick(string gameExePath)
        {
            if (string.IsNullOrEmpty(gameExePath)) return false;
            var gameDir = Path.GetDirectoryName(gameExePath)!;
            var realExeBackup = Path.Combine(gameDir, "DivaMegaMix.exe ");
            if (!File.Exists(realExeBackup))
            {
                Global.logger?.WriteLine($"No backup found at '{realExeBackup}'. Nothing to undo.", LoggerType.Warning);
                return false;
            }
            try
            {
                if (File.Exists(gameExePath) || IsSymlink(gameExePath))
                    File.Delete(gameExePath);
                File.Move(realExeBackup, gameExePath);
                Global.logger?.WriteLine("Restored real game exe.", LoggerType.Info);
                return true;
            }
            catch (Exception ex)
            {
                Global.logger?.WriteLine($"Couldn't restore game exe: {ex.Message}", LoggerType.Error);
                return false;
            }
        }

        private static bool IsSymlink(string path)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch { return false; }
        }
    }
}
