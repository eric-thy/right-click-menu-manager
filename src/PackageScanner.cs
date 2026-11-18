using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Cmm
{
    internal class PackageClass
    {
        public string Clsid;
        public string DisplayRaw;
        public string DisplayName;
        public string DefaultIcon;
        public string DllPath;
        public string HandlerPath;
        public string ServerDisplayName;
        public string ServerApplicationDisplayName;
        public bool IsSystemExecutable;
        public string HiveRegistration;
    }

    internal class PackageRecord
    {
        public string FullName;
        public string FamilyName;
        public string Root;
        public string DisplayRaw;
        public string DisplayName;
        public string AppExecutable;
        public Dictionary<string, PackageClass> Classes =
            new Dictionary<string, PackageClass>(StringComparer.OrdinalIgnoreCase);
        public XDocument Manifest;
        public bool IsSystem;
    }

    public static partial class Scanner
    {
        internal static class PackageScanner
        {
            private static readonly Dictionary<string, string> PackageNameMap =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Microsoft.WindowsNotepad", "记事本" },
                    { "Microsoft.Paint", "画图" },
                    { "Microsoft.Windows.Photos", "照片" },
                    { "Clipchamp.Clipchamp", "Clipchamp" },
                    { "Microsoft.VisualStudioCode", "Visual Studio Code" },
                    { "Microsoft.MicrosoftOfficeHub", "Copilot" },
                    { "Microsoft.OneDriveSync", "OneDrive" },
                    { "Microsoft.WindowsTerminal", "Windows 终端" },
                    { "Microsoft.BingSearch", "Bing 搜索" },
                    { "OpenAI.Codex", "ChatGPT" },
                    { "BandizipShellext2", "Bandizip" },
                    { "Microsoft.OutlookForWindows", "Outlook" },
                    { "Microsoft.YourPhone", "手机连接" },
                    { "Microsoft.GamingApp", "Xbox" }
                };

            internal static List<PackageRecord> LoadPackages(List<string> warnings)
            {
                var records = new Dictionary<string, PackageRecord>(StringComparer.OrdinalIgnoreCase);

                // The per-user package repository contains the real install root and a display name.
                try
                {
                    using (RegistryKey repo = Native.OpenKey(RegistryHive.CurrentUser,
                        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages", false))
                    {
                        if (repo != null)
                        {
                            foreach (string fullName in Native.GetSubKeyNamesSafe(repo))
                            {
                                try
                                {
                                    using (RegistryKey pkg = repo.OpenSubKey(fullName))
                                    {
                                        if (pkg == null) continue;
                                        PackageRecord record = GetOrCreate(records, fullName);
                                        record.Root = Native.GetString(pkg, "PackageRootFolder");
                                        record.DisplayRaw = Native.GetString(pkg, "DisplayName");
                                        record.FamilyName = DeriveFamilyName(fullName);
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add("读取应用仓库失败：" + ex.Message);
                }

                foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    try
                    {
                        using (RegistryKey packageRoot = Native.OpenKey(hive,
                            @"Software\Classes\PackagedCom\Package", false))
                        {
                            if (packageRoot == null) continue;
                            foreach (string fullName in Native.GetSubKeyNamesSafe(packageRoot))
                            {
                                try
                                {
                                    using (RegistryKey pkg = packageRoot.OpenSubKey(fullName))
                                    {
                                        if (pkg == null) continue;
                                        PackageRecord record = GetOrCreate(records, fullName);
                                        if (string.IsNullOrEmpty(record.FamilyName))
                                            record.FamilyName = DeriveFamilyName(fullName);

                                        var servers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                                        using (RegistryKey serverRoot = pkg.OpenSubKey("Server"))
                                        {
                                            if (serverRoot != null)
                                            {
                                                foreach (string serverId in Native.GetSubKeyNamesSafe(serverRoot))
                                                {
                                                    using (RegistryKey server = serverRoot.OpenSubKey(serverId))
                                                    {
                                                        if (server == null) continue;
                                                        string display = Native.GetString(server, "DisplayName");
                                                        string appDisplay = Native.GetString(server, "ApplicationDisplayName");
                                                        string executable = Native.GetString(server, "Executable");
                                                        string system = Native.GetString(server, "IsSystemExecutable");
                                                        bool sys = system == "1";
                                                        servers[serverId] = new[] { display, appDisplay, executable, sys ? "1" : "0" };
                                                        if (string.IsNullOrEmpty(record.DisplayRaw))
                                                            record.DisplayRaw = !string.IsNullOrEmpty(appDisplay) ? appDisplay : display;
                                                        if (sys) record.IsSystem = true;
                                                        if (string.IsNullOrEmpty(record.AppExecutable) && !string.IsNullOrEmpty(executable))
                                                            record.AppExecutable = executable;
                                                    }
                                                }
                                            }
                                        }

                                        using (RegistryKey classRoot = pkg.OpenSubKey("Class"))
                                        {
                                            if (classRoot == null) continue;
                                            foreach (string rawClsid in Native.GetSubKeyNamesSafe(classRoot))
                                            {
                                                string clsid = Native.NormalizeClsid(rawClsid);
                                                if (string.IsNullOrEmpty(clsid) || !clsid.StartsWith("{")) continue;
                                                try
                                                {
                                                    using (RegistryKey cls = classRoot.OpenSubKey(rawClsid))
                                                    {
                                                        if (cls == null) continue;
                                                        var info = new PackageClass();
                                                        info.Clsid = clsid;
                                                        info.DisplayRaw = Native.GetString(cls, "DisplayName");
                                                        info.DisplayName = Native.CleanLabel(info.DisplayRaw);
                                                        info.DefaultIcon = Native.GetString(cls, "DefaultIcon");
                                                        info.DllPath = Native.GetString(cls, "DllPath");
                                                        if (string.IsNullOrEmpty(info.DllPath))
                                                            info.DllPath = Native.GetString(cls, "InprocServerDllPath_x64");
                                                        if (string.IsNullOrEmpty(info.DllPath))
                                                            info.DllPath = Native.GetString(cls, "InprocServerDllPath");
                                                        string serverId = Native.GetString(cls, "ServerId");
                                                        string[] serverInfo;
                                                        if (!string.IsNullOrEmpty(serverId) && servers.TryGetValue(serverId, out serverInfo))
                                                        {
                                                            info.ServerDisplayName = serverInfo[0];
                                                            info.ServerApplicationDisplayName = serverInfo[1];
                                                            if (serverInfo[3] == "1") info.IsSystemExecutable = true;
                                                            if (!string.IsNullOrEmpty(serverInfo[2]))
                                                            {
                                                                info.HandlerPath = serverInfo[2];
                                                                if (string.IsNullOrEmpty(record.AppExecutable))
                                                                    record.AppExecutable = serverInfo[2];
                                                            }
                                                        }
                                                        info.HiveRegistration = Native.FullRegistryPath(hive,
                                                            @"Software\Classes\PackagedCom\Package\" + fullName + @"\Class\" + clsid);
                                                        if (!record.Classes.ContainsKey(clsid))
                                                            record.Classes[clsid] = info;
                                                    }
                                                }
                                                catch
                                                {
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (warnings.Count < 20)
                                        warnings.Add("读取打包应用 " + fullName + " 失败：" + ex.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add("枚举打包应用失败：" + ex.Message);
                    }
                }

                foreach (PackageRecord record in records.Values)
                {
                    if (string.IsNullOrEmpty(record.DisplayName))
                        record.DisplayName = ResolvePackageDisplay(record);
                    if (record.Classes.Count == 0) continue;
                    try
                    {
                        string manifest = FindManifest(record);
                        if (string.IsNullOrEmpty(manifest) || !File.Exists(manifest)) continue;
                        record.Manifest = XDocument.Load(manifest);
                        if (string.IsNullOrEmpty(record.AppExecutable))
                        {
                            XElement app = record.Manifest.Descendants()
                                .FirstOrDefault(delegate(XElement e) { return e.Name.LocalName == "Application"; });
                            if (app != null)
                            {
                                XAttribute exe = app.Attributes().FirstOrDefault(delegate(XAttribute a) { return a.Name.LocalName == "Executable"; });
                                if (exe != null) record.AppExecutable = exe.Value;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                return records.Values.OrderBy(delegate(PackageRecord r) { return r.DisplayName; }, StringComparer.CurrentCulture).ToList();
            }

            private static PackageRecord GetOrCreate(Dictionary<string, PackageRecord> records, string fullName)
            {
                PackageRecord record;
                if (!records.TryGetValue(fullName, out record))
                {
                    record = new PackageRecord();
                    record.FullName = fullName;
                    records[fullName] = record;
                }
                return record;
            }

            private static string DeriveFamilyName(string fullName)
            {
                if (string.IsNullOrEmpty(fullName)) return fullName;
                string[] parts = fullName.Split('_');
                if (parts.Length >= 2) return parts[0] + "_" + parts[parts.Length - 1];
                return fullName;
            }

            private static string ResolvePackageDisplay(PackageRecord record)
            {
                string display = Native.CleanLabel(record.DisplayRaw);
                if (!string.IsNullOrEmpty(display)) return display;
                string baseName = record.FullName;
                int idx = baseName.IndexOf('_');
                if (idx > 0) baseName = baseName.Substring(0, idx);
                string mapped;
                if (PackageNameMap.TryGetValue(baseName, out mapped)) return mapped;
                return baseName;
            }

            private static string FindManifest(PackageRecord record)
            {
                var candidates = new List<string>();
                if (!string.IsNullOrEmpty(record.Root))
                {
                    candidates.Add(Path.Combine(record.Root, "AppxManifest.xml"));
                    try
                    {
                        string parent = Path.GetDirectoryName(record.Root);
                        if (!string.IsNullOrEmpty(parent)) candidates.Add(Path.Combine(parent, "AppxManifest.xml"));
                        string grand = string.IsNullOrEmpty(parent) ? null : Path.GetDirectoryName(parent);
                        if (!string.IsNullOrEmpty(grand)) candidates.Add(Path.Combine(grand, "AppxManifest.xml"));
                    }
                    catch
                    {
                    }
                }
                try
                {
                    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    if (!string.IsNullOrEmpty(programFiles))
                        candidates.Add(Path.Combine(programFiles, "WindowsApps", record.FullName, "AppxManifest.xml"));
                    string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    if (!string.IsNullOrEmpty(windows) && !string.IsNullOrEmpty(record.FamilyName))
                        candidates.Add(Path.Combine(windows, "SystemApps", record.FamilyName, "AppxManifest.xml"));
                    if (!string.IsNullOrEmpty(windows))
                        candidates.Add(Path.Combine(windows, "SystemApps", record.FullName, "AppxManifest.xml"));
                }
                catch
                {
                }
                foreach (string candidate in candidates)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                            return candidate;
                    }
                    catch
                    {
                    }
                }
                return null;
            }

            internal static void ScanModernMenus(List<PackageRecord> packages,
                Dictionary<string, MenuEntry> byHandler, List<MenuEntry> entries,
                List<string> warnings, bool loadIcons)
            {
                ExplorerCommandProbe.BeginBatch(1800);
                foreach (PackageRecord package in packages)
                {
                    if (package.Manifest == null) continue;
                    try
                    {
                        var defs = new Dictionary<string, ModernDef>(StringComparer.OrdinalIgnoreCase);
                        foreach (XElement menu in package.Manifest.Descendants()
                            .Where(delegate(XElement e) { return e.Name.LocalName == "FileExplorerContextMenus"; }))
                        {
                            bool foundItemType = false;
                            foreach (XElement itemType in menu.Descendants()
                                .Where(delegate(XElement e) { return e.Name.LocalName == "ItemType"; }))
                            {
                                foundItemType = true;
                                XAttribute typeAttr = itemType.Attributes()
                                    .FirstOrDefault(delegate(XAttribute a) { return a.Name.LocalName == "Type"; });
                                string type = typeAttr == null ? ScopeKeys.AllFiles : Native.NormalizeType(typeAttr.Value);
                                foreach (XElement verb in itemType.Descendants()
                                    .Where(delegate(XElement e) { return e.Name.LocalName == "Verb"; }))
                                {
                                    AddModernDef(defs, package, verb, type);
                                }
                            }
                            if (!foundItemType)
                            {
                                foreach (XElement verb in menu.Descendants()
                                    .Where(delegate(XElement e) { return e.Name.LocalName == "Verb"; }))
                                {
                                    AddModernDef(defs, package, verb, ScopeKeys.AllFiles);
                                }
                            }
                        }

                        foreach (ModernDef def in defs.Values)
                        {
                            string comTitle = ExplorerCommandProbe.ProbeTitle(def.Clsid);
                            if (!string.IsNullOrEmpty(comTitle))
                            {
                                string cleaned = Native.CleanLabel(comTitle);
                                comTitle = string.IsNullOrEmpty(cleaned) ? comTitle : cleaned;
                            }
                            MenuEntry entry;
                            if (!byHandler.TryGetValue(def.Clsid, out entry))
                            {
                                PackageClass cls;
                                package.Classes.TryGetValue(def.Clsid, out cls);
                                entry = new MenuEntry();
                                entry.Id = "handler:" + def.Clsid;
                                entry.Clsid = def.Clsid;
                                entry.PrimaryKind = EntryKind.Modern;
                                entry.Kinds.Add(EntryKind.Modern);
                                entry.Title = !string.IsNullOrEmpty(comTitle) ? comTitle : BuildModernTitle(package, cls, def);
                                entry.Subtitle = package.DisplayName + " · Windows 11 现代菜单";
                                entry.PackageDisplay = package.DisplayName;
                                entry.IconLetter = Native.MakeIconLetter(entry.Title);
                                entry.IsEnabled = true;
                                entry.IsSystem = package.IsSystem || IsWindowsSystemPackage(package.FullName) ||
                                    (cls != null && cls.IsSystemExecutable);
                                entry.Backend = new HandlerInfo { Clsid = def.Clsid, Modern = true };
                                entry.Categories.Add(Categories.Modern);
                                entry.TechnicalPath = cls != null && !string.IsNullOrEmpty(cls.HiveRegistration)
                                    ? cls.HiveRegistration
                                    : "HKEY_LOCAL_MACHINE\\SOFTWARE\\Classes\\PackagedCom\\Package\\" + package.FullName + "\\Class\\" + def.Clsid;
                                entry.Command = "IExplorerCommand（打包应用）";
                                entry.ActionHint = "通过阻止此 CLSID 隐藏现代菜单项，不影响软件本身。";
                                if (loadIcons)
                                    entry.Icon = LoadModernIcon(package, cls, true);
                                byHandler[def.Clsid] = entry;
                                entries.Add(entry);
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(comTitle)) entry.Title = comTitle;
                                if (entry.Kinds.Contains(EntryKind.Modern) == false)
                                {
                                    entry.Kinds.Add(EntryKind.Modern);
                                    entry.Categories.Add(Categories.Modern);
                                    if (string.IsNullOrEmpty(entry.Subtitle))
                                        entry.Subtitle = package.DisplayName + " · Windows 11 现代菜单";
                                }
                                if (string.IsNullOrEmpty(entry.PackageDisplay))
                                    entry.PackageDisplay = package.DisplayName;
                                if (!entry.IsSystem)
                                    entry.IsSystem = package.IsSystem || IsWindowsSystemPackage(package.FullName);
                                if (entry.Icon == null && loadIcons)
                                {
                                    PackageClass existingCls;
                                    package.Classes.TryGetValue(def.Clsid, out existingCls);
                                    entry.Icon = LoadModernIcon(package, existingCls, true);
                                }
                            }

                            HandlerInfo info = entry.Backend as HandlerInfo;
                            if (info == null)
                            {
                                info = new HandlerInfo { Clsid = def.Clsid };
                                entry.Backend = info;
                            }
                            info.Modern = true;
                            if (!info.PackageNames.Contains(package.FullName))
                                info.PackageNames.Add(package.FullName);
                            foreach (string verb in def.VerbIds)
                            {
                                if (!info.VerbIds.Contains(verb)) info.VerbIds.Add(verb);
                                AppendDetail(entry, "Verb", verb);
                            }
                            foreach (string type in def.Types)
                            {
                                entry.Types.Add(type);
                                foreach (string cat in Native.CategoriesForScope(type))
                                    entry.Categories.Add(cat);
                            }
                            AppendDetail(entry, "应用包", package.FullName);
                            if (entry.DetailRows.All(delegate(KeyValuePair<string, string> p) { return p.Key != "处理程序"; }))
                            {
                                PackageClass cls;
                                if (package.Classes.TryGetValue(def.Clsid, out cls))
                                {
                                    if (!string.IsNullOrEmpty(cls.HandlerPath))
                                    {
                                        entry.HandlerPath = cls.HandlerPath;
                                        AppendDetail(entry, "处理程序", cls.HandlerPath);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (warnings.Count < 25)
                            warnings.Add("解析 " + package.DisplayName + " 的现代菜单失败：" + ex.Message);
                    }
                }

                foreach (MenuEntry entry in entries)
                {
                    if (!entry.Kinds.Contains(EntryKind.Modern)) continue;
                    if (string.IsNullOrEmpty(entry.HandlerPath))
                    {
                        HandlerInfo info = entry.Backend as HandlerInfo;
                        if (info != null)
                        {
                            foreach (PackageRecord p in packages.Where(delegate(PackageRecord pr) { return info.PackageNames.Contains(pr.FullName); }))
                            {
                                PackageClass cls;
                                if (p.Classes.TryGetValue(entry.Clsid, out cls) && !string.IsNullOrEmpty(cls.HandlerPath))
                                {
                                    entry.HandlerPath = cls.HandlerPath;
                                    break;
                                }
                            }
                        }
                    }
                    string types = Native.BuildScopeText(entry.Types);
                    if (!string.IsNullOrEmpty(types))
                        AppendDetail(entry, "适用范围", types);
                }
            }

            private class ModernDef
            {
                public string Clsid;
                public HashSet<string> VerbIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                public HashSet<string> Types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            private static void AddModernDef(Dictionary<string, ModernDef> defs, PackageRecord package, XElement verb, string type)
            {
                XAttribute clsidAttr = verb.Attributes()
                    .FirstOrDefault(delegate(XAttribute a) { return a.Name.LocalName == "Clsid"; });
                if (clsidAttr == null) return;
                string clsid = Native.NormalizeClsid(clsidAttr.Value);
                if (string.IsNullOrEmpty(clsid) || !clsid.StartsWith("{")) return;
                ModernDef def;
                if (!defs.TryGetValue(clsid, out def))
                {
                    def = new ModernDef();
                    def.Clsid = clsid;
                    defs[clsid] = def;
                }
                XAttribute idAttr = verb.Attributes()
                    .FirstOrDefault(delegate(XAttribute a) { return a.Name.LocalName == "Id"; });
                if (idAttr != null && !string.IsNullOrEmpty(idAttr.Value))
                    def.VerbIds.Add(idAttr.Value);
                def.Types.Add(type);
            }

            private static ImageSource LoadModernIcon(PackageRecord package, PackageClass cls, bool loadIcons)
            {
                if (!loadIcons) return null;
                try
                {
                    string baseDir = package.Root;
                    if (cls != null)
                    {
                        if (!string.IsNullOrEmpty(cls.DefaultIcon))
                        {
                            ImageSource img = Native.LoadIconSpec(cls.DefaultIcon, baseDir);
                            if (img != null) return img;
                        }
                        if (!string.IsNullOrEmpty(cls.HandlerPath))
                        {
                            string path = cls.HandlerPath;
                            if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(baseDir))
                                path = Path.Combine(baseDir, path);
                            ImageSource img = Native.LoadPathIcon(path, 0, baseDir);
                            if (img != null) return img;
                        }
                    }
                    if (!string.IsNullOrEmpty(package.AppExecutable))
                    {
                        string exe = package.AppExecutable;
                        if (!Path.IsPathRooted(exe) && !string.IsNullOrEmpty(baseDir))
                            exe = Path.Combine(baseDir, exe);
                        ImageSource img = Native.LoadPathIcon(exe, 0, baseDir);
                        if (img != null) return img;
                    }
                    if (!string.IsNullOrEmpty(package.Root))
                    {
                        ImageSource near = LoadAdjacentAppIcon(package);
                        if (near != null) return near;
                    }
                }
                catch
                {
                }
                return null;
            }

            private static ImageSource LoadAdjacentAppIcon(PackageRecord package)
            {
                if (package == null || string.IsNullOrEmpty(package.Root)) return null;
                string[] preferred;
                if (!string.IsNullOrEmpty(package.FullName) && package.FullName.StartsWith("Microsoft.VisualStudioCode", StringComparison.OrdinalIgnoreCase))
                    preferred = new[] { "Code.exe", "Code - Insiders.exe" };
                else if (!string.IsNullOrEmpty(package.FullName) && package.FullName.StartsWith("Microsoft.WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                    preferred = new[] { "WindowsTerminal.exe", "wt.exe" };
                else if (!string.IsNullOrEmpty(package.FullName) && package.FullName.StartsWith("Microsoft.WindowsNotepad", StringComparison.OrdinalIgnoreCase))
                    preferred = new[] { "notepad.exe" };
                else
                    preferred = new[] { "Code.exe", "WindowsTerminal.exe", "notepad.exe" };

                string dir = package.Root;
                for (int depth = 0; depth < 4 && !string.IsNullOrEmpty(dir) && Directory.Exists(dir); depth++)
                {
                    foreach (string name in preferred)
                    {
                        string candidate = Path.Combine(dir, name);
                        if (File.Exists(candidate))
                        {
                            ImageSource icon = Native.LoadPathIcon(candidate, 0, null);
                            if (icon != null) return icon;
                        }
                    }
                    if (depth > 0)
                    {
                        try
                        {
                            foreach (string exe in Directory.GetFiles(dir, "*.exe").Take(16))
                            {
                                string baseName = Path.GetFileName(exe).ToLowerInvariant();
                                if (baseName.Contains("unins") || baseName.Contains("setup") || baseName.Contains("update") ||
                                    baseName.Contains("crash") || baseName.Contains("helper"))
                                    continue;
                                string product = Native.GetFileProductName(exe);
                                if (!string.IsNullOrEmpty(product) &&
                                    product.IndexOf(package.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    ImageSource icon = Native.LoadPathIcon(exe, 0, null);
                                    if (icon != null) return icon;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                    dir = Path.GetDirectoryName(dir);
                }
                return null;
            }

            private static string BuildModernTitle(PackageRecord package, PackageClass cls, ModernDef def)
            {
                if (cls != null)
                {
                    string display = cls.DisplayName;
                    if (string.IsNullOrEmpty(display)) display = Native.CleanLabel(cls.DisplayRaw);
                    if (!string.IsNullOrEmpty(display)) return display;
                }
                foreach (string verb in def.VerbIds)
                {
                    string known = KnownVerbTitle(package, verb);
                    if (!string.IsNullOrEmpty(known)) return known;
                }
                if (def.VerbIds.Count > 0)
                    return HumanizeVerb(def.VerbIds.First());
                return package.DisplayName;
            }

            private static string KnownVerbTitle(PackageRecord package, string verb)
            {
                if (string.IsNullOrEmpty(verb)) return null;
                switch (verb)
                {
                    case "AskM365Copilot":
                        return "询问 Copilot";
                    case "OpenWithCode":
                        return "通过 Code 打开";
                    case "OpenInNotepad":
                        return "在记事本中编辑";
                    case "EditInPaint":
                        return "在画图中编辑";
                    case "AShellEdit":
                    case "ShellEdit":
                        return "在照片中编辑";
                    case "EditWith":
                        if (package.FullName.StartsWith("Clipchamp.", StringComparison.OrdinalIgnoreCase))
                            return "使用 Clipchamp 编辑";
                        return "编辑";
                    case "OpenInTerminal":
                    case "OpenTerminal":
                    case "OpenTerminalHere":
                        return "在终端中打开";
                    case "OpenProjectInCodex":
                    case "OpenProjectInChatGPT":
                        return "Open project in ChatGPT";
                    case "AskCopilot":
                        return "询问 Copilot";
                    case "Edit":
                        return "编辑";
                }
                return null;
            }

            private static string HumanizeVerb(string verb)
            {
                if (string.IsNullOrEmpty(verb)) return "菜单项";
                string s = Regex.Replace(verb, "([a-z0-9])([A-Z])", "$1 $2");
                s = s.Replace("_", " ").Trim();
                if (s.Length == 0) return "菜单项";
                return s;
            }

            private static bool IsMicrosoftPackage(string fullName)
            {
                if (string.IsNullOrEmpty(fullName)) return false;
                return fullName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
                    || fullName.StartsWith("MicrosoftCorporation", StringComparison.OrdinalIgnoreCase)
                    || fullName.StartsWith("MicrosoftWindows.", StringComparison.OrdinalIgnoreCase)
                    || fullName.StartsWith("Clipchamp.", StringComparison.OrdinalIgnoreCase);
            }

            private static bool IsWindowsSystemPackage(string fullName)
            {
                if (string.IsNullOrEmpty(fullName)) return false;
                return fullName.StartsWith("MicrosoftWindows.", StringComparison.OrdinalIgnoreCase)
                    || fullName.StartsWith("Microsoft.Windows.FileExplorer", StringComparison.OrdinalIgnoreCase)
                    || fullName.StartsWith("Microsoft.Windows.Shell", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
