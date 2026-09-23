using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

[assembly: AssemblyTitle("Endcord Installer")]
[assembly: AssemblyDescription("Installer, uninstaller and repair utility for Endcord on Windows and macOS.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Endcord Inc.")]
[assembly: AssemblyProduct("Endcord")]
[assembly: AssemblyCopyright("Copyright © 2026 Endcord")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("4.0.0.0")]
[assembly: AssemblyFileVersion("4.0.0.0")]

namespace EndcordInstaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // ═══════════════════════════════ NATIVE WIN32 KERNEL ENGINE ══════════════════════════════
    static class Win32Kernel
    {
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint PROCESS_TERMINATE     = 0x0001;
        public const uint TH32CS_SNAPPROCESS    = 0x00000002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint dwFlags;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags2;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool DeleteFile(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool RemoveDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetFileAttributes(string lpFileName, uint dwFileAttributes);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CopyFile(string lpExistingFileName, string lpNewFileName, bool bFailIfExists);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        public static void ForceKillProcessByName(string processName)
        {
            IntPtr hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (hSnap == IntPtr.Zero || hSnap == (IntPtr)(-1)) return;

            PROCESSENTRY32 pe = new PROCESSENTRY32();
            pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

            if (Process32First(hSnap, ref pe))
            {
                do
                {
                    if (string.Equals(pe.szExeFile, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        IntPtr hProc = OpenProcess(PROCESS_TERMINATE, false, pe.th32ProcessID);
                        if (hProc != IntPtr.Zero)
                        {
                            TerminateProcess(hProc, 0);
                            CloseHandle(hProc);
                        }
                    }
                } while (Process32Next(hSnap, ref pe));
            }
            CloseHandle(hSnap);
        }

        public static void DirectWin32DeleteDir(string path)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    SetFileAttributes(file, FILE_ATTRIBUTE_NORMAL);
                    DeleteFile(file);
                }
            }
            catch { }

            try
            {
                foreach (string dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
                {
                    RemoveDirectory(dir);
                }
            }
            catch { }

            RemoveDirectory(path);
        }
    }

    // ═══════════════════════════════ COLOR PALETTE & DESIGN SYSTEM ══════════════════════════════
    static class C
    {
        public static readonly Color Bg          = Color.FromArgb(10, 11, 18);
        public static readonly Color Sidebar     = Color.FromArgb(16, 17, 28);
        public static readonly Color Card        = Color.FromArgb(23, 25, 42);
        public static readonly Color CardHov     = Color.FromArgb(31, 33, 56);
        public static readonly Color CardSel     = Color.FromArgb(28, 32, 68);
        public static readonly Color Accent      = Color.FromArgb(99, 102, 241);      // Indigo Accent
        public static readonly Color AccentLight = Color.FromArgb(129, 140, 248);
        public static readonly Color AccentLo    = Color.FromArgb(55, 58, 140);
        public static readonly Color Green       = Color.FromArgb(16, 185, 129);      // Emerald Green
        public static readonly Color GreenBg     = Color.FromArgb(25, 16, 185, 129);
        public static readonly Color Red         = Color.FromArgb(239, 68, 68);
        public static readonly Color Amber       = Color.FromArgb(245, 158, 11);
        public static readonly Color AmberBg     = Color.FromArgb(25, 245, 158, 11);
        public static readonly Color Blue        = Color.FromArgb(59, 130, 246);
        public static readonly Color Text        = Color.FromArgb(243, 244, 246);
        public static readonly Color TextDim     = Color.FromArgb(156, 163, 175);
        public static readonly Color TextDark    = Color.FromArgb(75, 85, 99);
        public static readonly Color Border      = Color.FromArgb(31, 41, 55);
        public static readonly Color BorderLight = Color.FromArgb(55, 65, 81);
    }

    static class F
    {
        public static readonly Font LargeTitle = new Font("Segoe UI", 22, FontStyle.Bold);
        public static readonly Font Title      = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
        public static readonly Font Subtitle   = new Font("Segoe UI", 10f, FontStyle.Regular);
        public static readonly Font Code       = new Font("Consolas", 8.5f, FontStyle.Regular);
        public static readonly Font TabText    = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        public static readonly Font ButtonText = new Font("Segoe UI Semibold", 10, FontStyle.Bold);
        public static readonly Font LabelText  = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font MutedText  = new Font("Segoe UI", 7.5f, FontStyle.Regular);
    }

    // ═══════════════════════════════ GRAPHICS DRAWING HELPERS ═══════════════════════════════
    static class Gfx
    {
        public static GraphicsPath RoundRect(Rectangle r, int rad)
        {
            var path = new GraphicsPath();
            int d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRoundRect(Graphics g, Rectangle r, int rad, Color color)
        {
            using (var path = RoundRect(r, rad))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void DrawRoundRect(Graphics g, Rectangle r, int rad, Color color, float width)
        {
            using (var path = RoundRect(r, rad))
            using (var pen = new Pen(color, width))
                g.DrawPath(pen, path);
        }

        public static void FillGradientRoundRect(Graphics g, Rectangle r, int rad, Color c1, Color c2, float angle)
        {
            using (var path = RoundRect(r, rad))
            using (var brush = new LinearGradientBrush(r, c1, c2, angle))
                g.FillPath(brush, path);
        }
    }

    // ═══════════════════════════════ MACOS DISCORD APP BUNDLES ═══════════════════════════════
    static class MacSupport
    {
        public static bool IsMac()
        {
            return Environment.OSVersion.Platform != PlatformID.Win32NT
                && Directory.Exists("/System/Library")
                && Directory.Exists("/Applications");
        }

        public static string GetDistPath()
        {
            if (IsMac())
            {
                string home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrEmpty(home))
                    home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", "Endcord", "dist");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Endcord", "dist");
        }

        public static string PatcherJsForAsar()
        {
            return "const {join}=require(\"path\");"
                + "const home=process.env.HOME||\"\";"
                + "const appData=process.env.APPDATA||(process.platform===\"darwin\"?join(home,\"Library\",\"Application Support\"):(process.env.XDG_CONFIG_HOME||join(home,\".config\")));"
                + "require(join(appData,\"Endcord\",\"dist\",\"patcher.js\"));";
        }

        // Windows Discord keeps a loose discord_desktop_core module. Requiring
        // the patcher from there makes startup recurse or throw, and the splash
        // stays on "Starting...". Put the original index.js back before the asar swap.
        public static void RestoreLooseCore(string appVersionDir)
        {
            if (string.IsNullOrEmpty(appVersionDir)) return;
            string modulesDir = Path.Combine(appVersionDir, "modules");
            if (!Directory.Exists(modulesDir)) return;

            foreach (string dir in Directory.GetDirectories(modulesDir, "discord_desktop_core-*"))
            {
                string indexJs = Path.Combine(dir, "discord_desktop_core", "index.js");
                if (!File.Exists(indexJs)) continue;

                string bak = indexJs + ".bak";
                if (File.Exists(bak))
                {
                    PrepareWritable(indexJs);
                    File.Copy(bak, indexJs, true);
                    try { File.Delete(bak); } catch { }
                    continue;
                }

                string content = File.ReadAllText(indexJs);
                if (!content.Contains("Endcord") && !content.Contains("patcher.js")) continue;

                var kept = new System.Collections.Generic.List<string>();
                foreach (string line in content.Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.Contains("Endcord") || line.Contains("patcher.js")) continue;
                    kept.Add(line);
                }
                PrepareWritable(indexJs);
                File.WriteAllText(indexJs, string.Join("\n", kept));
            }
        }

        public static bool IsPatched(string resourcesDir)
        {
            if (string.IsNullOrEmpty(resourcesDir)) return false;
            string appAsar = Path.Combine(resourcesDir, "app.asar");
            string backup = Path.Combine(resourcesDir, "_app.asar");
            if (!File.Exists(backup) || Directory.Exists(backup)) return false;
            if (Directory.Exists(appAsar))
            {
                string idx = Path.Combine(appAsar, "index.js");
                return File.Exists(idx) && File.ReadAllText(idx).Contains("Endcord");
            }
            if (!File.Exists(appAsar)) return false;
            return FileContains(appAsar, "Endcord", 262144);
        }

        public static void Patch(string resourcesDir)
        {
            var parent = Directory.GetParent(resourcesDir);
            if (parent != null)
                RestoreLooseCore(parent.FullName);

            if (IsPatched(resourcesDir))
                Unpatch(resourcesDir);

            string appAsar = Path.Combine(resourcesDir, "app.asar");
            string backup = Path.Combine(resourcesDir, "_app.asar");
            RemoveOurAppFolder(Path.Combine(resourcesDir, "app"));

            if (Directory.Exists(appAsar))
                Directory.Delete(appAsar, true);

            if (!File.Exists(appAsar))
                throw new Exception("app.asar not found in " + resourcesDir);

            if (!File.Exists(backup))
            {
                PrepareWritable(appAsar);
                File.Move(appAsar, backup);
            }
            else
            {
                long currentLen = new FileInfo(appAsar).Length;
                long backupLen = new FileInfo(backup).Length;
                bool currentLooksOriginal = currentLen > 1024 * 1024 && !FileContains(appAsar, "Endcord", 262144);
                if (currentLooksOriginal && backupLen < currentLen)
                {
                    PrepareWritable(backup);
                    File.Delete(backup);
                    PrepareWritable(appAsar);
                    File.Move(appAsar, backup);
                }
                else
                {
                    PrepareWritable(appAsar);
                    File.Delete(appAsar);
                }
            }

            try
            {
                WriteAppAsar(appAsar, PatcherJsForAsar());
            }
            catch
            {
                if (!File.Exists(appAsar) && File.Exists(backup))
                    File.Copy(backup, appAsar, true);
                throw;
            }
        }

        public static void Unpatch(string resourcesDir)
        {
            var parent = Directory.GetParent(resourcesDir);
            if (parent != null)
                RestoreLooseCore(parent.FullName);

            string appAsar = Path.Combine(resourcesDir, "app.asar");
            string backup = Path.Combine(resourcesDir, "_app.asar");
            string tmp = Path.Combine(resourcesDir, "app.asar.endcord-tmp");
            RemoveOurAppFolder(Path.Combine(resourcesDir, "app"));

            if (!File.Exists(backup))
            {
                if (Directory.Exists(appAsar) || (File.Exists(appAsar) && FileContains(appAsar, "Endcord", 262144)))
                    throw new Exception("Backup _app.asar not found; cannot restore original Discord");
                return;
            }

            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            else if (File.Exists(tmp)) File.Delete(tmp);

            if (Directory.Exists(appAsar))
                Directory.Move(appAsar, tmp);
            else if (File.Exists(appAsar))
            {
                PrepareWritable(appAsar);
                File.Move(appAsar, tmp);
            }

            try
            {
                PrepareWritable(backup);
                File.Move(backup, appAsar);
            }
            catch
            {
                if (!File.Exists(appAsar) && !Directory.Exists(appAsar))
                {
                    if (Directory.Exists(tmp)) Directory.Move(tmp, appAsar);
                    else if (File.Exists(tmp)) File.Move(tmp, appAsar);
                }
                throw;
            }

            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            else if (File.Exists(tmp)) File.Delete(tmp);
        }

        public static bool Resign(string appBundle)
        {
            if (!IsMac() || string.IsNullOrEmpty(appBundle)) return true;
            Run("xattr", "-cr \"" + appBundle + "\"");
            return Run("codesign", "--force --deep --sign - \"" + appBundle + "\"") == 0;
        }

        public static string ReadVersion(string appBundle)
        {
            try
            {
                string plist = Path.Combine(appBundle, "Contents", "Info.plist");
                if (!File.Exists(plist)) return "macOS";
                string text = File.ReadAllText(plist);
                var match = Regex.Match(text, "<key>CFBundleShortVersionString</key>\\s*<string>([^<]+)</string>");
                if (match.Success) return match.Groups[1].Value.Trim();
            }
            catch { }
            return "macOS";
        }

        public static void KillProcessTree(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return;
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string n = proc.ProcessName ?? "";
                    if (string.Equals(n, processName, StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith(processName + " Helper", StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }

        public static DiscordClient ParseApp(string appBundle, string name, string processName)
        {
            if (string.IsNullOrEmpty(appBundle) || !Directory.Exists(appBundle)) return null;
            string res = Path.Combine(appBundle, "Contents", "Resources");
            string asar = Path.Combine(res, "app.asar");
            string backup = Path.Combine(res, "_app.asar");
            if (!Directory.Exists(res)) return null;
            if (!File.Exists(asar) && !Directory.Exists(asar) && !File.Exists(backup)) return null;

            return new DiscordClient
            {
                Name = name,
                RootPath = appBundle,
                AppPath = appBundle,
                ResourcesPath = res,
                ExeName = processName,
                IsMacBundle = true,
                VersionLabel = ReadVersion(appBundle)
            };
        }

        public static DiscordClient ParseAppFromUserPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string bundle = path;
            if (!bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                var dir = new DirectoryInfo(path);
                while (dir != null && !dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    dir = dir.Parent;
                if (dir == null) return null;
                bundle = dir.FullName;
            }

            string name = Path.GetFileNameWithoutExtension(bundle);
            return ParseApp(bundle, name, name);
        }

        public static List<DiscordClient> FindApps()
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home))
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string[] bases = new string[] { "/Applications", Path.Combine(home, "Applications") };
            string[][] specs = new string[][] {
                new string[] { "Discord Stable", "Discord.app", "Discord" },
                new string[] { "Discord Canary", "Discord Canary.app", "Discord Canary" },
                new string[] { "Discord PTB", "Discord PTB.app", "Discord PTB" },
                new string[] { "Discord Dev", "Discord Development.app", "Discord Development" }
            };

            var list = new List<DiscordClient>();
            foreach (var spec in specs)
            {
                foreach (var baseDir in bases)
                {
                    var client = ParseApp(Path.Combine(baseDir, spec[1]), spec[0], spec[2]);
                    if (client != null)
                    {
                        list.Add(client);
                        break;
                    }
                }
            }
            return list;
        }

        public static void WriteAppAsar(string outFile, string indexJs)
        {
            string packageJson = "{\n \"name\": \"discord\",\n \"main\": \"index.js\"\n}";
            int indexLen = Encoding.UTF8.GetByteCount(indexJs);
            int pkgLen = Encoding.UTF8.GetByteCount(packageJson);
            string header = "{\"files\":{\"index.js\":{\"size\":" + indexLen
                + ",\"offset\":\"0\"},\"package.json\":{\"size\":" + pkgLen
                + ",\"offset\":\"" + indexLen + "\"}}}";

            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            uint headerStringSize = (uint)headerBytes.Length;
            uint dataSize = 4;
            uint alignedSize = (headerStringSize + dataSize - 1) & ~(dataSize - 1);
            uint headerSize = alignedSize + 8;
            uint headerObjectSize = alignedSize + dataSize;
            int diff = (int)(alignedSize - headerStringSize);

            using (var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(unchecked((int)dataSize));
                bw.Write(unchecked((int)headerSize));
                bw.Write(unchecked((int)headerObjectSize));
                bw.Write(unchecked((int)headerStringSize));
                bw.Write(headerBytes);
                if (diff > 0)
                    bw.Write(Encoding.ASCII.GetBytes(new string('0', diff)));
                bw.Write(Encoding.UTF8.GetBytes(indexJs));
                bw.Write(Encoding.UTF8.GetBytes(packageJson));
            }
        }

        static void RemoveOurAppFolder(string appDir)
        {
            if (!Directory.Exists(appDir)) return;
            string idx = Path.Combine(appDir, "index.js");
            if (!File.Exists(idx)) return;
            string text = File.ReadAllText(idx);
            if (text.Contains("Endcord") || text.Contains("patcher.js"))
                Directory.Delete(appDir, true);
        }

        static bool FileContains(string path, string needle, int max)
        {
            try
            {
                var info = new FileInfo(path);
                int n = (int)Math.Min(info.Length, (long)max);
                if (n <= 0) return false;
                byte[] buf = new byte[n];
                using (var fs = File.OpenRead(path))
                {
                    int read = 0;
                    while (read < n)
                    {
                        int r = fs.Read(buf, read, n - read);
                        if (r <= 0) break;
                        read += r;
                    }
                }
                return Encoding.UTF8.GetString(buf).Contains(needle);
            }
            catch { return false; }
        }

        static void PrepareWritable(string path)
        {
            try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
        }

        static int Run(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                var p = Process.Start(psi);
                if (p == null) return -1;
                p.WaitForExit(30000);
                return p.ExitCode;
            }
            catch { return -1; }
        }
    }

    // ═══════════════════════════════ DISCORD CLIENT MODEL ═══════════════════════════════
    class DiscordClient
    {
        public string Name          { get; set; }
        public string RootPath      { get; set; }
        public string AppPath       { get; set; }
        public string ResourcesPath { get; set; }
        public string ExeName       { get; set; }
        public bool IsMacBundle     { get; set; }
        public string VersionLabel  { get; set; }

        public bool IsInjected()
        {
            try
            {
                if (IsMacBundle)
                    return MacSupport.IsPatched(ResourcesPath);
                if (!Directory.Exists(RootPath)) return false;
                var appDirs = Directory.GetDirectories(RootPath, "app-*");
                foreach (var appVerDir in appDirs)
                {
                    string res = Path.Combine(appVerDir, "resources");
                    if (MacSupport.IsPatched(res)) return true;

                    string modulesDir = Path.Combine(appVerDir, "modules");
                    if (!Directory.Exists(modulesDir)) continue;
                    foreach (string dir in Directory.GetDirectories(modulesDir, "discord_desktop_core-*"))
                    {
                        string indexJs = Path.Combine(dir, "discord_desktop_core", "index.js");
                        if (File.Exists(indexJs) && File.ReadAllText(indexJs).Contains("Endcord"))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public bool IsRunning()
        {
            string exe = ExeName ?? "Discord.exe";
            string baseName = Path.GetFileNameWithoutExtension(exe);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (string.Equals(p.ProcessName, baseName, StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(p.ProcessName + ".exe", exe, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
            }
            return false;
        }

        public void Kill()
        {
            if (IsMacBundle)
            {
                MacSupport.KillProcessTree(Path.GetFileNameWithoutExtension(ExeName ?? "Discord"));
                return;
            }

            try
            {
                string exe = ExeName ?? "Discord.exe";
                Win32Kernel.ForceKillProcessByName(exe);
                var psi = new ProcessStartInfo("cmd.exe", "/c taskkill /f /im \"" + exe + "\" /t")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(3000);
            }
            catch { }

            try
            {
                string rootLower = RootPath.ToLower();
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        string mainModule = proc.MainModule != null ? proc.MainModule.FileName : null;
                        if (!string.IsNullOrEmpty(mainModule) && mainModule.ToLower().StartsWith(rootLower))
                        {
                            proc.Kill();
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void Launch()
        {
            try
            {
                if (IsMacBundle)
                {
                    var psi = new ProcessStartInfo("open", "\"" + RootPath + "\"");
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    return;
                }

                string exe = Path.Combine(AppPath, ExeName ?? "Discord.exe");
                if (!File.Exists(exe)) exe = Path.Combine(RootPath, ExeName ?? "Discord.exe");
                if (File.Exists(exe)) Process.Start(exe);
            }
            catch { }
        }

        public string Version
        {
            get
            {
                if (!string.IsNullOrEmpty(VersionLabel)) return VersionLabel;
                return Path.GetFileName(AppPath);
            }
        }
    }

    // ═══════════════════════════════ MAIN WINDOW ═══════════════════════════════
    class MainForm : Form
    {
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(int nLeft, int nTop, int nRight, int nBottom, int nWidthEllipse, int nHeightEllipse);

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        static string DistPath
        {
            get { return MacSupport.GetDistPath(); }
        }

        List<DiscordClient> clients = new List<DiscordClient>();
        List<ClientCard>    cards   = new List<ClientCard>();

        Panel clientList;
        RichTextBox logBox;
        CustomProgress progress;
        CustomCheckBox chkRestart;
        Label lblStatus;
        PillButton btnInstall, btnRepair, btnUninstall, btnKill, btnRefresh, btnAddPath;

        static readonly Image LogoImg = GetEmbeddedLogo();

        private static Image GetEmbeddedLogo()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith("app_logo.png", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var stream = assembly.GetManifestResourceStream(name))
                        {
                            if (stream != null) return Image.FromStream(stream);
                        }
                    }
                }
            }
            catch { }

            try
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_logo.png");
                if (File.Exists(localPath)) return Image.FromFile(localPath);
            }
            catch { }

            try
            {
                Bitmap bmp = new Bitmap(28, 28);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Brush b = new LinearGradientBrush(new Rectangle(0, 0, 28, 28), Color.FromArgb(88, 101, 242), Color.FromArgb(114, 137, 218), 45f))
                    {
                        g.FillEllipse(b, 0, 0, 28, 28);
                    }
                    using (Font font = new Font("Segoe UI", 12, FontStyle.Bold))
                    {
                        TextRenderer.DrawText(g, "E", font, new Rectangle(0, 0, 28, 28), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }
                return bmp;
            }
            catch { }

            return null;
        }

        public MainForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }
            SuspendLayout();
            BuildUI();
            ResumeLayout(false);
            RefreshClients();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref dark, 4) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref dark, 4);
            }
            catch { }
        }

        void BuildUI()
        {
            AutoScaleMode   = AutoScaleMode.None;
            Text            = "Endcord Installer";
            ClientSize      = new Size(860, 680);
            MinimumSize     = new Size(760, 620);
            BackColor       = C.Bg;
            ForeColor       = C.Text;
            Font            = F.Subtitle;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition   = FormStartPosition.CenterScreen;

            var header = new Panel { BackColor = C.Bg };
            header.Controls.Add(new Label
            {
                Text = "Endcord",
                Font = F.LargeTitle,
                ForeColor = C.Text,
                AutoSize = true,
                Location = new Point(0, 0)
            });
            header.Controls.Add(new Label
            {
                Text = "Windows 圖形安裝程式",
                Font = new Font("Segoe UI", 11f),
                ForeColor = C.AccentLight,
                AutoSize = true,
                Location = new Point(2, 40)
            });

            var hint = new Label
            {
                Text = "會尋找本機的 Discord、Canary、PTB、Development。",
                Font = F.Subtitle,
                ForeColor = C.TextDim,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var listCard = new DBPanel
            {
                BackColor = Color.FromArgb(16, 17, 28),
                Margin = new Padding(0, 4, 0, 8),
                Padding = new Padding(12, 12, 6, 12)
            };
            listCard.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, listCard.Width - 1, listCard.Height - 1);
                Gfx.DrawRoundRect(e.Graphics, r, 12, C.Border, 1f);
            };
            clientList = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(16, 17, 28)
            };
            clientList.Resize += (s, e) => FitCards();
            listCard.Controls.Add(clientList);

            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                BackColor = C.Bg,
                Margin = new Padding(0, 4, 0, 0),
                Padding = new Padding(0, 6, 0, 0)
            };
            btnInstall = new PillButton("安裝", Color.FromArgb(99, 102, 241));
            btnRepair = new PillButton("修復", Color.FromArgb(79, 70, 229));
            btnUninstall = new PillButton("移除", Color.FromArgb(239, 68, 68));
            btnKill = new PillButton("關閉 Discord", Color.FromArgb(245, 158, 11));
            btnRefresh = new PillButton("重新整理", Color.FromArgb(31, 41, 55));
            btnAddPath = new PillButton("自訂路徑", Color.FromArgb(31, 41, 55));
            btnInstall.Click += (s, e) => StartInstall(false);
            btnRepair.Click += (s, e) => StartInstall(true);
            btnUninstall.Click += (s, e) => StartUninstall();
            btnKill.Click += (s, e) => DoKill();
            btnRefresh.Click += (s, e) => RefreshClients();
            btnAddPath.Click += BtnAddPath_Click;
            actions.Controls.Add(btnInstall);
            actions.Controls.Add(btnRepair);
            actions.Controls.Add(btnUninstall);
            actions.Controls.Add(btnKill);
            actions.Controls.Add(btnRefresh);
            actions.Controls.Add(btnAddPath);

            var logCard = new Panel
            {
                BackColor = Color.FromArgb(18, 20, 31),
                Padding = new Padding(10, 8, 10, 8),
                Margin = new Padding(0, 8, 0, 6)
            };
            logCard.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, logCard.Width - 1, logCard.Height - 1);
                Gfx.DrawRoundRect(e.Graphics, r, 8, C.Border, 1f);
            };
            logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 20, 31),
                ForeColor = Color.FromArgb(209, 213, 219),
                BorderStyle = BorderStyle.None,
                Font = F.Code,
                ReadOnly = true,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            logCard.Controls.Add(logBox);

            var statusRow = new Panel
            {
                BackColor = C.Bg,
                Margin = new Padding(0),
                Height = 28
            };
            lblStatus = new Label
            {
                Text = "就緒",
                Font = F.Subtitle,
                ForeColor = C.TextDim,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Left,
                Width = 280
            };
            progress = new CustomProgress { Dock = DockStyle.Right, Width = 140, Visible = false };
            chkRestart = new CustomCheckBox("完成後重新開啟 Discord")
            {
                Dock = DockStyle.Right,
                Width = 230,
                Checked = true
            };
            statusRow.Controls.Add(lblStatus);
            statusRow.Controls.Add(progress);
            statusRow.Controls.Add(chkRestart);

            Controls.Add(header);
            Controls.Add(hint);
            Controls.Add(listCard);
            Controls.Add(actions);
            Controls.Add(logCard);
            Controls.Add(statusRow);

            void Place()
            {
                int pad = 22;
                int w = Math.Max(200, ClientSize.Width - pad * 2);
                int y = 16;
                header.SetBounds(pad, y, w, 68);
                y += 72;
                hint.SetBounds(pad, y, w, 28);
                y += 34;

                int statusH = 28;
                int logH = 150;
                int btnH = 48;
                int bottom = ClientSize.Height - 14;
                int statusY = bottom - statusH;
                int logY = statusY - 8 - logH;
                int btnY = logY - 8 - btnH;
                int listH = btnY - 8 - y;
                if (listH < 120)
                {
                    listH = 120;
                    btnY = y + listH + 8;
                    logY = btnY + btnH + 8;
                    statusY = logY + logH + 8;
                }
                listCard.SetBounds(pad, y, w, listH);
                actions.SetBounds(pad, btnY, w, btnH);
                logCard.SetBounds(pad, logY, w, logH);
                statusRow.SetBounds(pad, statusY, w, statusH);
            }

            Resize += (s, e) => Place();
            Shown += (s, e) => Place();
            DpiChanged += (s, e) => Place();
            Place();
        }

        List<DiscordClient> SelectedClients()
        {
            var list = new List<DiscordClient>();
            for (int i = 0; i < cards.Count && i < clients.Count; i++)
                if (cards[i].Selected) list.Add(clients[i]);
            return list;
        }

        void FitCards()
        {
            if (clientList == null) return;
            int w = clientList.ClientSize.Width - 8;
            if (w < 240) w = 240;
            int y = 2;
            foreach (var card in cards)
            {
                card.SetBounds(0, y, w, 64);
                y += 72;
            }
        }

        // ── DETECT DISCORD INSTALLATIONS ────────────────────────────
        void RefreshClients()
        {
            clients.Clear();
            cards.Clear();
            clientList.Controls.Clear();

            if (MacSupport.IsMac())
            {
                foreach (var c in MacSupport.FindApps())
                    AddCard(c);
            }
            else
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string[,] paths = {
                    { "Discord", Path.Combine(local, "Discord"), "Discord.exe" },
                    { "Discord Canary", Path.Combine(local, "DiscordCanary"), "DiscordCanary.exe" },
                    { "Discord PTB", Path.Combine(local, "DiscordPTB"), "DiscordPTB.exe" },
                    { "Discord Development", Path.Combine(local, "DiscordDevelopment"), "DiscordDevelopment.exe" }
                };
                for (int i = 0; i < 4; i++)
                {
                    var c = GetClient(paths[i, 0], paths[i, 1], paths[i, 2]);
                    if (c != null) AddCard(c);
                }
            }

            if (clients.Count == 0)
            {
                clientList.Controls.Add(new Label
                {
                    Text = "沒有找到 Discord。",
                    ForeColor = C.Amber,
                    Font = F.Subtitle,
                    AutoSize = true,
                    Location = new Point(8, 12)
                });
                Log("沒有找到 Discord。", C.Amber);
                SetStatus("沒有找到 Discord");
            }
            else
            {
                FitCards();
                Log("找到 " + clients.Count + " 個 Discord。", C.TextDim);
                SetStatus("找到 " + clients.Count + " 個 Discord");
            }
        }

        void AddCard(DiscordClient client)
        {
            clients.Add(client);
            var card = new ClientCard(client) { Selected = true };
            cards.Add(card);
            clientList.Controls.Add(card);
        }

        DiscordClient GetClient(string name, string root, string exe)
        {
            if (!Directory.Exists(root)) return null;
            var dirs = Directory.GetDirectories(root, "app-*");
            if (dirs.Length == 0) return null;

            Array.Sort(dirs, (a, b) =>
            {
                string vaStr = Path.GetFileName(a).Replace("app-", "");
                string vbStr = Path.GetFileName(b).Replace("app-", "");
                Version va, vb;
                if (Version.TryParse(vaStr, out va) && Version.TryParse(vbStr, out vb))
                    return va.CompareTo(vb);
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });

            string latest = dirs[dirs.Length - 1];
            string res = Path.Combine(latest, "resources");
            if (!Directory.Exists(res)) return null;

            return new DiscordClient
            {
                Name = name, RootPath = root,
                AppPath = latest, ResourcesPath = res, ExeName = exe
            };
        }

        bool TryAddClient(string name, string root, string exe)
        {
            var c = GetClient(name, root, exe);
            if (c == null) return false;
            AddCard(c);
            FitCards();
            return true;
        }

        void BtnAddPath_Click(object sender, EventArgs e)
        {
            using (var dlg = new PathDialog())
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                string p = dlg.SelectedPath.Trim();
                if (!Directory.Exists(p)) { Log("找不到這個資料夾：" + p, C.Red); return; }
                if (MacSupport.IsMac() || p.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    var mac = MacSupport.ParseAppFromUserPath(p);
                    if (mac == null) { Log("這個路徑裡沒有 Discord.app：" + p, C.Red); return; }
                    AddCard(mac);
                    FitCards();
                    Log("已加入 " + mac.RootPath, C.Green);
                    SetStatus("找到 " + clients.Count + " 個 Discord");
                    return;
                }
                bool ok = TryAddClient("Discord", p, "Discord.exe");
                if (!ok)
                {
                    var parent = Directory.GetParent(p);
                    if (parent != null) ok = TryAddClient("Discord", parent.FullName, "Discord.exe");
                }
                if (ok)
                {
                    Log("已加入 " + p, C.Green);
                    SetStatus("找到 " + clients.Count + " 個 Discord");
                }
                else Log("這個路徑裡沒有 Discord：" + p, C.Red);
            }
        }

        void DoKill()
        {
            SetBusy(true);
            SetStatus("正在關閉 Discord...");
            new Thread(() =>
            {
                SafeLog("正在關閉 Discord...", C.TextDim);
                KillAllDiscordInstances();
                SafeLog("已關閉 Discord。", C.Green);
                Invoke(new Action(() =>
                {
                    SetBusy(false);
                    RefreshClients();
                    SetStatus("已關閉 Discord");
                }));
            }) { IsBackground = true }.Start();
        }

        void StartInstall(bool repair)
        {
            RunTargets(repair ? "正在修復..." : "正在安裝...", targets => DoInstall(targets, repair), true);
        }

        void StartUninstall()
        {
            RunTargets("正在移除...", targets => DoUninstall(targets), true);
        }

        void RunTargets(string status, Action<List<DiscordClient>> work, bool canRelaunch)
        {
            var targets = SelectedClients();
            if (targets.Count == 0) { Log("請先勾選至少一個 Discord。", C.Amber); return; }

            SetBusy(true);
            progress.Value = 0;
            progress.Visible = true;
            SetStatus(status);
            bool relaunch = canRelaunch && chkRestart.Checked;

            new Thread(() =>
            {
                try
                {
                    SafeLog("正在關閉選取的 Discord...", C.TextDim);
                    KillTargetDiscordClients(targets);
                    Thread.Sleep(800);
                    work(targets);
                    if (relaunch)
                    {
                        foreach (var c in targets)
                        {
                            SafeLog("正在重新開啟 " + c.Name + "...", C.Blue);
                            c.Launch();
                        }
                    }
                }
                catch (Exception ex) { SafeLog("失敗：" + ex.Message, C.Red); }
                finally
                {
                    Invoke(new Action(() =>
                    {
                        progress.Visible = false;
                        SetBusy(false);
                        RefreshClients();
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        static void SafeDeleteDir(string path)
        {
            if (!Directory.Exists(path)) return;
            Win32Kernel.DirectWin32DeleteDir(path);
        }

        static void SafeDeleteFile(string path)
        {
            if (!File.Exists(path)) return;
            Win32Kernel.SetFileAttributes(path, Win32Kernel.FILE_ATTRIBUTE_NORMAL);
            Win32Kernel.DeleteFile(path);
        }

        void DoInstall(List<DiscordClient> targets, bool repair)
        {
            SafeLog(repair ? "正在修復..." : "正在安裝...", C.AccentLight);
            SetProg(5);

            // ── Step 1: Extract dist files ────────────────────────────────────────
            try
            {
                Directory.CreateDirectory(DistPath);
                string[] files = { "patcher.js","patcher.js.map","preload.js","preload.js.map",
                                   "renderer.js","renderer.js.map","renderer.css","renderer.css.map" };
                SafeLog("正在複製 Endcord 檔案...", C.TextDim);
                for (int i = 0; i < files.Length; i++)
                {
                    string dest = Path.Combine(DistPath, files[i]);
                    SafeDeleteFile(dest);
                    ExtractRes(files[i], dest);
                    SetProg(5 + 40 * (i + 1) / files.Length);
                }
            }
            catch (Exception ex) { SafeLog("複製失敗：" + ex.Message, C.Red); return; }

            SafeLog("正在寫入 Discord...", C.TextDim);
            SetProg(48);

            // ── Step 2: Inject into each Discord version ──────────────────────────
            for (int i = 0; i < targets.Count; i++)
            {
                var c = targets[i];
                try
                {
                    c.Kill();
                    Thread.Sleep(600);

                    if (c.IsMacBundle)
                    {
                        MacSupport.Patch(c.ResourcesPath);
                        if (MacSupport.Resign(c.RootPath))
                            SafeLog("已安裝到 " + c.Name + "（" + c.Version + "）", C.Green);
                        else
                            SafeLog("已寫入 " + c.Name + "，但 codesign 失敗，macOS 可能不讓它開啟。", C.Amber);
                        SetProg(48 + 52 * (i + 1) / targets.Count);
                        continue;
                    }

                    var appDirs = Directory.GetDirectories(c.RootPath, "app-*");
                    if (appDirs.Length == 0)
                        SafeLog(c.Name + " 裡沒有 app-* 版本資料夾", C.Red);

                    bool patchedAny = false;
                    foreach (var appVerDir in appDirs)
                    {
                        // Same asar swap as macOS. Injecting discord_desktop_core
                        // requires the patcher while Electron is already inside
                        // app.asar, which throws or recurses and leaves the
                        // splash on "Starting...".
                        string res = Path.Combine(appVerDir, "resources");
                        if (!Directory.Exists(res)) continue;
                        MacSupport.Patch(res);
                        SafeLog("已寫入 " + Path.GetFileName(Path.GetDirectoryName(res)), C.TextDim);
                        patchedAny = true;
                    }

                    if (patchedAny)
                        SafeLog("已安裝到 " + c.Name + "（" + c.Version + "）", C.Green);
                    else
                        SafeLog("找不到可以寫入的位置：" + c.Name, C.Red);
                }
                catch (Exception ex) { SafeLog(c.Name + " 安裝失敗：" + ex.Message, C.Red); }
                SetProg(48 + 52 * (i + 1) / targets.Count);
            }
            SafeLog(repair ? "修復完成。" : "安裝完成。", C.AccentLight);
            SetProg(100);
        }

        void DoUninstall(List<DiscordClient> targets)
        {
            SafeLog("正在移除...", C.AccentLight);
            for (int i = 0; i < targets.Count; i++)
            {
                var c = targets[i];
                try
                {
                    ForceKillClient(c);
                    Thread.Sleep(600);

                    if (c.IsMacBundle)
                    {
                        MacSupport.Unpatch(c.ResourcesPath);
                        if (MacSupport.Resign(c.RootPath))
                            SafeLog("已從 " + c.Name + " 移除", C.Green);
                        else
                            SafeLog("已從 " + c.Name + " 移除，但 codesign 失敗。", C.Amber);
                        SetProg(100 * (i + 1) / targets.Count);
                        continue;
                    }

                    var appDirs = Directory.GetDirectories(c.RootPath, "app-*");
                    foreach (var appVerDir in appDirs)
                    {
                        string res = Path.Combine(appVerDir, "resources");
                        if (!Directory.Exists(res))
                        {
                            MacSupport.RestoreLooseCore(appVerDir);
                            continue;
                        }
                        if (MacSupport.IsPatched(res))
                            MacSupport.Unpatch(res);
                        else
                            MacSupport.RestoreLooseCore(appVerDir);
                    }
                    SafeLog("已從 " + c.Name + " 移除", C.Green);
                }
                catch (Exception ex) { SafeLog(c.Name + " 移除失敗：" + ex.Message, C.Red); }
                SetProg(100 * (i + 1) / targets.Count);
            }

            try
            {
                if (Directory.Exists(DistPath)) SafeDeleteDir(DistPath);
            }
            catch { }

            SafeLog("移除完成。", C.AccentLight);
        }

        static void ForceKillClient(DiscordClient c)
        {
            if (c == null) return;
            if (c.IsMacBundle)
            {
                c.Kill();
                return;
            }
            try
            {
                string exeName = Path.GetFileName(c.ExeName ?? "Discord.exe");
                Win32Kernel.ForceKillProcessByName(exeName);
            }
            catch { }
        }

        static void KillTargetDiscordClients(List<DiscordClient> targets)
        {
            foreach (var c in targets)
            {
                try { ForceKillClient(c); } catch { }
            }
        }

        static void KillAllDiscordInstances()
        {
            if (MacSupport.IsMac())
            {
                MacSupport.KillProcessTree("Discord");
                MacSupport.KillProcessTree("Discord Canary");
                MacSupport.KillProcessTree("Discord PTB");
                MacSupport.KillProcessTree("Discord Development");
                return;
            }

            string[] exes = { "Discord.exe", "DiscordCanary.exe", "DiscordPTB.exe", "DiscordDevelopment.exe", "Update.exe" };
            foreach (var exe in exes)
            {
                try
                {
                    Win32Kernel.ForceKillProcessByName(exe);
                }
                catch { }
            }
        }

        static void ExtractRes(string name, string dest)
        {
            var asm = Assembly.GetExecutingAssembly();
            string match = null;
            foreach (var n in asm.GetManifestResourceNames())
            {
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)
                    || n.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                {
                    match = n;
                    break;
                }
            }
            if (match != null)
            {
                using (var s = asm.GetManifestResourceStream(match))
                using (var f = new FileStream(dest, FileMode.Create))
                    s.CopyTo(f);
                return;
            }

            string[] candidates = {
                Path.Combine(AppContext.BaseDirectory, name),
                Path.Combine(AppContext.BaseDirectory, "dist", name),
                Path.Combine(Directory.GetCurrentDirectory(), "dist", name),
                Path.Combine(Directory.GetCurrentDirectory(), name)
            };
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                File.Copy(candidate, dest, true);
                return;
            }

            throw new Exception("Embedded asset not found: " + name);
        }

        // ── HELPERS ────────────────────────────────────────────────
        void Log(string msg, Color col)
        {
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionColor = col;
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\n");
            logBox.ScrollToCaret();
        }
        void SafeLog(string m, Color c)
        { if (InvokeRequired) Invoke(new Action(() => Log(m, c))); else Log(m, c); }
        void SetProg(int v)
        { if (InvokeRequired) Invoke(new Action(() => progress.Value = v)); else progress.Value = v; }
        void SetStatus(string s)
        { if (InvokeRequired) Invoke(new Action(() => lblStatus.Text = s)); else lblStatus.Text = s; }
        void SetBusy(bool b)
        {
            btnInstall.Enabled = !b;
            btnRepair.Enabled = !b;
            btnUninstall.Enabled = !b;
            btnKill.Enabled = !b;
            btnRefresh.Enabled = !b;
            btnAddPath.Enabled = !b;
            chkRestart.Enabled = !b;
        }
    }

    // ═══════════════════════════════ CUSTOM CONTROLS & RENDERING ═══════════════════════════════
    class DBPanel : Panel
    {
        public DBPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }

    class SidebarTab : Control
    {
        bool _active = false;
        bool _hov = false;
        string _title, _desc;

        public SidebarTab(string title, string desc, bool active)
        {
            _title = title; _desc = desc; _active = active;
            Height = 50; Cursor = Cursors.Hand; DoubleBuffered = true;
            MouseEnter += (s, e) => { _hov = true; Invalidate(); };
            MouseLeave += (s, e) => { _hov = false; Invalidate(); };
        }

        public void SetActive(bool a) { _active = a; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_active)
            {
                Gfx.FillRoundRect(g, r, 6, C.CardSel);
                Gfx.DrawRoundRect(g, r, 6, C.Accent, 1.25f);
            }
            else if (_hov)
            {
                Gfx.FillRoundRect(g, r, 6, C.CardHov);
            }

            TextRenderer.DrawText(g, _title, F.TabText, new Rectangle(14, 8, Width - 20, 20),
                _active ? C.Text : (_hov ? C.Text : C.TextDim), TextFormatFlags.Left);

            TextRenderer.DrawText(g, _desc, F.MutedText, new Rectangle(14, 28, Width - 20, 16),
                _active ? C.AccentLight : C.TextDark, TextFormatFlags.Left);
        }
    }

    class ClientCard : Control
    {
        DiscordClient dc;
        bool _sel = false;
        bool _hov = false;

        public bool Selected
        {
            get { return _sel; }
            set { _sel = value; Invalidate(); }
        }

        public ClientCard(DiscordClient client)
        {
            dc = client;
            Height = 64;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            MouseEnter += (s, e) => { _hov = true; Invalidate(); };
            MouseLeave += (s, e) => { _hov = false; Invalidate(); };
            Click += (s, e) => { Selected = !_sel; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = _sel ? C.CardSel : (_hov ? C.CardHov : C.Card);
            Gfx.FillRoundRect(g, r, 8, bg);
            Gfx.DrawRoundRect(g, r, 8, _sel ? Color.FromArgb(90, C.Accent) : C.Border, 1f);

            int cx = 22;
            int cy = Height / 2;
            var chkRect = new Rectangle(cx - 9, cy - 9, 18, 18);
            if (_sel)
            {
                Gfx.FillRoundRect(g, chkRect, 4, C.Accent);
                using (var p = new Pen(Color.White, 2f))
                {
                    g.DrawLine(p, cx - 4, cy, cx - 1, cy + 3);
                    g.DrawLine(p, cx - 1, cy + 3, cx + 5, cy - 4);
                }
            }
            else
            {
                Gfx.DrawRoundRect(g, chkRect, 4, _hov ? C.TextDim : C.TextDark, 1.5f);
            }

            bool injected = dc.IsInjected();
            bool running = dc.IsRunning();
            string state = injected ? "已安裝" : "未安裝";
            if (running) state += " · 執行中";
            string title = dc.Name + "   " + dc.Version + "  ·  " + state;
            int tx = 44;
            TextRenderer.DrawText(g, title, F.Title,
                new Rectangle(tx, 8, Width - tx - 12, 24),
                C.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            string path = dc.IsMacBundle ? dc.RootPath : dc.ResourcesPath;
            TextRenderer.DrawText(g, path, F.MutedText,
                new Rectangle(tx, 34, Width - tx - 12, 20),
                C.TextDim, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }
    }

    class PillButton : Control
    {
        readonly Color _fill;
        bool _hov;
        bool _down;

        public PillButton(string text, Color fill)
        {
            Text = text;
            _fill = fill;
            Font = F.ButtonText;
            Height = 36;
            Width = TextRenderer.MeasureText(text, Font).Width + 36;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 8, 0);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MouseEnter += (s, e) => { _hov = true; Invalidate(); };
            MouseLeave += (s, e) => { _hov = false; _down = false; Invalidate(); };
            MouseDown += (s, e) => { _down = true; Invalidate(); };
            MouseUp += (s, e) => { _down = false; Invalidate(); };
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = !Enabled ? C.Border : (_down ? ControlPaint.Dark(_fill) : (_hov ? ControlPaint.Light(_fill, 0.15f) : _fill));
            Gfx.FillRoundRect(g, r, 8, fill);
            TextRenderer.DrawText(g, Text, Font, r, Enabled ? Color.White : C.TextDark,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    class CustomCheckBox : Control
    {
        bool _checked = false;
        bool _hov = false;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set { _checked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        public CustomCheckBox(string text)
        {
            Text = text; Height = 22; Cursor = Cursors.Hand; DoubleBuffered = true;
            MouseEnter += (s, e) => { _hov = true; Invalidate(); };
            MouseLeave += (s, e) => { _hov = false; Invalidate(); };
            Click += (s, e) => Checked = !_checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var chkRect = new Rectangle(0, 3, 16, 16);
            if (_checked)
            {
                Gfx.FillRoundRect(g, chkRect, 4, C.Accent);
                using (var p = new Pen(Color.White, 2f))
                {
                    g.DrawLine(p, 3, 10, 6, 13);
                    g.DrawLine(p, 6, 13, 12, 6);
                }
            }
            else
            {
                Gfx.DrawRoundRect(g, chkRect, 4, _hov ? C.TextDim : C.TextDark, 1.5f);
            }

            TextRenderer.DrawText(g, Text, F.LabelText, new Rectangle(24, 0, Width - 24, Height),
                _hov ? C.Text : C.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    class CustomLink : Control
    {
        bool _hov = false;
        public CustomLink(string text)
        {
            Text = text; Height = 20; Cursor = Cursors.Hand; DoubleBuffered = true;
            MouseEnter += (s, e) => { _hov = true; Invalidate(); };
            MouseLeave += (s, e) => { _hov = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(g, Text, F.Subtitle, new Rectangle(0, 0, Width, Height),
                _hov ? C.AccentLight : C.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    class CustomProgress : Control
    {
        int _val = 0;
        public int Value
        {
            get { return _val; }
            set { _val = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        public CustomProgress() { Height = 8; DoubleBuffered = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, (Height - 6) / 2, Width, 6);
            Gfx.FillRoundRect(g, r, 3, C.Card);

            if (_val > 0)
            {
                int w = (int)(Width * (_val / 100f));
                if (w > 6)
                {
                    var pr = new Rectangle(0, (Height - 6) / 2, w, 6);
                    Gfx.FillGradientRoundRect(g, pr, 3, C.Accent, C.AccentLight, 0f);
                }
            }
        }
    }

    class CustomActionButton : Control
    {
        bool _hov = false;
        bool _down = false;

        public CustomActionButton(string text)
        {
            Text = text; Height = 42; Cursor = Cursors.Hand; DoubleBuffered = true;
            MouseEnter += (s, e) => { _hov = true; Invalidate(); };
            MouseLeave += (s, e) => { _hov = false; _down = false; Invalidate(); };
            MouseDown  += (s, e) => { _down = true; Invalidate(); };
            MouseUp    += (s, e) => { _down = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (!Enabled)
            {
                Gfx.FillRoundRect(g, r, 8, C.Border);
                TextRenderer.DrawText(g, Text, F.ButtonText, r, C.TextDark, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            Color c1 = _down ? C.AccentLo : (_hov ? C.AccentLight : C.Accent);
            Color c2 = _down ? C.Accent : (_hov ? C.Accent : C.AccentLo);

            Gfx.FillGradientRoundRect(g, r, 8, c1, c2, 45f);
            TextRenderer.DrawText(g, Text, F.ButtonText, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    class PathDialog : Form
    {
        public string SelectedPath { get; private set; }
        TextBox txt;

        public PathDialog()
        {
            Text = "自訂 Discord 路徑";
            Size = new Size(480, 160);
            BackColor = C.Sidebar;
            ForeColor = C.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var lbl = new Label { Text = "貼上 Discord 的安裝資料夾：", Left = 20, Top = 16, AutoSize = true, Font = F.Subtitle, ForeColor = C.TextDim };
            txt = new TextBox { Left = 20, Top = 42, Width = 424, Font = F.Subtitle, BackColor = C.Bg, ForeColor = C.Text, BorderStyle = BorderStyle.FixedSingle };

            var btnOk = new Button { Text = "加入", Left = 264, Top = 80, Width = 80, Height = 30, DialogResult = DialogResult.OK, BackColor = C.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            var btnCancel = new Button { Text = "取消", Left = 364, Top = 80, Width = 80, Height = 30, DialogResult = DialogResult.Cancel, BackColor = C.Card, ForeColor = C.Text, FlatStyle = FlatStyle.Flat };

            btnOk.Click += (s, e) => SelectedPath = txt.Text;

            Controls.Add(lbl); Controls.Add(txt); Controls.Add(btnOk); Controls.Add(btnCancel);
        }
    }
}
