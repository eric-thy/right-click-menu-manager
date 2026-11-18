using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using Microsoft.Win32;

namespace Cmm
{
    public static partial class Scanner
    {
        private class TypeRoot
        {
            public string Path;
            public string Label;
            public TypeRoot(string path, string label)
            {
                Path = path;
                Label = label;
            }
        }

        public static List<MenuEntry> ScanFileType(string key, IList<MenuEntry> allEntries,
            List<string> warnings, bool loadIcons)
        {
            var entries = new List<MenuEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            key = string.IsNullOrWhiteSpace(key) ? ScopeKeys.AllFiles : key.Trim();

            try
            {
                foreach (MenuEntry source in allEntries)
                {
                    if (!source.Kinds.Contains(EntryKind.Modern) && !source.IsOrphan) continue;
                    if (ModernMatchesType(source, key))
                    {
                        if (seen.Add("shared:" + source.Id))
                            entries.Add(source);
                    }
                }

                List<TypeRoot> roots = BuildTypeRoots(key);
                var handlers = new Dictionary<string, MenuEntry>(StringComparer.OrdinalIgnoreCase);

                foreach (TypeRoot root in roots)
                {
                    foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                    {
                        try
                        {
                            string shellPath = @"Software\Classes\" + root.Path + @"\shell";
                            using (RegistryKey shell = Native.OpenKey(hive, shellPath, false))
                            {
                                if (shell != null)
                                {
                                    foreach (string verbName in Native.GetSubKeyNamesSafe(shell))
                                    {
                                        if (verbName.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                                        try
                                        {
                                            var scope = new ScopeDef(root.Path, root.Label,
                                                key.StartsWith(".", StringComparison.Ordinal) || key == "*" ? Categories.Files : Categories.Files);
                                            MenuEntry e = ReadClassicVerb(hive, shellPath, verbName, scope, false, loadIcons);
                                            if (e == null) continue;
                                            e.Id = "filetype:" + key.ToLowerInvariant() + ":" + hive + ":" + root.Path + ":" + verbName;
                                            e.Categories.Clear();
                                            e.Types.Clear();
                                            e.Types.Add(key);
                                            e.ScopeText = key + " · " + root.Label;
                                            e.Subtitle = "文件类型 · " + root.Label;
                                            AppendDetail(e, "文件类型", key);
                                            AppendDetail(e, "匹配来源", root.Label);
                                            e.Description = string.IsNullOrEmpty(e.Description)
                                                ? "此项是为该文件类型单独注册的菜单命令。"
                                                : e.Description;
                                            if (seen.Add(e.Id)) entries.Add(e);
                                        }
                                        catch
                                        {
                                        }
                                    }
                                }
                            }

                            string handlerPath = @"Software\Classes\" + root.Path + @"\shellex\ContextMenuHandlers";
                            using (RegistryKey handlerRoot = Native.OpenKey(hive, handlerPath, false))
                            {
                                if (handlerRoot != null)
                                {
                                    foreach (string name in Native.GetSubKeyNamesSafe(handlerRoot))
                                    {
                                        try
                                        {
                                            using (RegistryKey handler = handlerRoot.OpenSubKey(name))
                                            {
                                                if (handler == null) continue;
                                                string clsid = Native.NormalizeClsid(Native.GetString(handler, string.Empty));
                                                if (string.IsNullOrEmpty(clsid) || !clsid.StartsWith("{")) continue;

                                                MenuEntry entry;
                                                if (!handlers.TryGetValue(clsid, out entry))
                                                {
                                                    entry = new MenuEntry();
                                                    entry.Id = "filetype:" + key.ToLowerInvariant() + ":handler:" + clsid;
                                                    entry.Clsid = clsid;
                                                    entry.PrimaryKind = EntryKind.ShellExtension;
                                                    entry.Kinds.Add(EntryKind.ShellExtension);
                                                    entry.Title = Native.GetClassDisplayName(clsid);
                                                    if (string.IsNullOrEmpty(entry.Title)) entry.Title = name;
                                                    entry.Subtitle = "文件类型 Shell 扩展";
                                                    entry.IconLetter = Native.MakeIconLetter(entry.Title);
                                                    entry.Backend = new HandlerInfo
                                                    {
                                                        Clsid = clsid,
                                                        ShellExtension = true
                                                    };
                                                    entry.ScopeText = key + " · " + root.Label;
                                                    entry.Categories.Add(Categories.ShellExt);
                                                    entry.Types.Add(key);
                                                    entry.ActionHint = "通过阻止 CLSID 隐藏该扩展。";
                                                    if (loadIcons)
                                                    {
                                                        string server = Native.GetClassServerPath(clsid);
                                                        if (!string.IsNullOrEmpty(server))
                                                            entry.Icon = Native.LoadPathIcon(server, 0, null);
                                                    }
                                                    ApplyBlockedStateToView(entry);
                                                    handlers[clsid] = entry;
                                                    if (seen.Add(entry.Id)) entries.Add(entry);
                                                }
                                                HandlerInfo info = entry.Backend as HandlerInfo;
                                                if (info != null && !info.HandlerPaths.Contains(name))
                                                    info.HandlerPaths.Add(name);
                                                AppendDetail(entry, "文件类型", key);
                                                AppendDetail(entry, "匹配来源", root.Label);
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
                                warnings.Add("扫描 " + root.Label + " 失败：" + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add("扫描文件类型失败：" + ex.Message);
            }

            foreach (MenuEntry entry in entries)
                FinalizeEntry(entry, loadIcons);

            return entries
                .OrderBy(delegate(MenuEntry e)
                {
                    if (e.Kinds.Contains(EntryKind.Modern)) return 0;
                    if (e.Kinds.Contains(EntryKind.ShellExtension)) return 1;
                    return 2;
                })
                .ThenBy(delegate(MenuEntry e) { return e.Title; }, StringComparer.CurrentCulture)
                .ToList();
        }

        private static bool ModernMatchesType(MenuEntry entry, string key)
        {
            if (entry.Types == null) return false;
            if (entry.Types.Contains(key)) return true;
            if (string.Equals(key, "*", StringComparison.OrdinalIgnoreCase))
                return entry.Types.Contains(ScopeKeys.AllFiles);
            if (string.Equals(key, "Directory", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "Folder", StringComparison.OrdinalIgnoreCase))
                return entry.Types.Contains(ScopeKeys.Directory) || entry.Types.Contains(ScopeKeys.Folder);
            return false;
        }

        private static List<TypeRoot> BuildTypeRoots(string key)
        {
            var roots = new List<TypeRoot>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> add = delegate(string path, string label)
            {
                if (string.IsNullOrEmpty(path)) return;
                if (seen.Add(path)) roots.Add(new TypeRoot(path, label));
            };

            if (string.Equals(key, "*", StringComparison.OrdinalIgnoreCase))
            {
                add(ScopeKeys.AllFiles, "所有文件通用");
                add(ScopeKeys.AllFilesystemObjects, "所有文件和文件夹通用");
                return roots;
            }
            if (string.Equals(key, "Directory", StringComparison.OrdinalIgnoreCase))
            {
                add(ScopeKeys.Directory, "文件夹");
                add(ScopeKeys.Folder, "文件夹（Folder）");
                add(ScopeKeys.AllFilesystemObjects, "所有文件和文件夹通用");
                return roots;
            }
            if (string.Equals(key, "Directory\\Background", StringComparison.OrdinalIgnoreCase))
            {
                add(ScopeKeys.DirectoryBackground, "文件夹空白处");
                return roots;
            }
            if (string.Equals(key, "Drive", StringComparison.OrdinalIgnoreCase))
            {
                add(ScopeKeys.Drive, "驱动器");
                return roots;
            }

            if (key.StartsWith(".", StringComparison.Ordinal))
            {
                add(key, key + " 直接注册");
                string progid = GetDefaultValue(key);
                if (!string.IsNullOrEmpty(progid))
                    add(progid, "ProgID：" + progid);
                string perceived = GetPerceivedType(key);
                if (!string.IsNullOrEmpty(perceived))
                {
                    add(@"SystemFileAssociations\" + key.TrimStart('.'), "文件类型：" + key);
                    add(@"SystemFileAssociations\" + perceived, "感知类型：" + perceived);
                }
                else
                {
                    add(@"SystemFileAssociations\" + key.TrimStart('.'), "文件类型：" + key);
                    string perceived2 = GetDefaultValue(@"SystemFileAssociations\" + key.TrimStart('.'));
                    if (!string.IsNullOrEmpty(perceived2))
                        add(@"SystemFileAssociations\" + perceived2, "感知类型：" + perceived2);
                }
                return roots;
            }

            add(@"SystemFileAssociations\" + key, "感知类型：" + key);
            add(key, key);
            return roots;
        }

        private static string GetDefaultValue(string keyPath)
        {
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(keyPath))
                {
                    if (key == null) return null;
                    return Native.GetString(key, string.Empty);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetPerceivedType(string ext)
        {
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(ext))
                {
                    if (key != null)
                    {
                        string value = Native.GetString(key, "PerceivedType");
                        if (!string.IsNullOrEmpty(value)) return value;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static void ApplyBlockedStateToView(MenuEntry entry)
        {
            bool hkcu = false;
            bool hklm = false;
            string hkcuValue = null;
            string hklmValue = null;
            using (RegistryKey key = Native.OpenKey(RegistryHive.CurrentUser, Native.BlockedKeyPath, false))
            {
                if (key != null)
                {
                    hkcu = Native.ValueExists(key, entry.Clsid);
                    if (hkcu) hkcuValue = Native.GetString(key, entry.Clsid);
                }
            }
            using (RegistryKey key = Native.OpenKey(RegistryHive.LocalMachine, Native.BlockedKeyPath, false))
            {
                if (key != null)
                {
                    hklm = Native.ValueExists(key, entry.Clsid);
                    if (hklm) hklmValue = Native.GetString(key, entry.Clsid);
                }
            }
            if (hkcu || hklm)
            {
                entry.IsEnabled = false;
                entry.RequiresAdmin = hklm;
                entry.DisabledReason = hklm ? "系统级阻止" : "用户级阻止";
                entry.IsExternalDisabled = !IsOurBlock(hkcu ? hkcuValue : hklmValue);
                entry.Categories.Add(Categories.Disabled);
            }
            else
            {
                entry.IsEnabled = true;
            }
        }
    }
}
