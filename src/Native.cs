using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Cmm
{
    public static class Native
    {
        public const string AppDisplayName = "右键菜单管理器";
        public const string BlockMarker = "由右键菜单管理器禁用";
        public const string BlockedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        public const string DisabledValueName = "LegacyDisable";
        public const string ProgrammaticValueName = "ProgrammaticAccessOnly";
        public const string ShellNewDisabledName = "ShellNew.__cmm_disabled";

        private static readonly object IconLock = new object();
        private static readonly Dictionary<string, ImageSource> IconCache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int AssocQueryString(int flags, int str, string pszAssoc, string pszExtra, StringBuilder pszOut, ref uint pcchOut);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegRenameKey(IntPtr hKey, string lpSubKeyName, string lpNewKeyName);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr hKey);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private const int SHGFI_ICON = 0x000000100;
        private const int SHGFI_LARGEICON = 0x000000000;
        private const int SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const int ASSOCSTR_FRIENDLYDOCNAME = 4;
        private const int ASSOCF_NONE = 0;
        private const int ASSOCF_INIT_IGNOREUNKNOWN = 0x00000400;
        private const int KEY_WOW64_64KEY = 0x0100;
        private const int KEY_READ = 0x20019;
        private const int KEY_WRITE = 0x20006;
        private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));

        public static RegistryKey OpenBase(RegistryHive hive)
        {
            return RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        }

        public static RegistryKey OpenKey(RegistryHive hive, string path, bool writable)
        {
            try
            {
                using (RegistryKey baseKey = OpenBase(hive))
                {
                    return baseKey.OpenSubKey(path, writable);
                }
            }
            catch
            {
                return null;
            }
        }

        public static string GetString(RegistryKey key, string name)
        {
            if (key == null) return null;
            try
            {
                object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value == null) return null;
                return value as string ?? value.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static bool ValueExists(RegistryKey key, string name)
        {
            if (key == null) return false;
            try
            {
                return key.GetValueNames().Any(delegate(string n)
                {
                    return string.Equals(n, name, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch
            {
                return false;
            }
        }

        public static string[] GetValueNamesSafe(RegistryKey key)
        {
            try { return key.GetValueNames(); }
            catch { return new string[0]; }
        }

        public static string[] GetSubKeyNamesSafe(RegistryKey key)
        {
            try { return key.GetSubKeyNames(); }
            catch { return new string[0]; }
        }

        public static string HiveLabel(RegistryHive hive)
        {
            return hive == RegistryHive.CurrentUser ? "HKEY_CURRENT_USER" : "HKEY_LOCAL_MACHINE";
        }

        public static string FullRegistryPath(RegistryHive hive, string path)
        {
            return HiveLabel(hive) + "\\" + path;
        }

        public static string ResolveMui(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            if (!(raw.StartsWith("@") || raw.StartsWith("@{", StringComparison.Ordinal)))
                return raw;
            try
            {
                var sb = new StringBuilder(2048);
                int hr = SHLoadIndirectString(raw, sb, sb.Capacity, IntPtr.Zero);
                if (hr == 0)
                {
                    string resolved = sb.ToString();
                    if (!string.IsNullOrEmpty(resolved) && !resolved.StartsWith("@", StringComparison.Ordinal))
                        return resolved;
                }
            }
            catch
            {
            }
            return raw;
        }

        public static string CleanLabel(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string s = ResolveMui(raw);
            if (string.IsNullOrEmpty(s)) return s;
            if (s.StartsWith("@", StringComparison.Ordinal) || s.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            s = Regex.Replace(s, @"\s*\(&[A-Za-z0-9]\)", string.Empty);
            s = Regex.Replace(s, @"&([A-Za-z0-9])", "$1");
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s;
        }

        public static string Expand(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            try { return Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')); }
            catch { return value; }
        }

        public static string NormalizeClsid(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string s = value.Trim();
            Guid g;
            if (Guid.TryParse(s, out g))
                return "{" + g.ToString("D").ToUpperInvariant() + "}";
            return s.ToUpperInvariant();
        }

        public static string NormalizeType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return ScopeKeys.AllFiles;
            type = type.Trim();
            if (type == "*" || type.StartsWith(".", StringComparison.Ordinal))
                return type.ToLowerInvariant();
            return type;
        }

        public static string FriendlyScope(string type)
        {
            if (string.IsNullOrEmpty(type)) return "所有文件";
            if (type == "*") return "所有文件";
            if (type.StartsWith(".", StringComparison.Ordinal)) return type.ToLowerInvariant();
            if (string.Equals(type, ScopeKeys.Directory, StringComparison.OrdinalIgnoreCase)) return "文件夹";
            if (string.Equals(type, ScopeKeys.DirectoryBackground, StringComparison.OrdinalIgnoreCase)) return "文件夹空白处";
            if (string.Equals(type, ScopeKeys.Drive, StringComparison.OrdinalIgnoreCase)) return "驱动器";
            if (string.Equals(type, ScopeKeys.DesktopBackground, StringComparison.OrdinalIgnoreCase)) return "桌面";
            if (string.Equals(type, ScopeKeys.AllFilesystemObjects, StringComparison.OrdinalIgnoreCase)) return "所有文件和文件夹";
            if (string.Equals(type, ScopeKeys.Folder, StringComparison.OrdinalIgnoreCase)) return "文件夹";
            return type;
        }

        public static List<string> CategoriesForScope(string scope)
        {
            var list = new List<string>();
            if (string.Equals(scope, ScopeKeys.AllFiles, StringComparison.OrdinalIgnoreCase))
                list.Add(Categories.Files);
            else if (string.Equals(scope, ScopeKeys.AllFilesystemObjects, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(Categories.Files);
                list.Add(Categories.Folders);
            }
            else if (string.Equals(scope, ScopeKeys.Directory, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scope, ScopeKeys.Folder, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scope, ScopeKeys.LibraryFolder, StringComparison.OrdinalIgnoreCase))
                list.Add(Categories.Folders);
            else if (string.Equals(scope, ScopeKeys.DirectoryBackground, StringComparison.OrdinalIgnoreCase))
                list.Add(Categories.Background);
            else if (string.Equals(scope, ScopeKeys.Drive, StringComparison.OrdinalIgnoreCase))
                list.Add(Categories.Drives);
            else if (string.Equals(scope, ScopeKeys.DesktopBackground, StringComparison.OrdinalIgnoreCase))
                list.Add(Categories.Desktop);
            else if (scope.StartsWith(".", StringComparison.Ordinal))
                list.Add(Categories.Files);
            return list;
        }

        public static string BuildScopeText(ICollection<string> scopes)
        {
            if (scopes == null || scopes.Count == 0) return "未标明范围";
            var order = new[] { "所有文件", "文件夹", "文件夹空白处", "驱动器", "桌面", "所有文件和文件夹" };
            var names = new List<string>();
            var ext = new List<string>();
            foreach (string s in scopes)
            {
                if (s.StartsWith(".", StringComparison.Ordinal)) ext.Add(s);
                else
                {
                    string f = FriendlyScope(s);
                    if (!names.Contains(f)) names.Add(f);
                }
            }
            names.Sort(delegate(string a, string b)
            {
                int ia = Array.IndexOf(order, a);
                int ib = Array.IndexOf(order, b);
                if (ia < 0) ia = 99;
                if (ib < 0) ib = 99;
                return ia.CompareTo(ib);
            });
            if (ext.Count > 0)
                names.Add(ext.Count <= 4 ? string.Join(" / ", ext.ToArray()) : string.Format("{0} 等 {1} 种文件", string.Join(" / ", ext.Take(3).ToArray()), ext.Count));
            return string.Join(" · ", names.ToArray());
        }

        public static string GetClassDisplayName(string clsid)
        {
            if (string.IsNullOrEmpty(clsid)) return null;
            using (RegistryKey baseKey = OpenBase(RegistryHive.ClassesRoot))
            {
                using (RegistryKey key = baseKey.OpenSubKey("CLSID\\" + clsid))
                {
                    if (key == null) return null;
                    string localized = CleanLabel(GetString(key, "LocalizedString"));
                    if (!string.IsNullOrEmpty(localized)) return localized;
                    string def = CleanLabel(GetString(key, string.Empty));
                    if (!string.IsNullOrEmpty(def)) return def;
                }
            }
            return null;
        }

        public static string GetClassServerPath(string clsid)
        {
            if (string.IsNullOrEmpty(clsid)) return null;
            using (RegistryKey baseKey = OpenBase(RegistryHive.ClassesRoot))
            {
                using (RegistryKey key = baseKey.OpenSubKey("CLSID\\" + clsid + "\\InprocServer32"))
                {
                    if (key == null) return null;
                    return Expand(GetString(key, string.Empty));
                }
            }
        }

        public static string GetFileProductName(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                string product = info.ProductName;
                if (string.IsNullOrWhiteSpace(product)) product = info.FileDescription;
                if (string.IsNullOrWhiteSpace(product)) product = info.CompanyName;
                if (string.IsNullOrWhiteSpace(product)) return null;
                return product.Trim();
            }
            catch
            {
                return null;
            }
        }

        public static string GetFileCompanyName(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                if (string.IsNullOrWhiteSpace(info.CompanyName)) return null;
                return info.CompanyName.Trim();
            }
            catch
            {
                return null;
            }
        }

        public static bool IsWindowsPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                return !string.IsNullOrEmpty(win) && path.StartsWith(win, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string GetExtensionFriendlyName(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return string.Empty;
            try
            {
                uint size = 2048;
                var sb = new StringBuilder((int)size);
                int hr = AssocQueryString(ASSOCF_NONE | ASSOCF_INIT_IGNOREUNKNOWN, ASSOCSTR_FRIENDLYDOCNAME, ext, null, sb, ref size);
                if (hr == 0 && sb.Length > 0) return sb.ToString();
            }
            catch
            {
            }
            return string.Empty;
        }

        public static string MakeIconLetter(string title)
        {
            if (string.IsNullOrEmpty(title)) return "?";
            string t = title.Trim();
            if (t.Length == 0) return "?";
            if (char.IsHighSurrogate(t[0]) && t.Length > 1) return t.Substring(0, 2).ToUpperInvariant();
            return t.Substring(0, 1).ToUpperInvariant();
        }

        public static ImageSource LoadPathIcon(string path, int index, string baseDir)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string expanded = Expand(path);
            if (string.IsNullOrEmpty(expanded)) return null;
            expanded = expanded.Trim().Trim('"');
            if (expanded.StartsWith("@", StringComparison.Ordinal)) expanded = expanded.Substring(1);
            if (!Path.IsPathRooted(expanded) && !string.IsNullOrEmpty(baseDir))
                expanded = Path.Combine(baseDir, expanded);
            if (!File.Exists(expanded))
                return null;
            string cacheKey = expanded.ToLowerInvariant() + "|" + index;
            lock (IconLock)
            {
                ImageSource cached;
                if (IconCache.TryGetValue(cacheKey, out cached)) return cached;
            }
            ImageSource result = null;
            IntPtr[] large = new IntPtr[1];
            IntPtr[] small = new IntPtr[1];
            try
            {
                uint got = ExtractIconEx(expanded, index, large, small, 1);
                if (got > 0 && large[0] != IntPtr.Zero)
                    result = IconHandleToSource(large[0]);
                else
                {
                    try
                    {
                        using (Icon icon = Icon.ExtractAssociatedIcon(expanded))
                        {
                            if (icon != null) result = IconToSource(icon);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
                if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
            }
            lock (IconLock) { IconCache[cacheKey] = result; }
            return result;
        }

        public static ImageSource LoadIconSpec(string spec, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            string s = Expand(spec).Trim();
            int index = 0;
            Match m = Regex.Match(s, @"^(?<path>.+?)(?:,(?<index>-?\d+))?$");
            if (m.Success)
            {
                s = m.Groups["path"].Value.Trim().Trim('"');
                int parsed;
                if (int.TryParse(m.Groups["index"].Value, out parsed)) index = Math.Abs(parsed) > 200 ? 0 : Math.Abs(parsed);
            }
            return LoadPathIcon(s, index, baseDir);
        }

        public static ImageSource LoadFileIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = "file:" + path.ToLowerInvariant();
            lock (IconLock)
            {
                ImageSource cached;
                if (IconCache.TryGetValue(key, out cached)) return cached;
            }
            ImageSource result = null;
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                var info = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;
                if (!File.Exists(path) && !Directory.Exists(path))
                    flags |= SHGFI_USEFILEATTRIBUTES;
                IntPtr ret = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(info), flags);
                if (ret != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    hIcon = info.hIcon;
                    result = IconHandleToSource(hIcon);
                }
            }
            catch
            {
            }
            finally
            {
                if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
            }
            lock (IconLock) { IconCache[key] = result; }
            return result;
        }

        public static ImageSource LoadExtensionIcon(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return null;
            return LoadFileIcon("cmm_icon" + (ext.StartsWith(".", StringComparison.Ordinal) ? ext : "." + ext));
        }

        private static ImageSource IconHandleToSource(IntPtr hIcon)
        {
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                if (source.CanFreeze) source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource IconToSource(Icon icon)
        {
            try
            {
                using (Bitmap bmp = icon.ToBitmap())
                {
                    IntPtr h = bmp.GetHbitmap();
                    try
                    {
                        BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                            h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                        if (source.CanFreeze) source.Freeze();
                        return source;
                    }
                    finally
                    {
                        DeleteObject(h);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool RelaunchElevated(string arguments, out string error)
        {
            error = null;
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = arguments ?? string.Empty;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) error = ex.Message;
                else error = "用户取消了管理员授权。";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string RenameSubKey(RegistryHive hive, string parentPath, string oldName, string newName)
        {
            IntPtr root = hive == RegistryHive.CurrentUser ? HKEY_CURRENT_USER : HKEY_LOCAL_MACHINE;
            IntPtr parent = IntPtr.Zero;
            try
            {
                int result = RegOpenKeyEx(root, parentPath, 0, KEY_READ | KEY_WRITE | KEY_WOW64_64KEY, out parent);
                if (result != 0)
                    return new Win32Exception(result).Message;
                result = RegRenameKey(parent, oldName, newName);
                if (result != 0)
                    return new Win32Exception(result).Message;
                return null;
            }
            finally
            {
                if (parent != IntPtr.Zero) RegCloseKey(parent);
            }
        }

        public static void NotifyShellChanged()
        {
            try { SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); }
            catch { }
        }

        public static void OpenRegeditAt(string fullPath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
                {
                    if (key != null) key.SetValue("LastKey", fullPath, RegistryValueKind.String);
                }
                var psi = new ProcessStartInfo("regedit.exe");
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch
            {
            }
        }

        public static void OpenFolder(string path)
        {
            try
            {
                var psi = new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"");
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch
            {
            }
        }

        public static void OpenUrl(string url)
        {
            try
            {
                var psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch
            {
            }
        }

        public static void EnableDpiAwareness()
        {
            try { SetProcessDPIAware(); }
            catch { }
        }
    }
}
