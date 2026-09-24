using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
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
            // A bare require kills Discord when patcher.js is missing or throws.
            // Fall back to the original package so the app still opens.
            return "const {join}=require(\"path\");const fs=require(\"fs\");"
                + "const home=process.env.HOME||\"\";"
                + "const appData=process.env.APPDATA||(process.platform===\"darwin\"?join(home,\"Library\",\"Application Support\"):(process.env.XDG_CONFIG_HOME||join(home,\".config\")));"
                + "const patcher=join(appData,\"Endcord\",\"dist\",\"patcher.js\");"
                + "function original(){const asar=join(__dirname,\"..\",\"_app.asar\");const pkg=require(join(asar,\"package.json\"));require(join(asar,pkg.main||\"index.js\"));}"
                + "try{if(!fs.existsSync(patcher))throw new Error(\"missing\");require(patcher);}catch(e){console.error(\"[Endcord]\",e);original();}";
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
            if (!RestorePlistBackup(resourcesDir))
                UpdateMacAsarIntegrity(resourcesDir);
        }

        public static string LastResignError;

        public static bool CanKeepRuntime(string appBundle)
        {
            if (!IsMac() || string.IsNullOrEmpty(appBundle)) return true;
            string entitlements = RunCapture("/usr/bin/codesign", "-d --entitlements - \"" + appBundle + "\"");
            return entitlements.IndexOf("allow-jit", StringComparison.Ordinal) >= 0
                || entitlements.IndexOf("allow-unsigned-executable-memory", StringComparison.Ordinal) >= 0
                || entitlements.IndexOf("disable-library-validation", StringComparison.Ordinal) >= 0;
        }

        // Patch only when we can still put a launchable Discord back.
        // A failed sign rolls the asar and Info.plist back to the bytes we found.
        public static bool ApplyMacPatch(string resourcesDir, string appBundle)
        {
            LastResignError = null;
            if (!CanKeepRuntime(appBundle))
            {
                LastResignError = "Endcord 沒有改這個 Discord。它的執行權限已經不在，再改檔案會讓它開不起來。請刪掉 Discord.app（不要覆蓋），到官網重裝，先確認能打開，先不要按修復。";
                return false;
            }

            Patch(resourcesDir);
            if (Resign(appBundle)) return true;

            Rollback(resourcesDir);
            if (Resign(appBundle))
                LastResignError = "寫入沒有完成，已把 Discord 還原成改動前的檔案。";
            else
                LastResignError = "寫入沒有完成。請刪掉 Discord.app 後到官網重裝，先確認能打開，先不要按修復。";
            return false;
        }

        public static void Rollback(string resourcesDir)
        {
            if (string.IsNullOrEmpty(resourcesDir)) return;
            string appAsar = Path.Combine(resourcesDir, "app.asar");
            string backup = Path.Combine(resourcesDir, "_app.asar");
            try
            {
                if (File.Exists(backup))
                {
                    PrepareWritable(appAsar);
                    if (File.Exists(appAsar)) File.Delete(appAsar);
                    PrepareWritable(backup);
                    File.Move(backup, appAsar);
                }
            }
            catch { }
            RestorePlistBackup(resourcesDir);
        }

        public static bool Resign(string appBundle)
        {
            LastResignError = null;
            if (!IsMac() || string.IsNullOrEmpty(appBundle)) return true;
            if (!CanKeepRuntime(appBundle))
            {
                LastResignError = "Endcord 沒有重新簽名。這個 Discord 的執行權限已經不在，簽下去會讓它開不起來。";
                return false;
            }

            Run("/usr/bin/xattr", "-cr \"" + appBundle + "\"");
            return Run("/usr/bin/codesign", "--force --sign - --preserve-metadata=entitlements,requirements,flags,runtime --deep \"" + appBundle + "\"") == 0;
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
            string appAsar = Path.Combine(resourcesDir, "app.asar");
            string backupAsar = Path.Combine(resourcesDir, "_app.asar");
            if (!File.Exists(appAsar)) return;

            string appHash = HeaderSha256(appAsar);
            string backupHash = File.Exists(backupAsar) ? HeaderSha256(backupAsar) : null;
            if (string.IsNullOrEmpty(appHash)) return;

            foreach (string plistPath in InfoPlists(resourcesDir))
            {
                BackupPlistOnce(plistPath);
                Run("/usr/bin/plutil", "-convert xml1 \"" + plistPath + "\"");
                string text;
                try { text = File.ReadAllText(plistPath); }
                catch { continue; }
                if (text.IndexOf("<plist", StringComparison.Ordinal) < 0 && text.IndexOf("ElectronAsarIntegrity", StringComparison.Ordinal) < 0)
                    continue;

                string updated = SetPlistIntegrityHash(text, "Resources/app.asar", appHash);
                if (!string.IsNullOrEmpty(backupHash))
                    updated = SetPlistIntegrityHash(updated, "Resources/_app.asar", backupHash);
                if (updated != text)
                    File.WriteAllText(plistPath, updated, new UTF8Encoding(false));
            }
        }

        static string PlistBackupPath(string plistPath)
        {
            return plistPath + ".endcord-bak";
        }

        static void BackupPlistOnce(string plistPath)
        {
            string bak = PlistBackupPath(plistPath);
            if (File.Exists(bak) || !File.Exists(plistPath)) return;
            File.Copy(plistPath, bak, false);
        }

        static bool RestorePlistBackup(string resourcesDir)
        {
            bool restored = false;
            foreach (string plistPath in InfoPlists(resourcesDir))
            {
                string bak = PlistBackupPath(plistPath);
                if (!File.Exists(bak)) continue;
                PrepareWritable(plistPath);
                File.Copy(bak, plistPath, true);
                File.Delete(bak);
                restored = true;
            }
            return restored;
        }

        // Only the main Contents/Info.plist. Rewriting helper plists
        // invalidates each helper and makes macOS refuse to open Discord.
        static IEnumerable<string> InfoPlists(string resourcesDir)
        {
            var contents = Directory.GetParent(resourcesDir);
            if (contents == null) yield break;
            string main = Path.Combine(contents.FullName, "Info.plist");
            if (File.Exists(main)) yield return main;
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
            return SetPlistIntegrityHash(plist, "Resources/app.asar", hash);
        }

        public static string SetPlistIntegrityHash(string plist, string relativePath, string hash)
        {
            if (string.IsNullOrEmpty(plist) || string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(hash))
                return plist;

            string keyTag = "<key>" + relativePath + "</key>";
            int key = plist.IndexOf(keyTag, StringComparison.Ordinal);
            if (key >= 0)
            {
                int hashKey = plist.IndexOf("<key>hash</key>", key, StringComparison.Ordinal);
                int start = hashKey >= 0 ? plist.IndexOf("<string>", hashKey, StringComparison.Ordinal) : -1;
                int end = start >= 0 ? plist.IndexOf("</string>", start, StringComparison.Ordinal) : -1;
                if (start >= 0 && end > start)
                    return plist.Substring(0, start + "<string>".Length) + hash + plist.Substring(end);
            }

            string entry =
                "\n\t\t<key>" + relativePath + "</key>\n" +
                "\t\t<dict>\n" +
                "\t\t\t<key>algorithm</key>\n" +
                "\t\t\t<string>SHA256</string>\n" +
                "\t\t\t<key>hash</key>\n" +
                "\t\t\t<string>" + hash + "</string>\n" +
                "\t\t</dict>";
            int integrityKey = plist.IndexOf("<key>ElectronAsarIntegrity</key>", StringComparison.Ordinal);
            if (integrityKey >= 0)
            {
                int dict = plist.IndexOf("<dict>", integrityKey, StringComparison.Ordinal);
                if (dict >= 0)
                {
                    int insertAt = dict + "<dict>".Length;
                    return plist.Substring(0, insertAt) + entry + plist.Substring(insertAt);
                }
            }

            string block =
                "\t<key>ElectronAsarIntegrity</key>\n" +
                "\t<dict>" + entry + "\n" +
                "\t</dict>\n";
            int close = plist.LastIndexOf("</dict>", StringComparison.Ordinal);
            if (close < 0) return plist;
            return plist.Substring(0, close) + block + plist.Substring(close);
        }

        static void DisableAsarIntegrityFuse(string appBundle)
        {
            string framework = Path.Combine(appBundle, "Contents", "Frameworks", "Electron Framework.framework", "Electron Framework");
            if (!File.Exists(framework)) return;

            byte[] sentinel = Encoding.ASCII.GetBytes("dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX");
            byte[] data;
            try { data = File.ReadAllBytes(framework); }
            catch { return; }

            bool changed = false;
            int from = 0;
            while (from < data.Length)
            {
                int at = IndexOfBytes(data, sentinel, from);
                if (at < 0) break;
                int wire = at + sentinel.Length;
                if (wire + 2 < data.Length)
                {
                    int length = data[wire + 1];
                    int fuse = wire + 2 + 4;
                    if (length > 4 && fuse < data.Length && data[fuse] == (byte)'1')
                    {
                        data[fuse] = (byte)'0';
                        changed = true;
                    }
                }
                from = at + sentinel.Length;
            }

            if (!changed) return;
            try { File.WriteAllBytes(framework, data); }
            catch { }
        }

        static int IndexOfBytes(byte[] data, byte[] needle, int start)
        {
            for (int i = start; i <= data.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        static string RunCapture(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                var p = Process.Start(psi);
                if (p == null) return "";
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
                return stdout + stderr;
            }
            catch { return ""; }
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

        public static string ReadOwnInstallerVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith("version.json", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = asm.GetManifestResourceStream(name))
                    using (var reader = new StreamReader(stream))
                    {
                        string version = ReadVersionText(reader.ReadToEnd());
                        if (!string.IsNullOrEmpty(version)) return version;
                    }
                }
            }
            catch { }

            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var dir = new DirectoryInfo(Path.GetDirectoryName(exe));
                while (dir != null)
                {
                    if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    {
                        string plist = Path.Combine(dir.FullName, "Contents", "Info.plist");
                        if (!File.Exists(plist)) break;
                        var match = Regex.Match(File.ReadAllText(plist), "<key>CFBundleShortVersionString</key>\\s*<string>([^<]+)</string>");
                        if (match.Success) return match.Groups[1].Value;
                        break;
                    }
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }

        public static void CheckForUpdate(Action<string> log)
        {
            string local = ReadOwnInstallerVersion();
            string remote = FetchLatestVersion();
            if (string.IsNullOrEmpty(remote))
            {
                if (log != null) log("無法檢查更新。請確認可以連上 GitHub。");
                return;
            }
            if (!string.IsNullOrEmpty(local) && local == remote)
            {
                if (log != null) log("安裝程式已是最新版本（" + remote + "）。");
                return;
            }
            if (log != null)
            {
                log(string.IsNullOrEmpty(local)
                    ? "GitHub 最新版是 " + remote + "，正在下載安裝程式..."
                    : "發現新版本 " + remote + "（這個安裝程式是 " + local + "），正在下載安裝程式...");
            }
            string error = DownloadInstallerAndRelaunch();
            if (error == null)
            {
                if (log != null) log("正在關閉這個安裝程式，並開啟新版本。");
                Environment.Exit(0);
                return;
            }
            if (log != null) log(error);
        }

        static string DownloadInstallerAndRelaunch()
        {
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(5);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("EndcordInstaller");
                    string meta = HttpGet(http, "https://api.github.com/repos/" + GitHubRepo + "/commits/main");
                    var shaMatch = Regex.Match(meta ?? "", "\"sha\"\\s*:\\s*\"([0-9a-f]{40})\"");
                    if (!shaMatch.Success) return "下載失敗。";
                    string sha = shaMatch.Groups[1].Value;
                    if (IsMac()) return DownloadMacAppAndRelaunch(http, sha);
                    return DownloadWindowsExeAndRelaunch(http, sha);
                }
            }
            catch
            {
                return "下載失敗。";
            }
        }

        static string DownloadWindowsExeAndRelaunch(HttpClient http, string sha)
        {
            string current = Process.GetCurrentProcess().MainModule.FileName;
            if (string.IsNullOrEmpty(current) || !current.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return "找不到正在執行的 EndcordInstaller.exe。";
            byte[] bytes = HttpGetBytes(http, "https://raw.githubusercontent.com/" + GitHubRepo + "/" + sha + "/EndcordInstaller.exe");
            if (bytes == null || bytes.Length < 1000000 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
                return "下載到的安裝程式不完整。";
            string downloaded = current + ".new";
            File.WriteAllBytes(downloaded, bytes);
            ScheduleWindowsRelaunch(current, downloaded);
            return null;
        }

        static void ScheduleWindowsRelaunch(string currentExe, string downloadedExe)
        {
            int pid = Process.GetCurrentProcess().Id;
            string scriptPath = Path.Combine(Path.GetTempPath(), "endcord-installer-update.ps1");
            string script = "$target = " + pid + "\r\n"
                + "Wait-Process -Id $target -ErrorAction SilentlyContinue\r\n"
                + "Start-Sleep -Milliseconds 600\r\n"
                + "Copy-Item -LiteralPath " + PsQuote(downloadedExe) + " -Destination " + PsQuote(currentExe) + " -Force\r\n"
                + "Start-Process -FilePath " + PsQuote(currentExe) + "\r\n"
                + "Remove-Item -LiteralPath " + PsQuote(downloadedExe) + " -Force -ErrorAction SilentlyContinue\r\n";
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"");
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        static string PsQuote(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        static string DownloadMacAppAndRelaunch(HttpClient http, string sha)
        {
            string app = CurrentMacAppBundle();
            if (string.IsNullOrEmpty(app))
                return "這個安裝程式不在 .app 裡，無法自己換掉。請到 GitHub 重新下載。";
            string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            byte[] bytes = HttpGetBytes(http, "https://raw.githubusercontent.com/" + GitHubRepo + "/" + sha + "/publish/EndcordInstaller-" + arch + ".zip");
            if (bytes == null || bytes.Length < 1000000 || bytes[0] != (byte)'P' || bytes[1] != (byte)'K')
                return "下載到的 Mac 安裝程式不完整。";
            string tempRoot = Path.Combine(Path.GetTempPath(), "endcord-installer-" + arch);
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            Directory.CreateDirectory(tempRoot);
            string zipPath = Path.Combine(tempRoot, "installer.zip");
            File.WriteAllBytes(zipPath, bytes);
            ZipFile.ExtractToDirectory(zipPath, tempRoot);
            string newApp = null;
            foreach (string dir in Directory.GetDirectories(tempRoot, "*.app", SearchOption.AllDirectories))
            {
                newApp = dir;
                break;
            }
            if (string.IsNullOrEmpty(newApp)) return "下載的壓縮檔裡沒有安裝程式。";
            ScheduleMacRelaunch(app, newApp);
            return null;
        }

        static string CurrentMacAppBundle()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var dir = new DirectoryInfo(Path.GetDirectoryName(exe));
                while (dir != null)
                {
                    if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }

        static void ScheduleMacRelaunch(string appPath, string newAppPath)
        {
            int pid = Process.GetCurrentProcess().Id;
            string scriptPath = "/tmp/endcord-installer-update.sh";
            string script = "#!/bin/bash\n"
                + "while kill -0 " + pid + " >/dev/null 2>&1; do sleep 0.2; done\n"
                + "sleep 0.4\n"
                + "rm -rf " + ShQuote(appPath) + "\n"
                + "ditto " + ShQuote(newAppPath) + " " + ShQuote(appPath) + "\n"
                + "chmod -R u+x " + ShQuote(Path.Combine(appPath, "Contents", "MacOS")) + "\n"
                + "open " + ShQuote(appPath) + "\n";
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
            Run("/bin/chmod", "+x \"" + scriptPath + "\"");
            Process.Start(new ProcessStartInfo("/bin/bash", "\"" + scriptPath + "\""));
        }

        static string ShQuote(string value)
        {
            return "'" + (value ?? "").Replace("'", "'\\''") + "'";
        }

        static byte[] HttpGetBytes(HttpClient http, string url)
        {
            var response = http.GetAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;
            return response.Content.ReadAsByteArrayAsync().ConfigureAwait(false).GetAwaiter().GetResult();
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

    public partial class DiscordClient
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
            try
            {
                if (IsMacBundle)
                    return MacSupport.IsPatched(ResourcesPath);
                if (string.IsNullOrEmpty(RootPath) || !Directory.Exists(RootPath)) return false;
                foreach (var appVerDir in Directory.GetDirectories(RootPath, "app-*"))
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
    }
}
