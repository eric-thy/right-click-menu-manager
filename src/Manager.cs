using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace Cmm
{
    public static class AppPaths
    {
        private static string _dataDir;
        private static string _backupDir;
        private static string _sendToDir;

        public static string DataDir
        {
            get
            {
                if (_dataDir == null)
                {
                    _dataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RightClickMenuManager");
                }
                return _dataDir;
            }
        }

        public static string BackupDir
        {
            get
            {
                if (_backupDir == null) _backupDir = Path.Combine(DataDir, "backups");
                return _backupDir;
            }
        }

        public static string SendToBackupDir
        {
            get
            {
                if (_sendToDir == null) _sendToDir = Path.Combine(DataDir, "sendto");
                return _sendToDir;
            }
        }

        public static string HistoryFile
        {
            get { return Path.Combine(DataDir, "history.xml"); }
        }

        public static void Ensure()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(BackupDir);
                Directory.CreateDirectory(SendToBackupDir);
            }
            catch
            {
            }
        }

        public static string HashPath(string path)
        {
            using (var sha = SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
                var sb = new StringBuilder();
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    public class OperationResult
    {
        public bool Success;
        public string Message;
        public ChangeGroup Group;
    }

    public class ChangeAction
    {
        public string Type { get; set; }
        public string Hive { get; set; }
        public string KeyPath { get; set; }
        public string ValueName { get; set; }
        public bool BeforeExists { get; set; }
        public string BeforeValue { get; set; }
        public int BeforeKind { get; set; }
        public bool AfterExists { get; set; }
        public string AfterValue { get; set; }
        public int AfterKind { get; set; }
        public string OldParentPath { get; set; }
        public string OldName { get; set; }
        public string NewName { get; set; }
        public string SourcePath { get; set; }
        public string DestPath { get; set; }
        public string SidecarPath { get; set; }
        public string BackupFile { get; set; }
        public string Note { get; set; }
    }

    public class ChangeGroup
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Description { get; set; }
        public bool Undone { get; set; }
        public List<ChangeAction> Actions { get; set; }

        public ChangeGroup()
        {
            Actions = new List<ChangeAction>();
        }

        [XmlIgnore]
        public string TimeText
        {
            get { return Timestamp.ToString("MM-dd HH:mm"); }
        }

        [XmlIgnore]
        public string StatusText
        {
            get { return Undone ? "已撤回" : "生效中"; }
        }
    }

    [XmlRoot("History")]
    public class HistoryDocument
    {
        [XmlArray("Groups")]
        [XmlArrayItem("Group")]
        public List<ChangeGroup> Groups { get; set; }

        public HistoryDocument()
        {
            Groups = new List<ChangeGroup>();
        }
    }

    public static class Manager
    {
        private static readonly object Sync = new object();
        private static HistoryDocument _history;

        private static HistoryDocument History
        {
            get
            {
                if (_history == null) LoadHistory();
                return _history;
            }
        }

        private static void LoadHistory()
        {
            AppPaths.Ensure();
            _history = new HistoryDocument();
            try
            {
                if (File.Exists(AppPaths.HistoryFile))
                {
                    var serializer = new XmlSerializer(typeof(HistoryDocument));
                    using (FileStream fs = File.OpenRead(AppPaths.HistoryFile))
                    {
                        HistoryDocument doc = serializer.Deserialize(fs) as HistoryDocument;
                        if (doc != null && doc.Groups != null) _history = doc;
                    }
                }
            }
            catch
            {
                try
                {
                    string bad = AppPaths.HistoryFile + ".bad-" + DateTime.Now.Ticks;
                    File.Copy(AppPaths.HistoryFile, bad, true);
                }
                catch
                {
                }
                _history = new HistoryDocument();
            }
        }

        private static void SaveHistory()
        {
            try
            {
                AppPaths.Ensure();
                var serializer = new XmlSerializer(typeof(HistoryDocument));
                string tmp = AppPaths.HistoryFile + ".tmp";
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    serializer.Serialize(fs, _history);
                }
                if (File.Exists(AppPaths.HistoryFile))
                    File.Replace(tmp, AppPaths.HistoryFile, null);
                else
                    File.Move(tmp, AppPaths.HistoryFile);
            }
            catch
            {
            }
        }

        public static List<ChangeGroup> GetHistory(int max)
        {
            lock (Sync)
            {
                return History.Groups
                    .OrderByDescending(delegate(ChangeGroup g) { return g.Timestamp; })
                    .Take(max)
                    .ToList();
            }
        }

        public static int ActiveChangeCount
        {
            get
            {
                lock (Sync)
                {
                    return History.Groups.Count(delegate(ChangeGroup g) { return !g.Undone; });
                }
            }
        }

        public static ChangeGroup LastActiveGroup
        {
            get
            {
                lock (Sync)
                {
                    return History.Groups.Where(delegate(ChangeGroup g) { return !g.Undone; })
                        .OrderByDescending(delegate(ChangeGroup g) { return g.Timestamp; })
                        .FirstOrDefault();
                }
            }
        }

        public static OperationResult Toggle(MenuEntry entry, bool enable)
        {
            lock (Sync)
            {
                if (entry == null) return Fail("没有选中项目。");
                if (entry.IsOrphan)
                    return ToggleBlock(entry, enable);
                switch (entry.PrimaryKind)
                {
                    case EntryKind.Modern:
                    case EntryKind.ShellExtension:
                        return ToggleBlock(entry, enable);
                    case EntryKind.Classic:
                    case EntryKind.Custom:
                        return ToggleClassic(entry, enable);
                    case EntryKind.NewMenu:
                        return ToggleNewMenu(entry, enable);
                    case EntryKind.SendTo:
                        return ToggleSendTo(entry, enable);
                }
                return Fail("暂不支持此类型的项目。");
            }
        }

        private static OperationResult Fail(string message)
        {
            return new OperationResult { Success = false, Message = message };
        }

        private static ChangeGroup NewGroup(string description)
        {
            return new ChangeGroup
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.Now,
                Description = description,
                Undone = false
            };
        }

        private static RegistryKey OpenWrite(RegistryHive hive, string path, bool create)
        {
            RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            if (create)
                return baseKey.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree);
            return baseKey.OpenSubKey(path, true);
        }

        private static string HiveText(RegistryHive hive)
        {
            return hive == RegistryHive.CurrentUser ? "CurrentUser" : "LocalMachine";
        }

        private static RegistryHive HiveFromText(string text)
        {
            return string.Equals(text, "CurrentUser", StringComparison.OrdinalIgnoreCase)
                ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        }

        private static ChangeAction SetValue(RegistryHive hive, string path, string name, object value,
            RegistryValueKind kind, bool exists, ChangeGroup group)
        {
            string full = Native.FullRegistryPath(hive, path);
            bool beforeExists = false;
            string beforeValue = null;
            int beforeKind = (int)RegistryValueKind.String;
            using (RegistryKey read = Native.OpenKey(hive, path, false))
            {
                if (read != null)
                {
                    string[] names = Native.GetValueNamesSafe(read);
                    beforeExists = names.Any(delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); });
                    if (beforeExists)
                    {
                        object current = read.GetValue(name);
                        beforeKind = (int)read.GetValueKind(name);
                        beforeValue = ValueToString(current);
                    }
                }
            }

            if (exists)
            {
                using (RegistryKey key = OpenWrite(hive, path, true))
                {
                    if (key == null) throw new UnauthorizedAccessException("无法写入注册表：" + full);
                    key.SetValue(name, value, kind);
                }
            }
            else
            {
                using (RegistryKey key = OpenWrite(hive, path, false))
                {
                    if (key == null) throw new UnauthorizedAccessException("无法写入注册表：" + full);
                    key.DeleteValue(name, false);
                }
            }

            var action = new ChangeAction();
            action.Type = "SetValue";
            action.Hive = HiveText(hive);
            action.KeyPath = path;
            action.ValueName = name;
            action.BeforeExists = beforeExists;
            action.BeforeValue = beforeValue;
            action.BeforeKind = beforeKind;
            action.AfterExists = exists;
            action.AfterValue = exists ? ValueToString(value) : null;
            action.AfterKind = (int)kind;
            action.Note = full + "\\" + name;
            group.Actions.Add(action);
            return action;
        }

        private static string ValueToString(object value)
        {
            if (value == null) return null;
            byte[] bytes = value as byte[];
            if (bytes != null) return Convert.ToBase64String(bytes);
            string[] strings = value as string[];
            if (strings != null) return string.Join("\0", strings);
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static object ValueFromString(string value, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    return unchecked((int)long.Parse(value));
                case RegistryValueKind.QWord:
                    return long.Parse(value);
                case RegistryValueKind.Binary:
                    return Convert.FromBase64String(value);
                case RegistryValueKind.MultiString:
                    return value.Split(new[] { "\0" }, StringSplitOptions.None);
                case RegistryValueKind.ExpandString:
                    return value;
                default:
                    return value;
            }
        }

        private static void ApplyState(RegistryHive hive, string path, string name, bool exists, string value, RegistryValueKind kind)
        {
            using (RegistryKey key = OpenWrite(hive, path, exists))
            {
                if (key == null) throw new UnauthorizedAccessException("无法写入注册表。");
                if (exists) key.SetValue(name ?? string.Empty, value == null ? string.Empty : ValueFromString(value, kind), kind);
                else key.DeleteValue(name ?? string.Empty, false);
            }
        }

        private static OperationResult ToggleClassic(MenuEntry entry, bool enable)
        {
            ClassicVerbInfo info = entry.Backend as ClassicVerbInfo;
            if (info == null) return Fail("项目数据已失效，请刷新。");
            try
            {
                using (RegistryKey read = Native.OpenKey(info.Hive, info.KeyPath, false))
                {
                    if (read == null) return Fail("此菜单项已不存在，请点击刷新。");
                }

                ChangeGroup group = NewGroup((enable ? "启用 " : "禁用 ") + entry.Title);
                if (!enable)
                {
                    bool alreadyDisabled;
                    using (RegistryKey read = Native.OpenKey(info.Hive, info.KeyPath, false))
                    {
                        alreadyDisabled = read != null && Native.ValueExists(read, Native.DisabledValueName);
                    }
                    if (!alreadyDisabled)
                    {
                        SetValue(info.Hive, info.KeyPath, Native.DisabledValueName,
                            Native.BlockMarker, RegistryValueKind.String, true, group);
                    }
                }
                else
                {
                    RemoveIfExists(info.Hive, info.KeyPath, Native.DisabledValueName, group);
                    RemoveIfExists(info.Hive, info.KeyPath, Native.ProgrammaticValueName, group);
                }

                if (group.Actions.Count == 0)
                    return Fail(enable ? "此项已经是启用状态。" : "此项已经是禁用状态。");

                FinishGroup(group, entry, enable);
                return new OperationResult
                {
                    Success = true,
                    Message = (enable ? "已启用 " : "已禁用 ") + entry.Title,
                    Group = group
                };
            }
            catch (UnauthorizedAccessException)
            {
                return Fail("需要管理员权限才能修改这个系统级项目。请点击左侧底部的“提升权限”。");
            }
            catch (Exception ex)
            {
                return Fail("修改失败：" + ex.Message);
            }
        }

        private static void RemoveIfExists(RegistryHive hive, string path, string name, ChangeGroup group)
        {
            using (RegistryKey key = Native.OpenKey(hive, path, false))
            {
                if (key == null) return;
                if (!Native.ValueExists(key, name)) return;
            }
            SetValue(hive, path, name, null, RegistryValueKind.String, false, group);
        }

        private static OperationResult ToggleBlock(MenuEntry entry, bool enable)
        {
            string clsid = entry.Clsid;
            if (string.IsNullOrEmpty(clsid)) return Fail("这个项目没有 CLSID，无法阻止。");
            try
            {
                string hkcuPath = Native.BlockedKeyPath;
                string hklmPath = Native.BlockedKeyPath;
                bool hkcuExists = false;
                bool hklmExists = false;
                string hkcuValue = null;
                string hklmValue = null;
                using (RegistryKey k = Native.OpenKey(RegistryHive.CurrentUser, hkcuPath, false))
                {
                    if (k != null)
                    {
                        hkcuExists = Native.ValueExists(k, clsid);
                        if (hkcuExists) hkcuValue = Native.GetString(k, clsid);
                    }
                }
                using (RegistryKey k = Native.OpenKey(RegistryHive.LocalMachine, hklmPath, false))
                {
                    if (k != null)
                    {
                        hklmExists = Native.ValueExists(k, clsid);
                        if (hklmExists) hklmValue = Native.GetString(k, clsid);
                    }
                }

                if (!enable)
                {
                    if (hkcuExists)
                        return Fail("这个扩展已经在阻止列表中，请刷新。");
                    ChangeGroup group = NewGroup("禁用 " + entry.Title);
                    SetValue(RegistryHive.CurrentUser, hkcuPath, clsid, Native.BlockMarker, RegistryValueKind.String, true, group);
                    FinishGroup(group, entry, false);
                    return new OperationResult { Success = true, Message = "已禁用 " + entry.Title, Group = group };
                }

                if (hklmExists && !Native.IsElevated())
                    return Fail("这个项目被系统级阻止列表禁用，需要管理员权限。");

                ChangeGroup enableGroup = NewGroup("启用 " + entry.Title);
                if (hkcuExists)
                    SetValue(RegistryHive.CurrentUser, hkcuPath, clsid, null, RegistryValueKind.String, false, enableGroup);
                if (hklmExists)
                    SetValue(RegistryHive.LocalMachine, hklmPath, clsid, null, RegistryValueKind.String, false, enableGroup);
                if (enableGroup.Actions.Count == 0)
                    return Fail("这个项目没有被阻止。");
                FinishGroup(enableGroup, entry, true);
                return new OperationResult { Success = true, Message = "已启用 " + entry.Title, Group = enableGroup };
            }
            catch (UnauthorizedAccessException)
            {
                return Fail("需要管理员权限才能修改系统级阻止项。");
            }
            catch (Exception ex)
            {
                return Fail("修改阻止列表失败：" + ex.Message);
            }
        }

        private static OperationResult ToggleNewMenu(MenuEntry entry, bool enable)
        {
            NewMenuInfo info = entry.Backend as NewMenuInfo;
            if (info == null) return Fail("项目数据已失效，请刷新。");
            try
            {
                ChangeGroup group = NewGroup((enable ? "启用新建 " : "禁用新建 ") + info.Extension);
                if (!enable)
                {
                    using (RegistryKey ext = Native.OpenKey(info.Hive, info.ExtensionKeyPath, false))
                    {
                        if (ext == null) return Fail("文件类型注册项已不存在。");
                        if (!Native.GetSubKeyNamesSafe(ext).Any(delegate(string n) { return string.Equals(n, "ShellNew", StringComparison.OrdinalIgnoreCase); }))
                            return Fail("ShellNew 已不存在，请刷新。");
                    }
                    string newName = Native.ShellNewDisabledName;
                    using (RegistryKey ext = Native.OpenKey(info.Hive, info.ExtensionKeyPath, false))
                    {
                        string[] names = Native.GetSubKeyNamesSafe(ext);
                        int suffix = 1;
                        while (names.Contains(newName, StringComparer.OrdinalIgnoreCase))
                        {
                            newName = Native.ShellNewDisabledName + "_" + suffix;
                            suffix++;
                        }
                    }
                    string error = Native.RenameSubKey(info.Hive, info.ExtensionKeyPath, "ShellNew", newName);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                    AddRenameAction(group, info.Hive, info.ExtensionKeyPath, "ShellNew", newName,
                        Native.FullRegistryPath(info.Hive, info.ExtensionKeyPath) + "\\ShellNew");
                    info.DisabledSubKey = newName;
                }
                else
                {
                    using (RegistryKey ext = Native.OpenKey(info.Hive, info.ExtensionKeyPath, false))
                    {
                        if (ext == null) return Fail("文件类型注册项已不存在。");
                        string[] names = Native.GetSubKeyNamesSafe(ext);
                        if (names.Any(delegate(string n) { return string.Equals(n, "ShellNew", StringComparison.OrdinalIgnoreCase); }))
                            return Fail("ShellNew 已经启用。");
                        string disabled = names.FirstOrDefault(delegate(string n) { return n.StartsWith(Native.ShellNewDisabledName, StringComparison.OrdinalIgnoreCase); });
                        if (string.IsNullOrEmpty(disabled)) return Fail("没有找到被禁用的 ShellNew。");
                        string error = Native.RenameSubKey(info.Hive, info.ExtensionKeyPath, disabled, "ShellNew");
                        if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                        AddRenameAction(group, info.Hive, info.ExtensionKeyPath, disabled, "ShellNew",
                            Native.FullRegistryPath(info.Hive, info.ExtensionKeyPath) + "\\" + disabled);
                        info.DisabledSubKey = null;
                    }
                }
                FinishGroup(group, entry, enable);
                return new OperationResult
                {
                    Success = true,
                    Message = (enable ? "已启用新建 " : "已禁用新建 ") + info.Extension,
                    Group = group
                };
            }
            catch (UnauthorizedAccessException)
            {
                return Fail("需要管理员权限才能修改这个系统级新建菜单项。");
            }
            catch (Exception ex)
            {
                return Fail("修改新建菜单失败：" + ex.Message);
            }
        }

        private static void AddRenameAction(ChangeGroup group, RegistryHive hive, string parentPath,
            string oldName, string newName, string note)
        {
            var action = new ChangeAction();
            action.Type = "RenameKey";
            action.Hive = HiveText(hive);
            action.KeyPath = parentPath;
            action.OldName = oldName;
            action.NewName = newName;
            action.Note = note;
            group.Actions.Add(action);
        }

        private static OperationResult ToggleSendTo(MenuEntry entry, bool enable)
        {
            SendToInfo info = entry.Backend as SendToInfo;
            if (info == null) return Fail("项目数据已失效，请刷新。");
            try
            {
                if (!enable)
                {
                    if (!File.Exists(info.OriginalPath) && !Directory.Exists(info.OriginalPath))
                        return Fail("发送到快捷方式已不存在，请刷新。");
                    AppPaths.Ensure();
                    string hash = AppPaths.HashPath(info.OriginalPath);
                    string backup = Path.Combine(AppPaths.SendToBackupDir, hash + Path.GetExtension(info.OriginalPath));
                    string sidecar = backup + ".path";
                    if (File.Exists(backup) || Directory.Exists(backup))
                        return Fail("备份目录里已经存在同名备份，无法安全移动。");
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    File.Move(info.OriginalPath, backup);
                    try
                    {
                        File.WriteAllText(sidecar, info.OriginalPath, new UTF8Encoding(false));
                    }
                    catch
                    {
                        try { File.Move(backup, info.OriginalPath); }
                        catch { }
                        throw;
                    }
                    info.BackupPath = backup;
                    info.SidecarPath = sidecar;
                    ChangeGroup group = NewGroup("禁用发送到 " + entry.Title);
                    var action = new ChangeAction();
                    action.Type = "SendToDisable";
                    action.SourcePath = info.OriginalPath;
                    action.DestPath = backup;
                    action.SidecarPath = sidecar;
                    action.Note = info.OriginalPath;
                    group.Actions.Add(action);
                    FinishGroup(group, entry, false);
                    return new OperationResult { Success = true, Message = "已从发送到菜单移除 " + entry.Title, Group = group };
                }
                else
                {
                    if (File.Exists(info.OriginalPath) || Directory.Exists(info.OriginalPath))
                        return Fail("发送到快捷方式已经恢复。");
                    if (string.IsNullOrEmpty(info.BackupPath) || (!File.Exists(info.BackupPath) && !Directory.Exists(info.BackupPath)))
                        return Fail("备份文件不存在，无法恢复。");
                    string parent = Path.GetDirectoryName(info.OriginalPath);
                    if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                    File.Move(info.BackupPath, info.OriginalPath);
                    ChangeGroup group = NewGroup("启用发送到 " + entry.Title);
                    var action = new ChangeAction();
                    action.Type = "SendToEnable";
                    action.SourcePath = info.BackupPath;
                    action.DestPath = info.OriginalPath;
                    action.SidecarPath = info.SidecarPath;
                    action.Note = info.OriginalPath;
                    group.Actions.Add(action);
                    if (!string.IsNullOrEmpty(info.SidecarPath) && File.Exists(info.SidecarPath))
                        File.Delete(info.SidecarPath);
                    info.BackupPath = null;
                    info.SidecarPath = null;
                    FinishGroup(group, entry, true);
                    return new OperationResult { Success = true, Message = "已恢复到发送到菜单 " + entry.Title, Group = group };
                }
            }
            catch (UnauthorizedAccessException)
            {
                return Fail("需要管理员权限才能修改“所有用户”的发送到项目。");
            }
            catch (Exception ex)
            {
                return Fail("修改发送到菜单失败：" + ex.Message);
            }
        }

        private static void FinishGroup(ChangeGroup group, MenuEntry entry, bool enabled)
        {
            History.Groups.Add(group);
            entry.IsEnabled = enabled;
            entry.IsBusy = false;
            if (enabled)
            {
                entry.DisabledReason = null;
                entry.IsExternalDisabled = false;
                entry.Categories.Remove(Categories.Disabled);
            }
            else
            {
                entry.DisabledReason = "已由右键菜单管理器禁用";
                entry.IsExternalDisabled = false;
                entry.Categories.Add(Categories.Disabled);
            }
            entry.RefreshVisualState();
            SaveHistory();
            Native.NotifyShellChanged();
        }

        public static OperationResult UndoLast()
        {
            lock (Sync)
            {
                ChangeGroup group = History.Groups.Where(delegate(ChangeGroup g) { return !g.Undone; })
                    .OrderByDescending(delegate(ChangeGroup g) { return g.Timestamp; })
                    .FirstOrDefault();
                if (group == null) return Fail("没有可以撤销的操作。");
                string error;
                if (!UndoGroup(group, out error))
                    return Fail(error);
                SaveHistory();
                Native.NotifyShellChanged();
                return new OperationResult { Success = true, Message = "已撤销：" + group.Description };
            }
        }

        public static OperationResult RestoreAll()
        {
            lock (Sync)
            {
                List<ChangeGroup> groups = History.Groups.Where(delegate(ChangeGroup g) { return !g.Undone; })
                    .OrderByDescending(delegate(ChangeGroup g) { return g.Timestamp; })
                    .ToList();
                if (groups.Count == 0) return Fail("没有需要恢复的更改。");
                int done = 0;
                var errors = new List<string>();
                foreach (ChangeGroup group in groups)
                {
                    string error;
                    if (UndoGroup(group, out error)) done++;
                    else errors.Add(group.Description + "：" + error);
                }
                SaveHistory();
                Native.NotifyShellChanged();
                if (errors.Count == 0)
                    return new OperationResult { Success = true, Message = string.Format("已恢复 {0} 组更改。", done) };
                return new OperationResult
                {
                    Success = false,
                    Message = string.Format("恢复了 {0} 组，另有 {1} 组失败：{2}", done, errors.Count, errors[0])
                };
            }
        }

        private static bool UndoGroup(ChangeGroup group, out string error)
        {
            error = null;
            var errors = new List<string>();
            for (int i = group.Actions.Count - 1; i >= 0; i--)
            {
                ChangeAction action = group.Actions[i];
                try
                {
                    UndoAction(action);
                }
                catch (Exception ex)
                {
                    errors.Add(action.Type + "：" + ex.Message);
                }
            }
            if (errors.Count == 0)
            {
                group.Undone = true;
                RefreshEntriesAfterRestore(group);
                return true;
            }
            error = "撤销未完成：" + errors[0];
            return false;
        }

        private static void UndoAction(ChangeAction action)
        {
            if (action == null || string.IsNullOrEmpty(action.Type)) return;
            RegistryHive hive = HiveFromText(action.Hive);
            switch (action.Type)
            {
                case "SetValue":
                    ApplyState(hive, action.KeyPath, action.ValueName, action.BeforeExists,
                        action.BeforeValue, (RegistryValueKind)action.BeforeKind);
                    break;
                case "RenameKey":
                    string parent = action.KeyPath;
                    string error = Native.RenameSubKey(hive, parent, action.NewName, action.OldName);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                    break;
                case "CreateKey":
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                    {
                        baseKey.DeleteSubKeyTree(action.KeyPath, false);
                    }
                    break;
                case "DeleteTree":
                    RestoreRegistryBackup(action.BackupFile);
                    break;
                case "SendToDisable":
                    if (File.Exists(action.DestPath) || Directory.Exists(action.DestPath))
                    {
                        if (File.Exists(action.SourcePath) || Directory.Exists(action.SourcePath))
                            throw new IOException("原位置已有同名文件，无法恢复。");
                        File.Move(action.DestPath, action.SourcePath);
                    }
                    if (!string.IsNullOrEmpty(action.SidecarPath) && File.Exists(action.SidecarPath))
                        File.Delete(action.SidecarPath);
                    break;
                case "SendToEnable":
                    if (File.Exists(action.DestPath) || Directory.Exists(action.DestPath))
                    {
                        if (File.Exists(action.SourcePath) || Directory.Exists(action.SourcePath))
                            throw new IOException("备份路径已有同名文件，无法撤销。");
                        File.Move(action.DestPath, action.SourcePath);
                    }
                    if (!string.IsNullOrEmpty(action.SidecarPath))
                        File.WriteAllText(action.SidecarPath, action.Note, new UTF8Encoding(false));
                    break;
            }
        }

        private static void RefreshEntriesAfterRestore(ChangeGroup group)
        {
            // The UI performs a full refresh after restore; this hook intentionally stays empty.
        }

        public static OperationResult CreateCustom(string scope, string label, string command, string icon, bool extended)
        {
            lock (Sync)
            {
                if (string.IsNullOrWhiteSpace(label)) return Fail("请填写菜单名称。");
                if (string.IsNullOrWhiteSpace(command)) return Fail("请填写要执行的命令。");
                string scopePath;
                switch (scope)
                {
                    case ScopeKeys.AllFiles: scopePath = ScopeKeys.AllFiles; break;
                    case ScopeKeys.Directory: scopePath = ScopeKeys.Directory; break;
                    case ScopeKeys.DirectoryBackground: scopePath = ScopeKeys.DirectoryBackground; break;
                    case ScopeKeys.Drive: scopePath = ScopeKeys.Drive; break;
                    default: return Fail("不支持这个右键位置。");
                }
                string name = "CMM_Custom_" + Guid.NewGuid().ToString("N");
                string keyPath = @"Software\Classes\" + scopePath + @"\shell\" + name;
                try
                {
                    using (RegistryKey key = OpenWrite(RegistryHive.CurrentUser, keyPath, true))
                    {
                        if (key == null) return Fail("无法创建自定义菜单项。");
                        key.SetValue("MUIVerb", label, RegistryValueKind.String);
                        if (!string.IsNullOrWhiteSpace(icon)) key.SetValue("Icon", icon, RegistryValueKind.String);
                        if (extended) key.SetValue("Extended", string.Empty, RegistryValueKind.String);
                        using (RegistryKey cmd = key.CreateSubKey("command"))
                        {
                            if (cmd != null) cmd.SetValue(string.Empty, command, RegistryValueKind.String);
                        }
                    }
                    ChangeGroup group = NewGroup("新建自定义项 " + label);
                    var action = new ChangeAction();
                    action.Type = "CreateKey";
                    action.Hive = "CurrentUser";
                    action.KeyPath = keyPath;
                    action.Note = label;
                    group.Actions.Add(action);
                    History.Groups.Add(group);
                    SaveHistory();
                    Native.NotifyShellChanged();
                    return new OperationResult { Success = true, Message = "已创建自定义项 " + label, Group = group };
                }
                catch (Exception ex)
                {
                    return Fail("创建失败：" + ex.Message);
                }
            }
        }

        public static OperationResult DeleteCustom(MenuEntry entry)
        {
            lock (Sync)
            {
                ClassicVerbInfo info = entry.Backend as ClassicVerbInfo;
                if (info == null || !info.IsCustom) return Fail("只能删除本工具创建的自定义项。");
                try
                {
                    string backup;
                    DeleteTreeWithBackup(info.Hive, info.KeyPath, out backup);
                    ChangeGroup group = NewGroup("删除自定义项 " + entry.Title);
                    var action = new ChangeAction();
                    action.Type = "DeleteTree";
                    action.Hive = HiveText(info.Hive);
                    action.KeyPath = info.KeyPath;
                    action.BackupFile = backup;
                    action.Note = entry.Title;
                    group.Actions.Add(action);
                    History.Groups.Add(group);
                    SaveHistory();
                    Native.NotifyShellChanged();
                    return new OperationResult { Success = true, Message = "已删除自定义项 " + entry.Title, Group = group };
                }
                catch (UnauthorizedAccessException)
                {
                    return Fail("需要管理员权限。");
                }
                catch (Exception ex)
                {
                    return Fail("删除失败：" + ex.Message);
                }
            }
        }

        public static bool IsClassicMenuActive()
        {
            using (RegistryKey key = Native.OpenKey(RegistryHive.CurrentUser,
                @"Software\Classes\CLSID\{86CA1AA0-34AA-4E8B-A509-50C905BAE2A2}\InprocServer32", false))
            {
                return key != null;
            }
        }

        public static OperationResult SetClassicMenu(bool classic)
        {
            lock (Sync)
            {
                string path = @"Software\Classes\CLSID\{86CA1AA0-34AA-4E8B-A509-50C905BAE2A2}\InprocServer32";
                try
                {
                    if (classic)
                    {
                        if (IsClassicMenuActive()) return Fail("已经使用经典右键菜单。");
                        using (RegistryKey key = OpenWrite(RegistryHive.CurrentUser, path, true))
                        {
                            if (key == null) return Fail("无法写入注册表。");
                            key.SetValue(string.Empty, string.Empty, RegistryValueKind.String);
                        }
                        ChangeGroup group = NewGroup("切换到经典右键菜单");
                        var action = new ChangeAction();
                        action.Type = "CreateKey";
                        action.Hive = "CurrentUser";
                        action.KeyPath = path;
                        action.Note = "Windows 11 经典右键菜单开关";
                        group.Actions.Add(action);
                        History.Groups.Add(group);
                        SaveHistory();
                        Native.NotifyShellChanged();
                        return new OperationResult { Success = true, Message = "已切换为经典右键菜单，重启资源管理器后生效。", Group = group };
                    }
                    else
                    {
                        if (!IsClassicMenuActive()) return Fail("当前已经是 Windows 11 现代右键菜单。");
                        string backup;
                        DeleteTreeWithBackup(RegistryHive.CurrentUser,
                            @"Software\Classes\CLSID\{86CA1AA0-34AA-4E8B-A509-50C905BAE2A2}", out backup);
                        ChangeGroup group = NewGroup("恢复 Windows 11 现代右键菜单");
                        var action = new ChangeAction();
                        action.Type = "DeleteTree";
                        action.Hive = "CurrentUser";
                        action.KeyPath = @"Software\Classes\CLSID\{86CA1AA0-34AA-4E8B-A509-50C905BAE2A2}";
                        action.BackupFile = backup;
                        action.Note = "Windows 11 经典右键菜单开关";
                        group.Actions.Add(action);
                        History.Groups.Add(group);
                        SaveHistory();
                        Native.NotifyShellChanged();
                        return new OperationResult { Success = true, Message = "已恢复 Windows 11 现代右键菜单，重启资源管理器后生效。", Group = group };
                    }
                }
                catch (Exception ex)
                {
                    return Fail("切换失败：" + ex.Message);
                }
            }
        }

        private static void DeleteTreeWithBackup(RegistryHive hive, string keyPath, out string backupFile)
        {
            RegistryTree tree = RegistryBackup.Export(hive, keyPath);
            AppPaths.Ensure();
            backupFile = Path.Combine(AppPaths.BackupDir, Guid.NewGuid().ToString("N") + ".xml");
            var serializer = new XmlSerializer(typeof(RegistryTree));
            using (FileStream fs = new FileStream(backupFile, FileMode.Create, FileAccess.Write))
            {
                serializer.Serialize(fs, tree);
            }
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            {
                baseKey.DeleteSubKeyTree(keyPath, false);
            }
        }

        private static void RestoreRegistryBackup(string backupFile)
        {
            if (string.IsNullOrEmpty(backupFile) || !File.Exists(backupFile))
                throw new FileNotFoundException("备份文件不存在。");
            var serializer = new XmlSerializer(typeof(RegistryTree));
            RegistryTree tree;
            using (FileStream fs = File.OpenRead(backupFile))
            {
                tree = (RegistryTree)serializer.Deserialize(fs);
            }
            RegistryBackup.Restore(tree);
        }

        public static void RestartExplorer()
        {
            try
            {
                var kill = new ProcessStartInfo("taskkill.exe", "/f /im explorer.exe");
                kill.UseShellExecute = false;
                kill.CreateNoWindow = true;
                using (Process p = Process.Start(kill))
                {
                    if (p != null) p.WaitForExit(5000);
                }
                Thread.Sleep(700);
                var start = new ProcessStartInfo("explorer.exe");
                start.UseShellExecute = false;
                Process.Start(start);
            }
            catch
            {
            }
        }
    }

    [XmlRoot("RegistryTree")]
    public class RegistryTree
    {
        public string Hive { get; set; }
        public string KeyPath { get; set; }
        [XmlArray("Keys")]
        [XmlArrayItem("Key")]
        public List<RegistryTreeKey> Keys { get; set; }

        public RegistryTree()
        {
            Keys = new List<RegistryTreeKey>();
        }
    }

    public class RegistryTreeKey
    {
        public string RelativePath { get; set; }
        [XmlArray("Values")]
        [XmlArrayItem("Value")]
        public List<RegistryTreeValue> Values { get; set; }

        public RegistryTreeKey()
        {
            Values = new List<RegistryTreeValue>();
        }
    }

    public class RegistryTreeValue
    {
        public string Name { get; set; }
        public int Kind { get; set; }
        public string Data { get; set; }
    }

    public static class RegistryBackup
    {
        public static RegistryTree Export(RegistryHive hive, string keyPath)
        {
            var tree = new RegistryTree();
            tree.Hive = hive == RegistryHive.CurrentUser ? "CurrentUser" : "LocalMachine";
            tree.KeyPath = keyPath;
            using (RegistryKey key = Native.OpenKey(hive, keyPath, false))
            {
                if (key == null) throw new IOException("注册表项不存在：" + keyPath);
                ExportKey(key, string.Empty, tree);
            }
            return tree;
        }

        private static void ExportKey(RegistryKey key, string relative, RegistryTree tree)
        {
            var node = new RegistryTreeKey();
            node.RelativePath = relative;
            foreach (string name in Native.GetValueNamesSafe(key))
            {
                try
                {
                    RegistryValueKind kind = key.GetValueKind(name);
                    object val = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    var item = new RegistryTreeValue();
                    item.Name = name;
                    item.Kind = (int)kind;
                    if (val is byte[]) item.Data = Convert.ToBase64String((byte[])val);
                    else if (val is string[]) item.Data = string.Join("\0", (string[])val);
                    else item.Data = Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture);
                    node.Values.Add(item);
                }
                catch
                {
                }
            }
            tree.Keys.Add(node);
            foreach (string sub in Native.GetSubKeyNamesSafe(key))
            {
                using (RegistryKey child = key.OpenSubKey(sub))
                {
                    if (child != null)
                        ExportKey(child, string.IsNullOrEmpty(relative) ? sub : relative + "\\" + sub, tree);
                }
            }
        }

        public static void Restore(RegistryTree tree)
        {
            if (tree == null) return;
            RegistryHive hive = string.Equals(tree.Hive, "CurrentUser", StringComparison.OrdinalIgnoreCase)
                ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            {
                foreach (RegistryTreeKey node in tree.Keys)
                {
                    string path = tree.KeyPath;
                    if (!string.IsNullOrEmpty(node.RelativePath)) path += "\\" + node.RelativePath;
                    using (RegistryKey key = baseKey.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        if (key == null) continue;
                        foreach (RegistryTreeValue item in node.Values)
                        {
                            RegistryValueKind kind = (RegistryValueKind)item.Kind;
                            object value;
                            if (kind == RegistryValueKind.Binary) value = Convert.FromBase64String(item.Data ?? string.Empty);
                            else if (kind == RegistryValueKind.MultiString) value = (item.Data ?? string.Empty).Split(new[] { "\0" }, StringSplitOptions.None);
                            else if (kind == RegistryValueKind.DWord) value = unchecked((int)long.Parse(item.Data ?? "0"));
                            else if (kind == RegistryValueKind.QWord) value = long.Parse(item.Data ?? "0");
                            else value = item.Data ?? string.Empty;
                            key.SetValue(item.Name, value, kind);
                        }
                    }
                }
            }
        }
    }
}
