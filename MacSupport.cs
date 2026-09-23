using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace EndcordInstaller
{
    public static class MacSupport
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
                PrepareWritable(appAsar);
                File.Delete(appAsar);
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
                new string[] { "Discord", "Discord.app", "Discord" },
                new string[] { "Discord Canary", "Discord Canary.app", "Discord Canary" },
                new string[] { "Discord PTB", "Discord PTB.app", "Discord PTB" },
                new string[] { "Discord Development", "Discord Development.app", "Discord Development" }
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

    public class DiscordClient
    {
        public string Name { get; set; }
        public string RootPath { get; set; }
        public string AppPath { get; set; }
        public string ResourcesPath { get; set; }
        public string ExeName { get; set; }
        public bool IsMacBundle { get; set; }
        public string VersionLabel { get; set; }
        public bool IsInjected()
        {
            if (IsMacBundle)
                return MacSupport.IsPatched(ResourcesPath);
            string appDir = Path.Combine(ResourcesPath, "app");
            string backupAsar = Path.Combine(ResourcesPath, "_app.asar");
            return Directory.Exists(appDir) && File.Exists(backupAsar);
        }
    }
}
