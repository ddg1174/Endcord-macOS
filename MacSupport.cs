using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
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

                var kept = new List<string>();
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
                UpdateMacAsarIntegrity(resourcesDir);
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
            UpdateMacAsarIntegrity(resourcesDir);
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
            byte[] indexBytes = Encoding.UTF8.GetBytes(indexJs);
            byte[] pkgBytes = Encoding.UTF8.GetBytes(packageJson);
            string header = "{\"files\":{\"index.js\":{\"size\":" + indexBytes.Length
                + ",\"offset\":\"0\",\"integrity\":" + IntegrityObject(indexBytes)
                + "},\"package.json\":{\"size\":" + pkgBytes.Length
                + ",\"offset\":\"" + indexBytes.Length + "\",\"integrity\":" + IntegrityObject(pkgBytes)
                + "}}}";

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
                bw.Write(indexBytes);
                bw.Write(pkgBytes);
            }
        }

        // Discord on macOS checks this hash at startup. A replaced app.asar
        // whose header hash is not in Info.plist is killed immediately.
        public static void UpdateMacAsarIntegrity(string resourcesDir)
        {
            if (!IsMac() || string.IsNullOrEmpty(resourcesDir)) return;
            var contents = Directory.GetParent(resourcesDir);
            if (contents == null) return;
            string plistPath = Path.Combine(contents.FullName, "Info.plist");
            string asarPath = Path.Combine(resourcesDir, "app.asar");
            if (!File.Exists(plistPath) || !File.Exists(asarPath)) return;

            string hash = HeaderSha256(asarPath);
            if (string.IsNullOrEmpty(hash)) return;

            Run("/usr/bin/plutil", "-convert xml1 \"" + plistPath + "\"");
            string text = File.ReadAllText(plistPath);
            if (text.IndexOf("<plist", StringComparison.Ordinal) < 0)
                return;
            string updated = SetPlistIntegrityHash(text, hash);
            if (updated != text)
                File.WriteAllText(plistPath, updated, new UTF8Encoding(false));
        }

        public static string HeaderSha256(string asarPath)
        {
            string header = ReadAsarHeaderString(asarPath);
            if (string.IsNullOrEmpty(header)) return null;
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(header)));
        }

        public static string SetPlistIntegrityHash(string plist, string hash)
        {
            if (string.IsNullOrEmpty(plist) || string.IsNullOrEmpty(hash)) return plist;
            int key = plist.IndexOf("Resources/app.asar", StringComparison.Ordinal);
            if (key >= 0)
            {
                int hashKey = plist.IndexOf("<key>hash</key>", key, StringComparison.Ordinal);
                int start = hashKey >= 0 ? plist.IndexOf("<string>", hashKey, StringComparison.Ordinal) : -1;
                int end = start >= 0 ? plist.IndexOf("</string>", start, StringComparison.Ordinal) : -1;
                if (start >= 0 && end > start)
                    return plist.Substring(0, start + "<string>".Length) + hash + plist.Substring(end);
            }

            string block =
                "\t<key>ElectronAsarIntegrity</key>\n" +
                "\t<dict>\n" +
                "\t\t<key>Resources/app.asar</key>\n" +
                "\t\t<dict>\n" +
                "\t\t\t<key>algorithm</key>\n" +
                "\t\t\t<string>SHA256</string>\n" +
                "\t\t\t<key>hash</key>\n" +
                "\t\t\t<string>" + hash + "</string>\n" +
                "\t\t</dict>\n" +
                "\t</dict>\n";
            int close = plist.LastIndexOf("</dict>", StringComparison.Ordinal);
            if (close < 0) return plist;
            return plist.Substring(0, close) + block + plist.Substring(close);
        }

        static string ReadAsarHeaderString(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                byte[] head = new byte[16];
                if (fs.Read(head, 0, 16) != 16) return null;
                int stringSize = BitConverter.ToInt32(head, 12);
                if (stringSize <= 0 || stringSize > 32 * 1024 * 1024) return null;
                byte[] body = new byte[stringSize];
                if (fs.Read(body, 0, stringSize) != stringSize) return null;
                return Encoding.UTF8.GetString(body);
            }
        }

        static string IntegrityObject(byte[] content)
        {
            const int blockSize = 4194304;
            var blocks = new List<string>();
            using (var sha = SHA256.Create())
            {
                if (content.Length == 0)
                    blocks.Add(Hex(sha.ComputeHash(Array.Empty<byte>())));
                else
                {
                    for (int i = 0; i < content.Length; i += blockSize)
                    {
                        int n = Math.Min(blockSize, content.Length - i);
                        blocks.Add(Hex(sha.ComputeHash(content, i, n)));
                    }
                }
                string whole = Hex(sha.ComputeHash(content));
                var sb = new StringBuilder();
                sb.Append("{\"algorithm\":\"SHA256\",\"hash\":\"").Append(whole)
                    .Append("\",\"blockSize\":").Append(blockSize).Append(",\"blocks\":[");
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(blocks[i]).Append('"');
                }
                sb.Append("]}");
                return sb.ToString();
            }
        }

        static string Hex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
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

        public const string GitHubRepo = "ddg1174/Endcord-macOS";

        public static readonly string[] DistFileNames = {
            "version.json",
            "patcher.js", "patcher.js.map",
            "preload.js", "preload.js.map",
            "renderer.js", "renderer.js.map",
            "renderer.css", "renderer.css.map"
        };

        public static string ReadInstalledVersion()
        {
            return ReadVersionFile(Path.Combine(GetDistPath(), "version.json"));
        }

        public static string FetchLatestVersion()
        {
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(20);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("EndcordInstaller");
                    string meta = HttpGet(http, "https://api.github.com/repos/" + GitHubRepo + "/commits/main");
                    var shaMatch = Regex.Match(meta ?? "", "\"sha\"\\s*:\\s*\"([0-9a-f]{40})\"");
                    if (!shaMatch.Success) return null;
                    string json = HttpGet(http, "https://raw.githubusercontent.com/" + GitHubRepo + "/" + shaMatch.Groups[1].Value + "/publish/dist/version.json");
                    return ReadVersionText(json);
                }
            }
            catch { return null; }
        }

        public static void CheckForUpdate(Action<string> log)
        {
            string local = ReadInstalledVersion();
            string remote = FetchLatestVersion();
            if (string.IsNullOrEmpty(remote))
            {
                if (log != null) log("無法檢查更新。請確認可以連上 GitHub。");
                return;
            }
            if (local == remote)
            {
                if (log != null) log("已是最新版本（" + remote + "）。");
                return;
            }
            if (log != null)
            {
                log(string.IsNullOrEmpty(local)
                    ? "GitHub 最新版是 " + remote + "，正在下載..."
                    : "發現新版本 " + remote + "（目前是 " + local + "），正在下載...");
            }
            if (TryUpdateDistFromGitHub(GetDistPath(), null))
            {
                if (log != null) log("已更新到 " + remote + "。請重新開啟 Discord。");
            }
            else if (log != null)
            {
                log("下載失敗。");
            }
        }

        static string ReadVersionFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return ReadVersionText(File.ReadAllText(path));
            }
            catch { return null; }
        }

        static string ReadVersionText(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        public static bool TryUpdateDistFromGitHub(string destDir, Action<string> log)
        {
            if (string.IsNullOrEmpty(destDir)) return false;
            try
            {
                Directory.CreateDirectory(destDir);
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(45);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("EndcordInstaller");
                    string meta = HttpGet(http, "https://api.github.com/repos/" + GitHubRepo + "/commits/main");
                    var shaMatch = Regex.Match(meta, "\"sha\"\\s*:\\s*\"([0-9a-f]{40})\"");
                    if (!shaMatch.Success) return false;
                    string sha = shaMatch.Groups[1].Value;

                    var downloaded = new List<KeyValuePair<string, byte[]>>();
                    foreach (string name in DistFileNames)
                    {
                        var response = http.GetAsync("https://raw.githubusercontent.com/" + GitHubRepo + "/" + sha + "/publish/dist/" + name)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                        if (!response.IsSuccessStatusCode)
                        {
                            if (name.EndsWith(".map") || name == "version.json") continue;
                            return false;
                        }
                        byte[] bytes = response.Content.ReadAsByteArrayAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                        if (!DistBytesLookValid(name, bytes))
                        {
                            if (name.EndsWith(".map") || name == "version.json") continue;
                            return false;
                        }
                        downloaded.Add(new KeyValuePair<string, byte[]>(name, bytes));
                    }

                    bool hasPatcher = false;
                    foreach (var item in downloaded)
                        if (item.Key == "patcher.js") hasPatcher = true;
                    if (!hasPatcher) return false;

                    foreach (var item in downloaded)
                    {
                        string dest = Path.Combine(destDir, item.Key);
                        string tmp = dest + ".download";
                        File.WriteAllBytes(tmp, item.Value);
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(tmp, dest);
                    }
                }

                if (log != null) log("已從 GitHub 下載最新版。");
                return true;
            }
            catch
            {
                return false;
            }
        }

        static string HttpGet(HttpClient http, string url)
        {
            return http.GetStringAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        static bool DistBytesLookValid(string name, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16) return false;
            string head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 120)).TrimStart().ToLowerInvariant();
            if (head.StartsWith("<!") || head.StartsWith("<html") || head.StartsWith("not found"))
                return false;
            if (name == "version.json" || name.EndsWith(".map"))
                return head.StartsWith("{");
            if (name.EndsWith(".js"))
                return bytes.Length > 500 && Encoding.UTF8.GetString(bytes).Contains("Endcord");
            return true;
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
            if (IsMacBundle || !string.IsNullOrEmpty(ResourcesPath))
                return MacSupport.IsPatched(ResourcesPath);
            return false;
        }
    }
}
