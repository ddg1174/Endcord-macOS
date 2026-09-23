using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace EndcordInstaller
{
    class Program
    {
        static string EndcordDistPath = MacSupport.GetDistPath();

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================");
            Console.WriteLine("           ENDCORD KURULUM YÖNETİCİSİ             ");
            Console.WriteLine("==================================================");
            Console.ResetColor();

            if (MacSupport.IsMac())
            {
                Console.WriteLine("Platform: macOS");
                Console.WriteLine("Discord: /Applications and ~/Applications");
                Console.WriteLine("Data: " + EndcordDistPath);
                Console.WriteLine();
            }

            if (args != null && args.Length > 0 && args[0] == "--detect")
            {
                var found = DetectDiscordClients();
                Console.WriteLine("COUNT=" + found.Count);
                return;
            }

            if (args != null && args.Length > 0 && args[0] == "--self-test")
            {
                RunMacPatchSelfTest();
                return;
            }

            while (true)
            {
                var clients = DetectDiscordClients();
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Lütfen Yapmak İstediğiniz İşlemi Seçin:");
                Console.ResetColor();
                Console.WriteLine("1. Endcord Kur (Install)");
                Console.WriteLine("2. Endcord Kaldır (Uninstall)");
                Console.WriteLine("3. Endcord Onar (Repair)");
                Console.WriteLine("4. Çalışan Discord Süreçlerini Kapat");
                Console.WriteLine("5. Çıkış");
                Console.Write("\nSeçiminiz (1-5): ");

                string choice = Console.ReadLine();
                if (choice == "5") break;

                switch (choice)
                {
                    case "1":
                        HandleInstall(clients, false);
                        break;
                    case "2":
                        HandleUninstall(clients);
                        break;
                    case "3":
                        HandleInstall(clients, true);
                        break;
                    case "4":
                        KillDiscordProcesses();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Discord süreçleri kapatıldı.");
                        Console.ResetColor();
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Geçersiz seçim!");
                        Console.ResetColor();
                        break;
                }
            }
        }

        static void RunMacPatchSelfTest()
        {
            string root = Path.Combine(Path.GetTempPath(), "endcord-mac-selftest-" + Guid.NewGuid().ToString("N"));
            string bundle = Path.Combine(root, "Discord.app");
            string resources = Path.Combine(bundle, "Contents", "Resources");
            Directory.CreateDirectory(resources);
            File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"),
                "<key>CFBundleShortVersionString</key><string>9.9.9</string>");

            string original = "original-discord-asar-payload";
            string appAsar = Path.Combine(resources, "app.asar");
            File.WriteAllText(appAsar, original);

            var parsed = MacSupport.ParseApp(bundle, "Discord", "Discord");
            if (parsed == null || parsed.VersionLabel != "9.9.9" || !parsed.IsMacBundle)
                throw new Exception("ParseApp failed");

            MacSupport.Patch(resources);
            if (!File.Exists(Path.Combine(resources, "_app.asar")))
                throw new Exception("backup missing");
            if (File.ReadAllText(Path.Combine(resources, "_app.asar")) != original)
                throw new Exception("backup contents changed");
            if (!MacSupport.IsPatched(resources))
                throw new Exception("patched marker missing");

            byte[] patched = File.ReadAllBytes(appAsar);
            if (patched.Length < 16) throw new Exception("asar too small");
            int headerBufLen = BitConverter.ToInt32(patched, 4);
            int headerStringSize = BitConverter.ToInt32(patched, 12);
            string header = Encoding.UTF8.GetString(patched, 16, headerStringSize);
            string body = Encoding.UTF8.GetString(patched, 8 + headerBufLen, patched.Length - (8 + headerBufLen));
            if (!header.Contains("\"index.js\"") || !header.Contains("\"package.json\""))
                throw new Exception("asar header missing files");
            if (!header.Contains("\"integrity\"") || !header.Contains("SHA256"))
                throw new Exception("asar integrity metadata missing");
            string headerHash = MacSupport.HeaderSha256(appAsar);
            if (string.IsNullOrEmpty(headerHash) || headerHash.Length != 64)
                throw new Exception("asar header hash missing");
            string plist = "<plist><dict><key>ElectronAsarIntegrity</key><dict><key>Resources/app.asar</key><dict><key>hash</key><string>abc</string></dict></dict></dict></plist>";
            string updatedPlist = MacSupport.SetPlistIntegrityHash(plist, headerHash);
            if (!updatedPlist.Contains("<string>" + headerHash + "</string>") || updatedPlist.Contains(">abc<"))
                throw new Exception("plist integrity hash was not replaced");
            if (!body.StartsWith("const {join}=require(\"path\");") || !body.Contains("\"Endcord\"") || !body.Contains("\"Application Support\""))
                throw new Exception("asar payload missing");
            if (!body.Contains("\"main\": \"index.js\""))
                throw new Exception("asar package.json missing");

            MacSupport.Patch(resources);
            if (File.ReadAllText(Path.Combine(resources, "_app.asar")) != original)
                throw new Exception("repair overwrote the original backup");

            MacSupport.Unpatch(resources);
            if (File.Exists(Path.Combine(resources, "_app.asar")))
                throw new Exception("backup left behind");
            if (File.ReadAllText(appAsar) != original)
                throw new Exception("original app.asar was not restored");
            if (MacSupport.IsPatched(resources))
                throw new Exception("still marked patched");

            string winRoot = Path.Combine(root, "win-discord", "app-1.0.0");
            string winRes = Path.Combine(winRoot, "resources");
            string coreIndex = Path.Combine(winRoot, "modules", "discord_desktop_core-1", "discord_desktop_core", "index.js");
            Directory.CreateDirectory(Path.GetDirectoryName(coreIndex));
            Directory.CreateDirectory(winRes);
            File.WriteAllText(coreIndex, "require(require('path').join(process.env.APPDATA, 'Endcord', 'dist', 'patcher.js'));\nmodule.exports = require('./core.asar');\n");
            File.WriteAllText(coreIndex + ".bak", "module.exports = require('./core.asar');\n");
            File.WriteAllText(Path.Combine(winRes, "app.asar"), original);

            MacSupport.Patch(winRes);
            string restoredCore = File.ReadAllText(coreIndex);
            if (restoredCore.Contains("Endcord") || restoredCore.Contains("patcher.js"))
                throw new Exception("desktop core injection was left in place");
            if (!restoredCore.Contains("core.asar"))
                throw new Exception("desktop core lost its original export");
            if (File.Exists(coreIndex + ".bak"))
                throw new Exception("desktop core backup was not removed");
            if (!MacSupport.IsPatched(winRes))
                throw new Exception("windows asar swap did not apply");

            Directory.Delete(root, true);
            Console.WriteLine("macOS patch self-test passed");
        }

        static List<DiscordClient> DetectDiscordClients()
        {
            if (MacSupport.IsMac())
                return DetectMacDiscordClients();

            var clients = new List<DiscordClient>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var searchFolders = new Dictionary<string, string>
            {
                { "Discord", Path.Combine(localAppData, "Discord") },
                { "Discord Canary", Path.Combine(localAppData, "DiscordCanary") },
                { "Discord PTB", Path.Combine(localAppData, "DiscordPTB") },
                { "Discord Development", Path.Combine(localAppData, "DiscordDevelopment") }
            };

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\nTespit Edilen Discord Sürümleri:");
            Console.ResetColor();

            int index = 1;
            foreach (var kvp in searchFolders)
            {
                string name = kvp.Key;
                string path = kvp.Value;
                if (Directory.Exists(path))
                {
                    var appDirs = Directory.GetDirectories(path, "app-*");
                    if (appDirs.Length > 0)
                    {
                        Array.Sort(appDirs);
                        string latestAppDir = appDirs[appDirs.Length - 1];
                        string resourcesPath = Path.Combine(latestAppDir, "resources");
                        if (Directory.Exists(resourcesPath))
                        {
                            var client = new DiscordClient
                            {
                                Name = name,
                                RootPath = path,
                                AppPath = latestAppDir,
                                ResourcesPath = resourcesPath
                            };
                            clients.Add(client);
                            string status = client.IsInjected() ? "KURULU" : "KURULU DEĞİL";
                            Console.WriteLine(string.Format("{0}. {1} ({2}) [{3}]", index, client.Name, Path.GetFileName(client.AppPath), status));
                            index++;
                        }
                    }
                }
            }

            if (clients.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Hiçbir Discord sürümü tespit edilemedi!");
                Console.ResetColor();
            }

            return clients;
        }

        static List<DiscordClient> DetectMacDiscordClients()
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\nTespit Edilen Discord Sürümleri:");
            Console.ResetColor();

            var clients = MacSupport.FindApps();
            if (clients.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Hiçbir Discord sürümü tespit edilemedi!");
                Console.WriteLine("Discord.app, /Applications veya ~/Applications içinde olmalı.");
                Console.ResetColor();
                return clients;
            }

            for (int i = 0; i < clients.Count; i++)
            {
                var client = clients[i];
                string status = client.IsInjected() ? "KURULU" : "KURULU DEĞİL";
                Console.WriteLine(string.Format("{0}. {1} ({2}) [{3}]", i + 1, client.Name, client.VersionLabel, status));
                Console.WriteLine("   " + client.RootPath);
            }

            return clients;
        }

        static void HandleInstall(List<DiscordClient> clients, bool isRepair)
        {
            if (clients.Count == 0) return;

            Console.Write("\nHangi Discord sürümüne kurmak istersiniz? (Numara girin veya hepsi için 'H' yazın): ");
            string rawInput = Console.ReadLine();
            string targetInput = rawInput != null ? rawInput.Trim().ToUpper() : "";

            var targets = new List<DiscordClient>();
            if (targetInput == "H")
            {
                targets.AddRange(clients);
            }
            else
            {
                int idx;
                if (int.TryParse(targetInput, out idx) && idx >= 1 && idx <= clients.Count)
                {
                    targets.Add(clients[idx - 1]);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Geçersiz seçim!");
                    Console.ResetColor();
                    return;
                }
            }

            Console.Write("Çalışan Discord süreçlerini otomatik kapatmak ister misiniz? (E/H): ");
            string closeInput = Console.ReadLine();
            if (closeInput != null && closeInput.Trim().ToUpper() == "E")
            {
                KillDiscordProcesses();
            }

            try
            {
                Directory.CreateDirectory(EndcordDistPath);
                string[] filesToExtract = new string[] {
                    "patcher.js", "patcher.js.map",
                    "preload.js", "preload.js.map",
                    "renderer.js", "renderer.js.map",
                    "renderer.css", "renderer.css.map"
                };

                Console.WriteLine("\n[1/2] Endcord dosyaları çıkartılıyor...");
                foreach (var file in filesToExtract)
                {
                    string dest = Path.Combine(EndcordDistPath, file);
                    ExtractResource(file, dest);
                    Console.WriteLine("Çıkartıldı: " + file);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Endcord dosyaları çıkartılırken hata oluştu: " + ex.Message);
                Console.ResetColor();
                return;
            }

            Console.WriteLine("[2/2] Discord'a enjekte ediliyor...");
            foreach (var client in targets)
            {
                try
                {
                    if (client.IsMacBundle)
                    {
                        if (!string.IsNullOrEmpty(client.ExeName))
                            MacSupport.KillProcessTree(client.ExeName);
                        MacSupport.Patch(client.ResourcesPath);
                        bool signed = MacSupport.Resign(client.RootPath);
                        Console.ForegroundColor = signed ? ConsoleColor.Green : ConsoleColor.Yellow;
                        Console.WriteLine(signed
                            ? "Başarıyla kuruldu/onarıldı: " + client.Name
                            : "Kuruldu ama codesign başarısız: " + client.Name);
                        Console.ResetColor();
                        continue;
                    }

                    var appDirs = Directory.GetDirectories(client.RootPath, "app-*");
                    if (appDirs.Length == 0 && Directory.Exists(client.ResourcesPath))
                        MacSupport.Patch(client.ResourcesPath);
                    foreach (var appVerDir in appDirs)
                    {
                        string res = Path.Combine(appVerDir, "resources");
                        if (Directory.Exists(res))
                            MacSupport.Patch(res);
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Başarıyla kuruldu/onarıldı: " + client.Name);
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(client.Name + " enjeksiyonu başarısız oldu: " + ex.Message);
                    Console.WriteLine("Lütfen Discord'un kapalı olduğundan ve yönetici izinlerine sahip olduğunuzdan emin olun.");
                    Console.ResetColor();
                }
            }
        }

        static void HandleUninstall(List<DiscordClient> clients)
        {
            if (clients.Count == 0) return;

            Console.Write("\nHangi Discord sürümünden kaldırmak istersiniz? (Numara girin veya hepsi için 'H' yazın): ");
            string rawInput = Console.ReadLine();
            string targetInput = rawInput != null ? rawInput.Trim().ToUpper() : "";

            var targets = new List<DiscordClient>();
            if (targetInput == "H")
            {
                targets.AddRange(clients);
            }
            else
            {
                int idx;
                if (int.TryParse(targetInput, out idx) && idx >= 1 && idx <= clients.Count)
                {
                    targets.Add(clients[idx - 1]);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Geçersiz seçim!");
                    Console.ResetColor();
                    return;
                }
            }

            Console.Write("Çalışan Discord süreçlerini otomatik kapatmak ister misiniz? (E/H): ");
            string closeInput = Console.ReadLine();
            if (closeInput != null && closeInput.Trim().ToUpper() == "E")
            {
                KillDiscordProcesses();
            }

            foreach (var client in targets)
            {
                try
                {
                    if (client.IsMacBundle)
                    {
                        if (!string.IsNullOrEmpty(client.ExeName))
                            MacSupport.KillProcessTree(client.ExeName);
                        MacSupport.Unpatch(client.ResourcesPath);
                        bool signed = MacSupport.Resign(client.RootPath);
                        Console.ForegroundColor = signed ? ConsoleColor.Green : ConsoleColor.Yellow;
                        Console.WriteLine(signed
                            ? "Başarıyla kaldırıldı: " + client.Name
                            : "Kaldırıldı ama codesign başarısız: " + client.Name);
                        Console.ResetColor();
                        continue;
                    }

                    var appDirs = Directory.GetDirectories(client.RootPath, "app-*");
                    if (appDirs.Length == 0 && Directory.Exists(client.ResourcesPath))
                    {
                        if (MacSupport.IsPatched(client.ResourcesPath))
                            MacSupport.Unpatch(client.ResourcesPath);
                        else
                            MacSupport.RestoreLooseCore(Directory.GetParent(client.ResourcesPath)?.FullName);
                    }
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

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Başarıyla kaldırıldı: " + client.Name);
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(client.Name + " kaldırılması başarısız oldu: " + ex.Message);
                    Console.WriteLine("Lütfen Discord'un kapalı olduğundan emin olun.");
                    Console.ResetColor();
                }
            }
        }

        static void KillDiscordProcesses()
        {
            if (MacSupport.IsMac())
            {
                MacSupport.KillProcessTree("Discord");
                MacSupport.KillProcessTree("Discord Canary");
                MacSupport.KillProcessTree("Discord PTB");
                MacSupport.KillProcessTree("Discord Development");
                return;
            }

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string name = proc.ProcessName.ToLower();
                    if (name.Contains("discord"))
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }

        static void ExtractResource(string endsWith, string targetPath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string match = null;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase))
                {
                    match = name;
                    break;
                }
            }
            if (match != null)
            {
                using (Stream stream = assembly.GetManifestResourceStream(match))
                using (FileStream fileStream = new FileStream(targetPath, FileMode.Create))
                {
                    stream.CopyTo(fileStream);
                }
                return;
            }

            var candidates = new List<string>();
            candidates.Add(Path.Combine(AppContext.BaseDirectory, endsWith));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "dist", endsWith));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "dist", endsWith));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), endsWith));
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string exeDir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    candidates.Add(Path.Combine(exeDir, endsWith));
                    candidates.Add(Path.Combine(exeDir, "dist", endsWith));
                }
            }
            catch { }

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                File.Copy(candidate, targetPath, true);
                return;
            }

            throw new Exception("Kaynak bulunamadı: " + endsWith);
        }
    }
}
