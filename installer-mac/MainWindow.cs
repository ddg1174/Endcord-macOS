using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using EndcordInstaller;

namespace EndcordInstaller.Mac
{
    class MainWindow : Window
    {
        static readonly string[] DistFiles = MacSupport.DistFileNames;

        readonly StackPanel _clientList = new StackPanel { Spacing = 8 };
        readonly TextBox _log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 160,
            MaxHeight = 220,
            Background = Brush("#12141F"),
            Foreground = Brush("#D1D5DB"),
            BorderBrush = Brush("#1F2937"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10)
        };
        readonly TextBlock _status = new TextBlock
        {
            Foreground = Brush("#9CA3AF"),
            FontSize = 12
        };
        readonly List<CheckBox> _checks = new List<CheckBox>();
        readonly Button _install;
        readonly Button _repair;
        readonly Button _uninstall;
        readonly Button _kill;
        readonly Button _refresh;
        readonly Button _checkUpdate;

        public MainWindow()
        {
            Title = "Endcord Installer";
            Width = 860;
            Height = 680;
            MinWidth = 720;
            MinHeight = 560;
            Background = Brush("#0A0B12");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new Grid
            {
                Margin = new Thickness(22),
                RowDefinitions = Rows("Auto", "Auto", "*", "Auto", "Auto", "Auto")
            };

            var title = new TextBlock
            {
                Text = "Endcord",
                FontSize = 28,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("#F3F4F6")
            };
            var subtitle = new TextBlock
            {
                Text = "macOS 圖形安裝程式",
                FontSize = 13,
                Foreground = Brush("#818CF8"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            var header = new StackPanel();
            header.Children.Add(title);
            header.Children.Add(subtitle);
            if (!MacSupport.IsMac())
            {
                header.Children.Add(new TextBlock
                {
                    Text = "這是給 Mac 用的安裝程式。在 Windows 上只能預覽畫面，偵測不到 Discord.app。",
                    Foreground = Brush("#F59E0B"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var hint = new TextBlock
            {
                Text = "會尋找 /Applications 與個人 Applications 資料夾裡的 Discord、Canary、PTB、Development。",
                Foreground = Brush("#9CA3AF"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 10)
            };
            Grid.SetRow(hint, 1);
            root.Children.Add(hint);

            var scroller = new ScrollViewer
            {
                Content = _clientList,
                Background = Brush("#10111C"),
                Padding = new Thickness(12),
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
            var listCard = new Border
            {
                Child = scroller,
                Background = Brush("#10111C"),
                CornerRadius = new CornerRadius(12),
                BorderBrush = Brush("#1F2937"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(listCard, 2);
            root.Children.Add(listCard);

            var actions = new WrapPanel { Orientation = Orientation.Horizontal };
            _install = ActionButton("安裝", "#6366F1", () => RunInstall(false));
            _repair = ActionButton("修復", "#4F46E5", () => RunInstall(true));
            _uninstall = ActionButton("移除", "#EF4444", RunUninstall);
            _kill = ActionButton("關閉 Discord", "#F59E0B", RunKill);
            _refresh = ActionButton("重新整理", "#1F2937", RefreshClients);
            _checkUpdate = ActionButton("檢查更新", "#10B981", RunCheckUpdate);
            actions.Children.Add(_install);
            actions.Children.Add(_repair);
            actions.Children.Add(_uninstall);
            actions.Children.Add(_kill);
            actions.Children.Add(_refresh);
            actions.Children.Add(_checkUpdate);
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);

            Grid.SetRow(_log, 4);
            _log.Margin = new Thickness(0, 12, 0, 8);
            root.Children.Add(_log);

            Grid.SetRow(_status, 5);
            root.Children.Add(_status);

            Content = root;
            RefreshClients();
        }

        static RowDefinitions Rows(params string[] heights)
        {
            var rows = new RowDefinitions();
            foreach (var height in heights)
                rows.Add(new RowDefinition(GridLength.Parse(height)));
            return rows;
        }

        Button ActionButton(string text, string color, Action click)
        {
            var button = new Button
            {
                Content = text,
                Background = Brush(color),
                Foreground = Brushes.White,
                Padding = new Thickness(16, 8),
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(8),
                FontWeight = FontWeight.SemiBold
            };
            button.Click += (_, __) => click();
            return button;
        }

        void RefreshClients()
        {
            _checks.Clear();
            _clientList.Children.Clear();
            var clients = MacSupport.FindApps();
            if (clients.Count == 0)
            {
                _clientList.Children.Add(new TextBlock
                {
                    Text = "沒有找到 Discord.app。",
                    Foreground = Brush("#F59E0B"),
                    Margin = new Thickness(4)
                });
                SetStatus("沒有找到 Discord");
                Log("沒有在 /Applications 或 ~/Applications 找到 Discord。");
                return;
            }

            foreach (var client in clients)
            {
                bool patched = client.IsInjected();
                var box = new CheckBox
                {
                    IsChecked = true,
                    Tag = client,
                    Content = client.Name + "  " + client.VersionLabel + (patched ? "  ·  已安裝 Endcord" : "  ·  未安裝"),
                    Foreground = Brush("#F3F4F6"),
                    Margin = new Thickness(4, 2)
                };
                _checks.Add(box);
                var card = new Border
                {
                    Background = Brush("#17192A"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8),
                    Child = new StackPanel
                    {
                        Children =
                        {
                            box,
                            new TextBlock
                            {
                                Text = client.RootPath,
                                Foreground = Brush("#6B7280"),
                                FontSize = 12,
                                Margin = new Thickness(28, 0, 0, 0)
                            }
                        }
                    }
                };
                _clientList.Children.Add(card);
            }
            SetStatus("找到 " + clients.Count + " 個 Discord");
            Log("找到 " + clients.Count + " 個 Discord。");
        }

        List<DiscordClient> SelectedClients()
        {
            var list = new List<DiscordClient>();
            foreach (var box in _checks)
            {
                if (box.IsChecked == true && box.Tag is DiscordClient client)
                    list.Add(client);
            }
            return list;
        }

        void RunInstall(bool repair)
        {
            var targets = SelectedClients();
            if (targets.Count == 0)
            {
                Log("請先勾選至少一個 Discord。");
                return;
            }

            RunBusy(repair ? "正在修復..." : "正在安裝...", () =>
            {
                CopyDistFiles();
                foreach (var client in targets)
                {
                    if (!string.IsNullOrEmpty(client.ExeName))
                        MacSupport.KillProcessTree(client.ExeName);
                    MacSupport.Patch(client.ResourcesPath);
                    bool signed = MacSupport.Resign(client.RootPath);
                    Log(signed
                        ? "已安裝到 " + client.Name
                        : (string.IsNullOrEmpty(MacSupport.LastResignError)
                            ? "已寫入 " + client.Name + "，但 codesign 失敗，macOS 可能不讓它開啟。"
                            : MacSupport.LastResignError));
                }
                Log(repair ? "修復完成。" : "安裝完成。");
            });
        }

        void RunUninstall()
        {
            var targets = SelectedClients();
            if (targets.Count == 0)
            {
                Log("請先勾選至少一個 Discord。");
                return;
            }

            RunBusy("正在移除...", () =>
            {
                foreach (var client in targets)
                {
                    if (!string.IsNullOrEmpty(client.ExeName))
                        MacSupport.KillProcessTree(client.ExeName);
                    MacSupport.Unpatch(client.ResourcesPath);
                    bool signed = MacSupport.Resign(client.RootPath);
                    Log(signed
                        ? "已從 " + client.Name + " 移除"
                        : (string.IsNullOrEmpty(MacSupport.LastResignError)
                            ? "已從 " + client.Name + " 移除，但 codesign 失敗。"
                            : MacSupport.LastResignError));
                }
                try
                {
                    string dist = MacSupport.GetDistPath();
                    if (Directory.Exists(dist))
                        Directory.Delete(dist, true);
                }
                catch { }
                Log("移除完成。");
            });
        }

        void RunKill()
        {
            RunBusy("正在關閉 Discord...", () =>
            {
                MacSupport.KillProcessTree("Discord");
                MacSupport.KillProcessTree("Discord Canary");
                MacSupport.KillProcessTree("Discord PTB");
                MacSupport.KillProcessTree("Discord Development");
                Log("已關閉 Discord。");
            });
        }

        void RunBusy(string status, Action work)
        {
            SetBusy(true);
            SetStatus(status);
            Task.Run(() =>
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    Log("失敗：" + ex.Message);
                }
                finally
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        SetBusy(false);
                        RefreshClients();
                        SetStatus("就緒");
                    });
                }
            });
        }

        void CopyDistFiles()
        {
            string destDir = MacSupport.GetDistPath();
            Directory.CreateDirectory(destDir);
            if (MacSupport.TryUpdateDistFromGitHub(destDir, Log))
                return;

            Log("無法連上 GitHub，改用安裝程式裡的版本。");
            foreach (var name in DistFiles)
            {
                string source = FindDistFile(name);
                if (source == null)
                {
                    if (name == "version.json" || name.EndsWith(".map")) continue;
                    throw new Exception("找不到 " + name + "。請把 Endcord 建置出的 dist 資料夾放在這個 App 旁邊。");
                }
                File.Copy(source, Path.Combine(destDir, name), true);
                Log("已複製 " + name);
            }
        }

        static string FindDistFile(string name)
        {
            var candidates = new List<string>();
            AddDistCandidates(candidates, AppContext.BaseDirectory, name);
            AddDistCandidates(candidates, Directory.GetCurrentDirectory(), name);
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string dir = Path.GetDirectoryName(exe);
                AddDistCandidates(candidates, dir, name);
                var contents = Directory.GetParent(dir);
                var bundle = contents != null ? contents.Parent : null;
                var besideApp = bundle != null ? bundle.Parent : null;
                if (besideApp != null)
                    AddDistCandidates(candidates, besideApp.FullName, name);
            }
            catch { }

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        static void AddDistCandidates(List<string> candidates, string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) return;
            candidates.Add(Path.Combine(dir, name));
            candidates.Add(Path.Combine(dir, "dist", name));
        }

        void SetBusy(bool busy)
        {
            _install.IsEnabled = !busy;
            _repair.IsEnabled = !busy;
            _uninstall.IsEnabled = !busy;
            _kill.IsEnabled = !busy;
            _refresh.IsEnabled = !busy;
            _checkUpdate.IsEnabled = !busy;
        }

        void RunCheckUpdate()
        {
            RunBusy("正在檢查更新...", () => MacSupport.CheckForUpdate(Log));
        }

        void SetStatus(string text)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => _status.Text = text);
                return;
            }
            _status.Text = text;
        }

        void Log(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message + "\n";
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => _log.Text += line);
                return;
            }
            _log.Text += line;
        }

        static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
    }
}
