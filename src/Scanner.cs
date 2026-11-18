using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;

namespace Cmm
{
    public class ScanResult
    {
        public List<MenuEntry> Entries = new List<MenuEntry>();
        public List<string> Warnings = new List<string>();
        public long ElapsedMilliseconds;
    }

    public class FileTypeOption
    {
        public string Key { get; set; }
        public string Display { get; set; }
        public string Description { get; set; }
        public override string ToString()
        {
            return Display;
        }
    }

    public static partial class Scanner
    {
        private const string CustomPrefix = "CMM_Custom_";

        private class ScopeDef
        {
            public string Path;
            public string Label;
            public string Category;
            public ScopeDef(string path, string label, string category)
            {
                Path = path;
                Label = label;
                Category = category;
            }
        }

        private static readonly ScopeDef[] ClassicScopes = new[]
        {
            new ScopeDef(ScopeKeys.AllFiles, "所有文件", Categories.Files),
            new ScopeDef(ScopeKeys.AllFilesystemObjects, "所有文件和文件夹", Categories.Files),
            new ScopeDef(ScopeKeys.Directory, "文件夹", Categories.Folders),
            new ScopeDef(ScopeKeys.DirectoryBackground, "文件夹空白处", Categories.Background),
            new ScopeDef(ScopeKeys.Folder, "文件夹（Folder）", Categories.Folders),
            new ScopeDef(ScopeKeys.Drive, "驱动器", Categories.Drives),
            new ScopeDef(ScopeKeys.DesktopBackground, "桌面", Categories.Desktop),
            new ScopeDef(ScopeKeys.LibraryFolder, "库文件夹", Categories.Folders),
            new ScopeDef(ScopeKeys.Network, "网络位置", Categories.All),
            new ScopeDef(@"SystemFileAssociations\Directory", "文件夹（类型关联）", Categories.Folders),
            new ScopeDef(@"SystemFileAssociations\Directory.Audio", "音频文件夹", Categories.Folders),
            new ScopeDef(@"SystemFileAssociations\Directory.Image", "图片文件夹", Categories.Folders),
            new ScopeDef(@"SystemFileAssociations\Directory.Video", "视频文件夹", Categories.Folders),
            new ScopeDef(@"SystemFileAssociations\Drive", "驱动器（类型关联）", Categories.Drives),
            new ScopeDef(@"SystemFileAssociations\AllFilesystemObjects", "所有文件和文件夹（类型关联）", Categories.Files),
            new ScopeDef(@"SystemFileAssociations\*", "所有文件（类型关联）", Categories.Files)
        };

        private static readonly ScopeDef[] ShellScopes = new[]
        {
            new ScopeDef(ScopeKeys.AllFiles, "所有文件", Categories.Files),
            new ScopeDef(ScopeKeys.AllFilesystemObjects, "所有文件和文件夹", Categories.Files),
            new ScopeDef(ScopeKeys.Directory, "文件夹", Categories.Folders),
            new ScopeDef(ScopeKeys.DirectoryBackground, "文件夹空白处", Categories.Background),
            new ScopeDef(ScopeKeys.Folder, "文件夹（Folder）", Categories.Folders),
            new ScopeDef(ScopeKeys.Drive, "驱动器", Categories.Drives),
            new ScopeDef(ScopeKeys.DesktopBackground, "桌面", Categories.Desktop),
            new ScopeDef(ScopeKeys.LibraryFolder, "库文件夹", Categories.Folders),
            new ScopeDef(ScopeKeys.Network, "网络位置", Categories.All),
            new ScopeDef(@"SystemFileAssociations\Directory", "文件夹（类型关联）", Categories.Folders),
            new ScopeDef(@"SystemFileAssociations\Directory.Audio", "音频文件夹", Categories.Folders),
            new ScopeDef(@"SystemFileAssociations\Directory.Image", "图片文件夹", Categories.Folders),
            new ScopeDef(@"SystemFileAssociations\Directory.Video", "视频文件夹", Categories.Folders),
            new ScopeDef(@"SystemFileAssociations\Drive", "驱动器（类型关联）", Categories.Drives),
            new ScopeDef(@"SystemFileAssociations\AllFilesystemObjects", "所有文件和文件夹（类型关联）", Categories.Files),
            new ScopeDef(@"SystemFileAssociations\*", "所有文件（类型关联）", Categories.Files)
        };

        public static ScanResult ScanAll(bool loadIcons, Action<string> progress)
        {
            var started = Stopwatch.StartNew();
            var result = new ScanResult();
            var byHandler = new Dictionary<string, MenuEntry>(StringComparer.OrdinalIgnoreCase);

            Report(progress, "正在读取已安装的打包应用…");
            List<PackageRecord> packages = new List<PackageRecord>();
            try
            {
                packages = PackageScanner.LoadPackages(result.Warnings);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("读取打包应用失败：" + ex.Message);
            }

            Report(progress, "正在分析 Windows 11 现代右键菜单…");
            try
            {
                PackageScanner.ScanModernMenus(packages, byHandler, result.Entries, result.Warnings, loadIcons);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("扫描现代菜单失败：" + ex.Message);
            }

            Report(progress, "正在分析 Shell 扩展…");
            try
            {
                ScanShellExtensions(byHandler, result.Entries, result.Warnings, loadIcons);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("扫描 Shell 扩展失败：" + ex.Message);
            }

            Report(progress, "正在读取阻止列表…");
            try
            {
                ApplyBlockedList(byHandler, result.Entries, result.Warnings, loadIcons);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("读取阻止列表失败：" + ex.Message);
            }

            Report(progress, "正在分析经典右键菜单…");
            try
            {
                ScanClassicVerbs(result.Entries, result.Warnings, loadIcons);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("扫描经典菜单失败：" + ex.Message);
            }

            Report(progress, "正在分析新建菜单…");
            try
            {
                ScanNewMenu(result.Entries, result.Warnings, loadIcons);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("扫描新建菜单失败：" + ex.Message);
            }

            Report(progress, "正在分析发送到菜单…");
            try
            {
                ScanSendTo(result.Entries, result.Warnings, loadIcons);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("扫描发送到菜单失败：" + ex.Message);
            }

            Report(progress, "正在读取自定义项目…");
            try
            {
                ScanCustom(result.Entries, result.Warnings, loadIcons);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("扫描自定义项目失败：" + ex.Message);
            }

            Report(progress, "正在整理结果…");
            foreach (MenuEntry entry in result.Entries)
                FinalizeEntry(entry, loadIcons);

            result.Entries = result.Entries
                .GroupBy(delegate(MenuEntry e) { return e.Id; }, StringComparer.OrdinalIgnoreCase)
                .Select(delegate(IGrouping<string, MenuEntry> g) { return g.First(); })
                .ToList();

            result.ElapsedMilliseconds = started.ElapsedMilliseconds;
            Report(progress, "扫描完成");
            return result;
        }

        private static void Report(Action<string> progress, string message)
        {
            if (progress != null)
            {
                try { progress(message); }
                catch { }
            }
        }

        private static void ScanShellExtensions(Dictionary<string, MenuEntry> byHandler, List<MenuEntry> entries, List<string> warnings, bool loadIcons)
        {
            foreach (ScopeDef scope in ShellScopes)
            {
                foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    string path = @"Software\Classes\" + scope.Path + @"\shellex\ContextMenuHandlers";
                    using (RegistryKey handlers = Native.OpenKey(hive, path, false))
                    {
                        if (handlers == null) continue;
                        foreach (string name in Native.GetSubKeyNamesSafe(handlers))
                        {
                            try
                            {
                                using (RegistryKey handler = handlers.OpenSubKey(name))
                                {
                                    if (handler == null) continue;
                                    string clsid = Native.NormalizeClsid(Native.GetString(handler, string.Empty));
                                    if (string.IsNullOrEmpty(clsid) || !clsid.StartsWith("{"))
                                        continue;

                                    MenuEntry entry;
                                    if (!byHandler.TryGetValue(clsid, out entry))
                                    {
                                        entry = new MenuEntry();
                                        entry.Id = "handler:" + clsid;
                                        entry.Clsid = clsid;
                                        entry.PrimaryKind = EntryKind.ShellExtension;
                                        entry.Kinds.Add(EntryKind.ShellExtension);
                                        string display = Native.GetClassDisplayName(clsid);
                                        string server = Native.GetClassServerPath(clsid);
                                        string product = Native.GetFileProductName(server);
                                        string company = Native.GetFileCompanyName(server);
                                        entry.Title = !string.IsNullOrEmpty(display) ? display : PrettifyClsidName(name);
                                        if (string.IsNullOrEmpty(entry.Title) || entry.Title.StartsWith("{", StringComparison.Ordinal))
                                            entry.Title = !string.IsNullOrEmpty(product) ? product : "Shell 扩展";
                                        entry.Subtitle = "Shell 扩展";
                                        if (!string.IsNullOrEmpty(product))
                                        {
                                            if (Native.IsWindowsPath(server) || product.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
                                                entry.Subtitle = "Shell 扩展 · Windows 系统组件";
                                            else
                                                entry.Subtitle = "Shell 扩展 · " + product;
                                        }
                                        entry.IconLetter = Native.MakeIconLetter(entry.Title);
                                        entry.HandlerPath = server;
                                        entry.Command = server;
                                        entry.TechnicalPath = "HKEY_CLASSES_ROOT\\CLSID\\" + clsid;
                                        entry.IsSystem = Native.IsWindowsPath(server);
                                        entry.IsEnabled = true;
                                        entry.VendorText = entry.IsSystem ? null : BuildVendorLabel(server, product, company);
                                        entry.Backend = new HandlerInfo();
                                        byHandler[clsid] = entry;
                                        entries.Add(entry);
                                        entry.Icon = LoadHandlerIcon(clsid, null, loadIcons);
                                        if (!string.IsNullOrEmpty(server)) AppendDetail(entry, "处理程序", server);
                                        AppendDetail(entry, "CLSID", clsid);
                                    }

                                    HandlerInfo info = entry.Backend as HandlerInfo;
                                    if (info == null)
                                    {
                                        info = new HandlerInfo();
                                        info.Clsid = clsid;
                                        entry.Backend = info;
                                    }
                                    info.ShellExtension = true;
                                    entry.Kinds.Add(EntryKind.ShellExtension);
                                    entry.Types.Add(scope.Path);
                                    entry.Categories.Add(scope.Category);
                                    AddHandlerScope(entry, scope.Label);
                                    AppendDetail(entry, "注册位置", Native.FullRegistryPath(hive, path + "\\" + name));
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, MenuEntry> pair in byHandler)
            {
                MenuEntry e = pair.Value;
                if ((e.Kinds.Contains(EntryKind.ShellExtension) || e.Kinds.Contains(EntryKind.Modern)) && e.Icon == null && loadIcons)
                {
                    HandlerInfo info = e.Backend as HandlerInfo;
                    if (info != null && info.HandlerPaths.Count > 0)
                        e.Icon = LoadHandlerIcon(e.Clsid, null, true);
                }
            }
        }

        private static void AddHandlerScope(MenuEntry entry, string label)
        {
            if (string.IsNullOrEmpty(label)) return;
            string marker = "范围：" + label;
            if (entry.DetailRows.Any(delegate(KeyValuePair<string, string> p) { return p.Key == "范围" && p.Value.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0; }))
                return;
            AppendDetail(entry, "范围", label);
        }

        private static ImageSource LoadHandlerIcon(string clsid, string packageRoot, bool loadIcons)
        {
            if (!loadIcons) return null;
            try
            {
                string server = Native.GetClassServerPath(clsid);
                if (!string.IsNullOrEmpty(server))
                {
                    ImageSource img = Native.LoadPathIcon(server, 0, packageRoot);
                    if (img != null) return img;
                }
            }
            catch
            {
            }
            return null;
        }

        private static string PrettifyClsidName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "未知扩展";
            if (name.StartsWith("{", StringComparison.Ordinal)) return name;
            string s = name.Replace("_", " ").Trim();
            s = Regex.Replace(s, "([a-z0-9])([A-Z])", "$1 $2");
            return s.Trim();
        }

        private static string BuildVendorLabel(string serverPath, string product, string company)
        {
            if (!string.IsNullOrEmpty(product))
            {
                if (product.IndexOf("Windows® Operating System", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    product.IndexOf("Windows Operating System", StringComparison.OrdinalIgnoreCase) >= 0)
                    return null;
                if (product.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    product.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    product = Path.GetFileNameWithoutExtension(product);
            }
            string companyShort = ShortCompany(company);
            string vendor = product;
            if (string.IsNullOrEmpty(vendor)) vendor = companyShort;
            else if (!string.IsNullOrEmpty(companyShort) && vendor.IndexOf(companyShort, StringComparison.OrdinalIgnoreCase) < 0)
                vendor = companyShort + " " + vendor;
            if (!string.IsNullOrEmpty(vendor)) return vendor.Trim();
            if (!string.IsNullOrEmpty(serverPath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(serverPath);
                    for (int i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
                    {
                        string name = Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(name) &&
                            !name.Equals("Program Files", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("Common Files", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("Temp", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("addons", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("office6", StringComparison.OrdinalIgnoreCase) &&
                            name.IndexOf("shellext", StringComparison.OrdinalIgnoreCase) < 0 &&
                            !System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d+(\.\d+)+$"))
                        {
                            return name;
                        }
                        dir = Path.GetDirectoryName(dir);
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private static string ShortCompany(string company)
        {
            if (string.IsNullOrWhiteSpace(company)) return null;
            string c = company.Trim();
            string[] words = c.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return c;
            string first = words[0];
            if (first.Equals("Huawei", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Adobe", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Bandisoft", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("IObit", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Kingsoft", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
                return first;
            return c.Length <= 28 ? c : first;
        }

        private static void ApplyBlockedList(Dictionary<string, MenuEntry> byHandler, List<MenuEntry> entries, List<string> warnings, bool loadIcons)
        {
            var blocked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var hklmBlocked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (RegistryKey key = Native.OpenKey(RegistryHive.CurrentUser, Native.BlockedKeyPath, false))
            {
                if (key != null)
                {
                    foreach (string name in Native.GetValueNamesSafe(key))
                    {
                        if (string.IsNullOrEmpty(name) || !name.StartsWith("{")) continue;
                        blocked[Native.NormalizeClsid(name)] = Native.GetString(key, name) ?? string.Empty;
                    }
                }
            }
            using (RegistryKey key = Native.OpenKey(RegistryHive.LocalMachine, Native.BlockedKeyPath, false))
            {
                if (key != null)
                {
                    foreach (string name in Native.GetValueNamesSafe(key))
                    {
                        if (string.IsNullOrEmpty(name) || !name.StartsWith("{")) continue;
                        hklmBlocked[Native.NormalizeClsid(name)] = Native.GetString(key, name) ?? string.Empty;
                    }
                }
            }

            foreach (KeyValuePair<string, string> pair in blocked)
            {
                MenuEntry entry;
                if (byHandler.TryGetValue(pair.Key, out entry))
                {
                    entry.IsEnabled = false;
                    entry.DisabledReason = "用户级阻止：" + (string.IsNullOrEmpty(pair.Value) ? "已阻止" : pair.Value);
                    entry.IsExternalDisabled = !IsOurBlock(pair.Value);
                    if (hklmBlocked.ContainsKey(pair.Key))
                    {
                        entry.RequiresAdmin = true;
                        entry.DisabledReason += "；同时存在系统级阻止";
                    }
                    AppendDetail(entry, "阻止方式", "HKEY_CURRENT_USER\\" + Native.BlockedKeyPath);
                }
                else
                {
                    entry = CreateOrphanBlock(pair.Key, pair.Value, false, loadIcons);
                    entries.Add(entry);
                    byHandler[pair.Key] = entry;
                }
            }

            foreach (KeyValuePair<string, string> pair in hklmBlocked)
            {
                MenuEntry entry;
                if (byHandler.TryGetValue(pair.Key, out entry))
                {
                    entry.IsEnabled = false;
                    entry.RequiresAdmin = true;
                    if (string.IsNullOrEmpty(entry.DisabledReason))
                        entry.DisabledReason = "系统级阻止：" + (string.IsNullOrEmpty(pair.Value) ? "已阻止" : pair.Value);
                    entry.IsExternalDisabled = !IsOurBlock(pair.Value);
                    AppendDetail(entry, "系统阻止", "HKEY_LOCAL_MACHINE\\" + Native.BlockedKeyPath);
                }
                else
                {
                    entry = CreateOrphanBlock(pair.Key, pair.Value, true, loadIcons);
                    entries.Add(entry);
                    byHandler[pair.Key] = entry;
                }
            }
        }

        private static bool IsOurBlock(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf("右键菜单管理器", StringComparison.Ordinal) >= 0;
        }

        private static MenuEntry CreateOrphanBlock(string clsid, string reason, bool machine, bool loadIcons)
        {
            string known = KnownHandlerName(clsid);
            var entry = new MenuEntry();
            entry.Id = "block:" + clsid;
            entry.Clsid = clsid;
            entry.PrimaryKind = EntryKind.OrphanBlock;
            entry.Kinds.Add(EntryKind.OrphanBlock);
            entry.Title = string.IsNullOrEmpty(known) ? "已阻止的菜单扩展" : known;
            entry.Subtitle = "阻止列表中的项目";
            entry.IsEnabled = false;
            entry.IsAvailable = false;
            entry.IsOrphan = true;
            entry.RequiresAdmin = machine;
            entry.DisabledReason = machine ? "系统级阻止" : "用户级阻止";
            entry.Description = "当前没有找到这个 CLSID 对应的已注册处理程序，软件可能已卸载。你可以解除阻止，但不会恢复已卸载的软件。";
            entry.IconLetter = Native.MakeIconLetter(entry.Title);
            entry.Backend = new HandlerInfo { Clsid = clsid, Modern = false, ShellExtension = false };
            entry.TechnicalPath = machine
                ? "HKEY_LOCAL_MACHINE\\" + Native.BlockedKeyPath
                : "HKEY_CURRENT_USER\\" + Native.BlockedKeyPath;
            AppendDetail(entry, "CLSID", clsid);
            AppendDetail(entry, "阻止原因", string.IsNullOrEmpty(reason) ? "未注明" : reason);
            AppendDetail(entry, "状态", "已失效的阻止项");
            entry.Types.Add("阻止项");
            entry.Categories.Add(Categories.ShellExt);
            entry.Categories.Add(Categories.Disabled);
            entry.ActionHint = "解除阻止不会重新安装已卸载的软件。";
            return entry;
        }

        private static string KnownHandlerName(string clsid)
        {
            if (string.IsNullOrEmpty(clsid)) return null;
            string c = clsid.ToUpperInvariant();
            if (c == "{DB6BACB1-FC31-4B28-9C53-7240A4F98404}" || c == "{CB3B0003-8088-4EDE-8769-8B354AB2FF8C}")
                return "询问 Copilot";
            if (c.StartsWith("{1FA0E654-C9F2-4A1F-9800-B9A75D744B", StringComparison.Ordinal))
                return "OneDrive 菜单扩展";
            if (c == "{E2BF9676-5F8F-435C-97EB-11607A5BEDF7}")
                return "共享";
            if (c == "{86CA1AA0-34AA-4E8B-A509-50C905BAE2A2}")
                return "Windows 11 右键菜单";
            return null;
        }

        private static void ScanClassicVerbs(List<MenuEntry> entries, List<string> warnings, bool loadIcons)
        {
            foreach (ScopeDef scope in ClassicScopes)
            {
                foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    string path = @"Software\Classes\" + scope.Path + @"\shell";
                    using (RegistryKey shell = Native.OpenKey(hive, path, false))
                    {
                        if (shell == null) continue;
                        foreach (string verbName in Native.GetSubKeyNamesSafe(shell))
                        {
                            if (verbName.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                            try
                            {
                                MenuEntry entry = ReadClassicVerb(hive, path, verbName, scope, false, loadIcons);
                                if (entry != null) entries.Add(entry);
                            }
                            catch (Exception ex)
                            {
                                if (warnings.Count < 20)
                                    warnings.Add("读取 " + Native.FullRegistryPath(hive, path + "\\" + verbName) + " 失败：" + ex.Message);
                            }
                        }
                    }
                }
            }
        }

        private static MenuEntry ReadClassicVerb(RegistryHive hive, string parentPath, string verbName, ScopeDef scope, bool custom, bool loadIcons)
        {
            string keyPath = parentPath + "\\" + verbName;
            using (RegistryKey key = Native.OpenKey(hive, keyPath, false))
            {
                if (key == null) return null;

                string muiverb = Native.CleanLabel(Native.GetString(key, "MUIVerb"));
                string def = Native.CleanLabel(Native.GetString(key, string.Empty));
                string commandStore = Native.GetString(key, "CommandStore");
                string explorerHandler = Native.GetString(key, "ExplorerCommandHandler");
                string delegateExecute = Native.GetString(key, "DelegateExecute");
                bool extended = Native.ValueExists(key, "Extended");
                string appliesTo = Native.GetString(key, "AppliesTo");
                bool hasSubCommands = Native.ValueExists(key, "SubCommands");
                string iconSpec = Native.GetString(key, "Icon");
                bool legacy = Native.ValueExists(key, Native.DisabledValueName);
                bool programmatic = Native.ValueExists(key, Native.ProgrammaticValueName);

                string title = muiverb;
                if (string.IsNullOrEmpty(title)) title = def;
                string command = null;

                using (RegistryKey cmdKey = key.OpenSubKey("command"))
                {
                    if (cmdKey != null) command = Native.GetString(cmdKey, string.Empty);
                }

                if (!string.IsNullOrEmpty(commandStore))
                {
                    string storePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\" + commandStore;
                    using (RegistryKey store = Native.OpenKey(RegistryHive.LocalMachine, storePath, false))
                    {
                        if (store != null)
                        {
                            if (string.IsNullOrEmpty(title)) title = Native.CleanLabel(Native.GetString(store, "MUIVerb"));
                            if (string.IsNullOrEmpty(title)) title = Native.CleanLabel(Native.GetString(store, string.Empty));
                            if (string.IsNullOrEmpty(iconSpec)) iconSpec = Native.GetString(store, "Icon");
                            if (string.IsNullOrEmpty(command))
                            {
                                using (RegistryKey storeCmd = store.OpenSubKey("command"))
                                {
                                    if (storeCmd != null) command = Native.GetString(storeCmd, string.Empty);
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(title)) title = PrettyVerbName(verbName);
                if (string.IsNullOrEmpty(title)) title = "未命名菜单项";
                var entry = new MenuEntry();
                entry.Id = string.Format("classic:{0}:{1}:{2}", hive, scope.Path, verbName).ToLowerInvariant();
                entry.Title = title;
                entry.PrimaryKind = custom ? EntryKind.Custom : EntryKind.Classic;
                entry.Kinds.Add(custom ? EntryKind.Custom : EntryKind.Classic);
                entry.IsEnabled = !(legacy || programmatic);
                entry.IsExternalDisabled = entry.IsEnabled ? false : !IsOurLegacyReason(key);
                entry.RequiresAdmin = hive == RegistryHive.LocalMachine;
                entry.IsCustom = custom;
                foreach (string category in Native.CategoriesForScope(scope.Path))
                    entry.Categories.Add(category);
                if (scope.Category != Categories.All)
                    entry.Categories.Add(scope.Category);
                entry.Types.Add(scope.Path);
                entry.Clsid = Native.NormalizeClsid(explorerHandler);
                entry.TechnicalPath = Native.FullRegistryPath(hive, keyPath);
                entry.Command = command;
                entry.IconLetter = Native.MakeIconLetter(title);
                entry.Backend = new ClassicVerbInfo
                {
                    Hive = hive,
                    KeyPath = keyPath,
                    VerbName = verbName,
                    IsCustom = custom,
                    CustomCommand = command
                };
                entry.ScopeText = scope.Label + (extended ? " · 按住 Shift 显示" : string.Empty);
                if (!entry.IsEnabled)
                {
                    entry.DisabledReason = programmatic ? "系统以 ProgrammaticAccessOnly 隐藏" : (legacy ? "LegacyDisable 已启用" : "已禁用");
                    if (entry.IsExternalDisabled) entry.DisabledReason += "（不是本工具设置的）";
                }

                string exe = ExtractExecutable(command);
                if (loadIcons && !string.IsNullOrEmpty(iconSpec))
                    entry.Icon = Native.LoadIconSpec(iconSpec, null);
                if (loadIcons && entry.Icon == null && !string.IsNullOrEmpty(exe))
                    entry.Icon = Native.LoadPathIcon(exe, 0, null);
                if (loadIcons && entry.Icon == null && !string.IsNullOrEmpty(entry.Clsid))
                    entry.Icon = LoadHandlerIcon(entry.Clsid, null, true);

                if (extended) AppendDetail(entry, "显示条件", "按住 Shift 右键时显示");
                if (!string.IsNullOrEmpty(appliesTo)) AppendDetail(entry, "AppliesTo", appliesTo);
                if (hasSubCommands) AppendDetail(entry, "菜单类型", "级联子菜单");
                if (!string.IsNullOrEmpty(commandStore)) AppendDetail(entry, "命令存储", commandStore);
                if (!string.IsNullOrEmpty(explorerHandler)) AppendDetail(entry, "ExplorerCommandHandler", explorerHandler);
                if (!string.IsNullOrEmpty(delegateExecute)) AppendDetail(entry, "DelegateExecute", delegateExecute);
                if (string.IsNullOrEmpty(command) && string.IsNullOrEmpty(explorerHandler) && !hasSubCommands)
                    entry.Description = "此项没有命令行，可能是由系统或其他扩展动态生成的占位项。";
                if (custom)
                    entry.ActionHint = "这是你创建的自定义菜单项，可在详情中删除。";
                else
                    entry.ActionHint = "使用 LegacyDisable 隐藏，不删除原命令，随时可以恢复。";
                entry.IsSystem = LooksSystem(command, entry.Clsid);
                return entry;
            }
        }

        private static bool IsOurLegacyReason(RegistryKey key)
        {
            try
            {
                object v = key.GetValue(Native.DisabledValueName);
                if (v == null) return false;
                string s = v.ToString();
                return s.IndexOf("右键菜单管理器", StringComparison.Ordinal) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksSystem(string command, string clsid)
        {
            string exe = ExtractExecutable(command);
            if (!string.IsNullOrEmpty(exe))
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(win) && exe.StartsWith(win, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            if (!string.IsNullOrEmpty(clsid))
            {
                string server = Native.GetClassServerPath(clsid);
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(win) &&
                    server.StartsWith(win, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string ExtractExecutable(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            string s = command.Trim();
            string exe;
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = s.IndexOf('"', 1);
                exe = end > 0 ? s.Substring(1, end - 1) : s.Trim('"');
            }
            else
            {
                int space = s.IndexOf(' ');
                exe = space > 0 ? s.Substring(0, space) : s;
            }
            exe = Native.Expand(exe);
            if (string.IsNullOrEmpty(exe)) return null;
            if (exe.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                exe.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
                exe.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase) ||
                exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                string sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe);
                if (File.Exists(sys)) return sys;
                string win = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), exe);
                if (File.Exists(win)) return win;
            }
            return exe;
        }

        private static string PrettyVerbName(string verbName)
        {
            if (string.IsNullOrEmpty(verbName)) return verbName;
            string lower = verbName.ToLowerInvariant();
            switch (lower)
            {
                case "open": return "打开";
                case "edit": return "编辑";
                case "print": return "打印";
                case "printto": return "打印到";
                case "new": return "新建";
                case "find": return "查找";
                case "explore": return "在资源管理器中打开";
                case "runas": return "以管理员身份运行";
                case "runasuser": return "以其他用户身份运行";
                case "pintohome": return "固定到“快速访问”";
                case "pintohomefile": return "固定到“快速访问”";
                case "removeproperties": return "删除属性";
                case "cmd": return "在此处打开命令窗口";
                case "powershell": return "在此处打开 PowerShell";
            }
            string s = verbName.Replace("_", " ").Replace(".", " ").Trim();
            s = Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");
            if (s.EndsWith("Class", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 5).Trim();
            return s;
        }

        private static void ScanNewMenu(List<MenuEntry> entries, List<string> warnings, bool loadIcons)
        {
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                string classesPath = @"Software\Classes";
                using (RegistryKey classes = Native.OpenKey(hive, classesPath, false))
                {
                    if (classes == null) continue;
                    foreach (string extName in Native.GetSubKeyNamesSafe(classes))
                    {
                        if (!extName.StartsWith(".", StringComparison.Ordinal)) continue;
                        try
                        {
                            using (RegistryKey extKey = classes.OpenSubKey(extName))
                            {
                                if (extKey == null) continue;
                                bool hasActive = Native.GetSubKeyNamesSafe(extKey).Any(delegate(string n) { return string.Equals(n, "ShellNew", StringComparison.OrdinalIgnoreCase); });
                                string disabledName = Native.GetSubKeyNamesSafe(extKey).FirstOrDefault(delegate(string n) { return n.StartsWith(Native.ShellNewDisabledName, StringComparison.OrdinalIgnoreCase); });
                                if (!hasActive && string.IsNullOrEmpty(disabledName)) continue;

                                string friendly = Native.CleanLabel(Native.GetString(extKey, "FriendlyTypeName"));
                                if (string.IsNullOrEmpty(friendly)) friendly = Native.CleanLabel(Native.GetString(extKey, string.Empty));
                                if (string.IsNullOrEmpty(friendly)) friendly = Native.GetExtensionFriendlyName(extName);
                                if (string.IsNullOrEmpty(friendly)) friendly = extName;
                                if (friendly.StartsWith(".", StringComparison.Ordinal))
                                    friendly = friendly + " 文件";
                                if (string.Equals(friendly, "LibraryFolder", StringComparison.OrdinalIgnoreCase))
                                    friendly = "库";

                                var entry = new MenuEntry();
                                entry.Id = string.Format("new:{0}:{1}", hive, extName).ToLowerInvariant();
                                entry.Title = friendly;
                                entry.Subtitle = extName + " · 新建菜单";
                                entry.PrimaryKind = EntryKind.NewMenu;
                                entry.Kinds.Add(EntryKind.NewMenu);
                                entry.IsEnabled = hasActive;
                                entry.IsCustom = false;
                                entry.RequiresAdmin = hive == RegistryHive.LocalMachine;
                                entry.Categories.Add(Categories.NewMenu);
                                entry.Types.Add("新建");
                                entry.ScopeText = "桌面 / 文件夹空白处 · 新建";
                                entry.TechnicalPath = Native.FullRegistryPath(hive, classesPath + "\\" + extName + "\\ShellNew");
                                entry.IconLetter = Native.MakeIconLetter(friendly);
                                entry.Backend = new NewMenuInfo
                                {
                                    Hive = hive,
                                    ExtensionKeyPath = classesPath + "\\" + extName,
                                    Extension = extName,
                                    ActiveSubKey = "ShellNew",
                                    DisabledSubKey = disabledName
                                };
                                if (loadIcons) entry.Icon = Native.LoadExtensionIcon(extName);
                                if (!entry.IsEnabled)
                                {
                                    entry.DisabledReason = "ShellNew 已被禁用";
                                    entry.IsExternalDisabled = false;
                                }

                                string activePath = classesPath + "\\" + extName + "\\ShellNew";
                                using (RegistryKey newKey = extKey.OpenSubKey("ShellNew"))
                                {
                                    if (newKey != null)
                                    {
                                        string fileName = Native.GetString(newKey, "FileName");
                                        string command = Native.GetString(newKey, "Command");
                                        string handler = Native.GetString(newKey, "Handler");
                                        string data = Native.GetString(newKey, "Data");
                                        string menuText = Native.CleanLabel(Native.GetString(newKey, "MenuText"));
                                        if (!string.IsNullOrEmpty(menuText)) entry.Title = menuText;
                                        if (Native.ValueExists(newKey, "NullFile"))
                                            entry.Command = "创建空文件";
                                        else if (!string.IsNullOrEmpty(command))
                                            entry.Command = command;
                                        else if (!string.IsNullOrEmpty(fileName))
                                            entry.Command = "模板文件：" + fileName;
                                        else if (!string.IsNullOrEmpty(handler))
                                            entry.Command = "处理器：" + handler;
                                        else if (data != null)
                                            entry.Command = "内嵌模板数据";
                                        else
                                            entry.Command = "由 ShellNew 定义";
                                        if (!string.IsNullOrEmpty(handler)) AppendDetail(entry, "处理器", handler);
                                    }
                                }
                                entry.ActionHint = "通过重命名 ShellNew 子键禁用，不会删除模板数据。";
                                entries.Add(entry);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (warnings.Count < 20)
                                warnings.Add("读取新建菜单 " + extName + " 失败：" + ex.Message);
                        }
                    }
                }
            }
        }

        private static void ScanSendTo(List<MenuEntry> entries, List<string> warnings, bool loadIcons)
        {
            var folders = new List<KeyValuePair<string, bool>>();
            string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\SendTo");
            string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\SendTo");
            folders.Add(new KeyValuePair<string, bool>(user, false));
            folders.Add(new KeyValuePair<string, bool>(common, true));

            foreach (KeyValuePair<string, bool> folder in folders)
            {
                if (!Directory.Exists(folder.Key)) continue;
                string[] files;
                try { files = Directory.GetFiles(folder.Key); }
                catch (Exception ex)
                {
                    warnings.Add("读取发送到目录失败：" + ex.Message);
                    continue;
                }
                foreach (string file in files)
                {
                    if (string.Equals(Path.GetFileName(file), "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string title = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(title)) title = Path.GetFileName(file);
                    var entry = new MenuEntry();
                    entry.Id = "sendto:" + file.ToLowerInvariant();
                    entry.Title = title;
                    entry.Subtitle = "发送到 · " + (folder.Value ? "所有用户" : "当前用户");
                    entry.PrimaryKind = EntryKind.SendTo;
                    entry.Kinds.Add(EntryKind.SendTo);
                    entry.IsEnabled = true;
                    entry.RequiresAdmin = folder.Value;
                    entry.Categories.Add(Categories.SendTo);
                    entry.Types.Add("发送到");
                    entry.ScopeText = "右键 → 发送到";
                    entry.TechnicalPath = file;
                    entry.IconLetter = Native.MakeIconLetter(title);
                    entry.Backend = new SendToInfo { OriginalPath = file, Common = folder.Value };
                    if (loadIcons) entry.Icon = Native.LoadFileIcon(file);
                    entry.ActionHint = "禁用时会把快捷方式移到本工具备份目录，随时可以恢复。";
                    entries.Add(entry);
                }
            }

            string backupDir = AppPaths.SendToBackupDir;
            if (!Directory.Exists(backupDir)) return;
            string[] sidecars;
            try { sidecars = Directory.GetFiles(backupDir, "*.path"); }
            catch { return; }
            foreach (string sidecar in sidecars)
            {
                try
                {
                    string original = File.ReadAllText(sidecar, Encoding.UTF8).Trim();
                    if (string.IsNullOrEmpty(original)) continue;
                    string hash = Path.GetFileNameWithoutExtension(sidecar);
                    string backup = Path.Combine(backupDir, hash + Path.GetExtension(original));
                    if (!File.Exists(backup)) continue;
                    bool isCommon = !string.IsNullOrEmpty(common) && original.StartsWith(common, StringComparison.OrdinalIgnoreCase);
                    string title = Path.GetFileNameWithoutExtension(original);
                    var entry = new MenuEntry();
                    entry.Id = "sendto:" + original.ToLowerInvariant();
                    entry.Title = title;
                    entry.Subtitle = "发送到 · " + (isCommon ? "所有用户" : "当前用户");
                    entry.PrimaryKind = EntryKind.SendTo;
                    entry.Kinds.Add(EntryKind.SendTo);
                    entry.IsEnabled = false;
                    entry.RequiresAdmin = isCommon;
                    entry.Categories.Add(Categories.SendTo);
                    entry.Categories.Add(Categories.Disabled);
                    entry.Types.Add("发送到");
                    entry.ScopeText = "右键 → 发送到";
                    entry.TechnicalPath = original;
                    entry.IconLetter = Native.MakeIconLetter(title);
                    entry.Backend = new SendToInfo { OriginalPath = original, BackupPath = backup, Common = isCommon, SidecarPath = sidecar };
                    entry.DisabledReason = "快捷方式已移至备份目录";
                    entry.IsExternalDisabled = false;
                    if (loadIcons) entry.Icon = Native.LoadFileIcon(backup);
                    entry.ActionHint = "恢复时会把快捷方式移回原发送到目录。";
                    entries.Add(entry);
                }
                catch
                {
                }
            }
        }

        private static void ScanCustom(List<MenuEntry> entries, List<string> warnings, bool loadIcons)
        {
            ScopeDef[] scopes = new[]
            {
                new ScopeDef(ScopeKeys.AllFiles, "所有文件", Categories.Files),
                new ScopeDef(ScopeKeys.Directory, "文件夹", Categories.Folders),
                new ScopeDef(ScopeKeys.DirectoryBackground, "文件夹空白处", Categories.Background),
                new ScopeDef(ScopeKeys.Drive, "驱动器", Categories.Drives)
            };
            foreach (ScopeDef scope in scopes)
            {
                string path = @"Software\Classes\" + scope.Path + @"\shell";
                using (RegistryKey shell = Native.OpenKey(RegistryHive.CurrentUser, path, false))
                {
                    if (shell == null) continue;
                    foreach (string name in Native.GetSubKeyNamesSafe(shell))
                    {
                        if (!name.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            MenuEntry entry = ReadClassicVerb(RegistryHive.CurrentUser, path, name, scope, true, loadIcons);
                            if (entry != null)
                            {
                                entry.Categories.Clear();
                                entry.Categories.Add(Categories.Custom);
                                entry.Categories.Add(scope.Category);
                                entries.Add(entry);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (warnings.Count < 20) warnings.Add("读取自定义项目失败：" + ex.Message);
                        }
                    }
                }
            }
        }

        private static void FinalizeEntry(MenuEntry entry, bool loadIcons)
        {
            if (entry.Kinds.Contains(EntryKind.Modern) && entry.Kinds.Contains(EntryKind.ShellExtension))
            {
                entry.Badge = "现代 + 扩展";
                entry.BadgeBackground = Theme.ModernBadge;
                entry.BadgeForeground = Theme.ModernBadgeText;
            }
            else if (entry.Kinds.Contains(EntryKind.Modern))
            {
                entry.Badge = "Win11 现代";
                entry.BadgeBackground = Theme.ModernBadge;
                entry.BadgeForeground = Theme.ModernBadgeText;
            }
            else if (entry.Kinds.Contains(EntryKind.ShellExtension))
            {
                entry.Badge = "Shell 扩展";
                entry.BadgeBackground = Theme.ShellBadge;
                entry.BadgeForeground = Theme.ShellBadgeText;
            }
            else if (entry.Kinds.Contains(EntryKind.Custom))
            {
                entry.Badge = "自定义";
                entry.BadgeBackground = Theme.CustomBadge;
                entry.BadgeForeground = Theme.CustomBadgeText;
            }
            else if (entry.Kinds.Contains(EntryKind.NewMenu))
            {
                entry.Badge = "新建菜单";
                entry.BadgeBackground = Theme.NewBadge;
                entry.BadgeForeground = Theme.NewBadgeText;
            }
            else if (entry.Kinds.Contains(EntryKind.SendTo))
            {
                entry.Badge = "发送到";
                entry.BadgeBackground = Theme.SendBadge;
                entry.BadgeForeground = Theme.SendBadgeText;
            }
            else if (entry.Kinds.Contains(EntryKind.OrphanBlock))
            {
                entry.Badge = "阻止项";
                entry.BadgeBackground = Theme.DangerSoft;
                entry.BadgeForeground = Theme.Danger;
            }
            else
            {
                entry.Badge = "经典";
                entry.BadgeBackground = Theme.ClassicBadge;
                entry.BadgeForeground = Theme.ClassicBadgeText;
            }

            if (entry.Kinds.Contains(EntryKind.Modern)) entry.Categories.Add(Categories.Modern);
            if (entry.Kinds.Contains(EntryKind.Classic)) entry.Categories.Add(Categories.Classic);
            if (entry.Kinds.Contains(EntryKind.ShellExtension)) entry.Categories.Add(Categories.ShellExt);
            if (entry.Kinds.Contains(EntryKind.NewMenu)) entry.Categories.Add(Categories.NewMenu);
            if (entry.Kinds.Contains(EntryKind.SendTo)) entry.Categories.Add(Categories.SendTo);
            if (entry.Kinds.Contains(EntryKind.Custom)) entry.Categories.Add(Categories.Custom);
            if (!entry.IsEnabled) entry.Categories.Add(Categories.Disabled);

            if (string.IsNullOrEmpty(entry.Subtitle))
            {
                if (entry.Kinds.Contains(EntryKind.Modern)) entry.Subtitle = "Windows 11 现代右键菜单";
                else if (entry.Kinds.Contains(EntryKind.ShellExtension)) entry.Subtitle = "Shell 上下文菜单扩展";
                else if (entry.Kinds.Contains(EntryKind.NewMenu)) entry.Subtitle = "新建菜单";
                else if (entry.Kinds.Contains(EntryKind.SendTo)) entry.Subtitle = "发送到菜单";
                else if (entry.Kinds.Contains(EntryKind.Custom)) entry.Subtitle = "自定义菜单项";
                else entry.Subtitle = "经典右键菜单";
            }

            if (string.IsNullOrEmpty(entry.ScopeText))
                entry.ScopeText = Native.BuildScopeText(entry.Types);
            if (!string.IsNullOrEmpty(entry.VendorText) &&
                !entry.ScopeText.StartsWith(entry.VendorText, StringComparison.OrdinalIgnoreCase))
                entry.ScopeText = entry.VendorText + " · " + entry.ScopeText;

            if (entry.Categories.Count == 0) entry.Categories.Add(Categories.Files);
            if (entry.IconLetter == null) entry.IconLetter = Native.MakeIconLetter(entry.Title);
            if (entry.IconBackground == null)
            {
                if (entry.Kinds.Contains(EntryKind.Modern)) entry.IconBackground = Theme.ModernBadge;
                else if (entry.Kinds.Contains(EntryKind.ShellExtension)) entry.IconBackground = Theme.ShellBadge;
                else if (entry.Kinds.Contains(EntryKind.Custom)) entry.IconBackground = Theme.CustomBadge;
                else if (entry.Kinds.Contains(EntryKind.NewMenu)) entry.IconBackground = Theme.NewBadge;
                else if (entry.Kinds.Contains(EntryKind.SendTo)) entry.IconBackground = Theme.SendBadge;
                else entry.IconBackground = Theme.ClassicBadge;
            }

            if (entry.BadgeBackground == null)
            {
                entry.BadgeBackground = Theme.ClassicBadge;
                entry.BadgeForeground = Theme.ClassicBadgeText;
            }

            if (string.IsNullOrEmpty(entry.ActionHint))
                entry.ActionHint = "切换不会删除软件或命令，可随时恢复。";
            if (entry.IsSystem && string.IsNullOrEmpty(entry.Description))
                entry.Description = "这是 Windows 或系统组件的菜单项。禁用可能导致某些系统功能入口消失。";

            var parts = new List<string>();
            parts.Add(entry.Title ?? string.Empty);
            parts.Add(entry.Subtitle ?? string.Empty);
            parts.Add(entry.Description ?? string.Empty);
            parts.Add(entry.Badge ?? string.Empty);
            parts.Add(entry.ScopeText ?? string.Empty);
            parts.Add(entry.TechnicalPath ?? string.Empty);
            parts.Add(entry.Command ?? string.Empty);
            parts.Add(entry.Clsid ?? string.Empty);
            parts.Add(entry.PackageDisplay ?? string.Empty);
            parts.Add(string.Join(" ", entry.Types.ToArray()));
            entry.SearchText = string.Join(" ", parts.ToArray()).ToLowerInvariant();
        }

        private static void AppendDetail(MenuEntry entry, string key, string value)
        {
            if (entry == null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return;
            if (entry.DetailRows.Any(delegate(KeyValuePair<string, string> p) { return p.Key == key && p.Value == value; }))
                return;
            entry.DetailRows.Add(new KeyValuePair<string, string>(key, value));
        }

        public static void AppendDetailPublic(MenuEntry entry, string key, string value)
        {
            AppendDetail(entry, key, value);
        }

        public static List<FileTypeOption> BuildFileTypeOptions()
        {
            var list = new List<FileTypeOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] common = new[]
            {
                "*", "Directory", "Directory\\Background", "Drive",
                ".txt", ".md", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx",
                ".csv", ".json", ".xml", ".html", ".htm", ".css", ".js", ".ts", ".py", ".cs", ".java",
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".heic", ".tif", ".tiff", ".ico",
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mp3", ".wav", ".flac", ".m4a", ".aac",
                ".zip", ".rar", ".7z", ".tar", ".gz", ".iso", ".exe", ".dll", ".msi", ".ps1", ".bat", ".cmd",
                ".reg", ".lnk", ".torrent", ".apk", ".epub", ".rtf", ".log", ".ini", ".yaml", ".yml"
            };
            foreach (string key in common)
            {
                if (seen.Contains(key)) continue;
                seen.Add(key);
                list.Add(MakeFileTypeOption(key));
            }
            string[] classes = new[] { "image", "video", "audio", "document", "text", "compressed" };
            foreach (string c in classes)
            {
                if (seen.Contains(c)) continue;
                seen.Add(c);
                list.Add(MakeFileTypeOption(c));
            }

            var dynamicKeys = new List<string>();
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                using (RegistryKey classesKey = Native.OpenKey(hive, @"Software\Classes", false))
                {
                    if (classesKey == null) continue;
                    foreach (string ext in Native.GetSubKeyNamesSafe(classesKey))
                    {
                        if (!ext.StartsWith(".", StringComparison.Ordinal) || seen.Contains(ext)) continue;
                        try
                        {
                            string[] children;
                            using (RegistryKey extKey = classesKey.OpenSubKey(ext))
                            {
                                children = Native.GetSubKeyNamesSafe(extKey);
                            }
                            bool hasMenu = children.Any(delegate(string n)
                            {
                                return string.Equals(n, "shell", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(n, "shellex", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(n, "ShellNew", StringComparison.OrdinalIgnoreCase);
                            });
                            if (hasMenu && !dynamicKeys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                                dynamicKeys.Add(ext);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            dynamicKeys.Sort(StringComparer.CurrentCultureIgnoreCase);
            foreach (string ext in dynamicKeys)
            {
                if (seen.Contains(ext)) continue;
                seen.Add(ext);
                list.Add(MakeFileTypeOption(ext));
            }
            return list;
        }

        private static FileTypeOption MakeFileTypeOption(string key)
        {
            var option = new FileTypeOption();
            option.Key = key;
            if (key == "*")
            {
                option.Display = "所有文件 (*)";
                option.Description = "适用于所有文件的菜单";
            }
            else if (string.Equals(key, "Directory", StringComparison.OrdinalIgnoreCase))
            {
                option.Display = "文件夹 (Directory)";
                option.Description = "文件夹右键菜单";
            }
            else if (string.Equals(key, "Directory\\Background", StringComparison.OrdinalIgnoreCase))
            {
                option.Display = "文件夹空白处";
                option.Description = "在文件夹空白处右键";
            }
            else if (string.Equals(key, "Drive", StringComparison.OrdinalIgnoreCase))
            {
                option.Display = "驱动器";
                option.Description = "驱动器右键菜单";
            }
            else if (key.StartsWith(".", StringComparison.Ordinal))
            {
                string friendly = Native.GetExtensionFriendlyName(key);
                option.Display = string.IsNullOrEmpty(friendly) ? key : key + " · " + friendly;
                option.Description = string.IsNullOrEmpty(friendly) ? key + " 文件" : friendly;
            }
            else
            {
                string label;
                switch (key.ToLowerInvariant())
                {
                    case "image": label = "图片"; break;
                    case "video": label = "视频"; break;
                    case "audio": label = "音频"; break;
                    case "document": label = "文档"; break;
                    case "text": label = "文本"; break;
                    case "compressed": label = "压缩文件"; break;
                    default: label = key; break;
                }
                option.Display = "所有" + label + "（类型）";
                option.Description = "SystemFileAssociations\\" + key;
            }
            return option;
        }
    }
}
