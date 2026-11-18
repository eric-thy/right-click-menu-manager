using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using Microsoft.Win32;

namespace Cmm
{
    public enum EntryKind
    {
        Modern,
        Classic,
        ShellExtension,
        NewMenu,
        SendTo,
        Custom,
        OrphanBlock
    }

    public enum EntryFilter
    {
        All,
        Enabled,
        Disabled
    }

    public static class Categories
    {
        public const string Overview = "overview";
        public const string All = "all";
        public const string Files = "files";
        public const string Folders = "folders";
        public const string Background = "background";
        public const string Drives = "drives";
        public const string Desktop = "desktop";
        public const string FileTypes = "filetypes";
        public const string Modern = "modern";
        public const string Classic = "classic";
        public const string ShellExt = "shellext";
        public const string NewMenu = "newmenu";
        public const string SendTo = "sendto";
        public const string Custom = "custom";
        public const string Disabled = "disabled";
        public const string Settings = "settings";
    }

    public static class ScopeKeys
    {
        public const string AllFiles = "*";
        public const string AllFilesystemObjects = "AllFilesystemObjects";
        public const string Directory = "Directory";
        public const string DirectoryBackground = "Directory\\Background";
        public const string Drive = "Drive";
        public const string DesktopBackground = "DesktopBackground";
        public const string Folder = "Folder";
        public const string LibraryFolder = "LibraryFolder";
        public const string Network = "Network";
    }

    public class NavItem : INotifyPropertyChanged
    {
        private string _title;
        private string _glyph;
        private string _category;
        private bool _isHeader;
        private bool _isSelected;
        private int _count;

        public string Title
        {
            get { return _title; }
            set { _title = value; OnPropertyChanged("Title"); }
        }

        public string Glyph
        {
            get { return _glyph; }
            set { _glyph = value; OnPropertyChanged("Glyph"); }
        }

        public string Category
        {
            get { return _category; }
            set { _category = value; OnPropertyChanged("Category"); }
        }

        public bool IsHeader
        {
            get { return _isHeader; }
            set { _isHeader = value; OnPropertyChanged("IsHeader"); OnPropertyChanged("IsNav"); }
        }

        public bool IsNav
        {
            get { return !_isHeader; }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged("IsSelected"); }
        }

        public int Count
        {
            get { return _count; }
            set { _count = value; OnPropertyChanged("Count"); OnPropertyChanged("HasCount"); }
        }

        public bool HasCount
        {
            get { return _count > 0; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }

    public class ClassicVerbInfo
    {
        public RegistryHive Hive;
        public string KeyPath;
        public string VerbName;
        public bool IsCustom;
        public string CustomCommand;
        public string CustomScope;
    }

    public class HandlerInfo
    {
        public string Clsid;
        public bool Modern;
        public bool ShellExtension;
        public List<string> PackageNames = new List<string>();
        public List<string> VerbIds = new List<string>();
        public List<string> HandlerPaths = new List<string>();
    }

    public class NewMenuInfo
    {
        public RegistryHive Hive;
        public string ExtensionKeyPath;
        public string Extension;
        public string ActiveSubKey;
        public string DisabledSubKey;
    }

    public class SendToInfo
    {
        public string OriginalPath;
        public string BackupPath;
        public bool Common;
        public string SidecarPath;
    }

    public class MenuEntry : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private bool _isBusy;
        private bool _isSelected;
        private int _sortOrder;

        public string Id { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Description { get; set; }
        public EntryKind PrimaryKind { get; set; }
        public HashSet<EntryKind> Kinds { get; set; }
        public HashSet<string> Categories { get; set; }
        public HashSet<string> Types { get; set; }
        public string Badge { get; set; }
        public Brush BadgeBackground { get; set; }
        public Brush BadgeForeground { get; set; }
        public ImageSource Icon { get; set; }
        public string IconLetter { get; set; }
        public Brush IconBackground { get; set; }
        public string ScopeText { get; set; }
        public string TechnicalPath { get; set; }
        public string Command { get; set; }
        public string Clsid { get; set; }
        public string PackageDisplay { get; set; }
        public string HandlerPath { get; set; }
        public string DetailsNote { get; set; }
        public string DisabledReason { get; set; }
        public string VendorText { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool IsSystem { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsCustom { get; set; }
        public bool IsOrphan { get; set; }
        public string SearchText { get; set; }
        public object Backend { get; set; }
        public List<KeyValuePair<string, string>> DetailRows { get; set; }
        public string ActionHint { get; set; }
        public bool IsExternalDisabled { get; set; }

        public MenuEntry()
        {
            Kinds = new HashSet<EntryKind>();
            Categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IsAvailable = true;
            DetailRows = new List<KeyValuePair<string, string>>();
        }

        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                OnPropertyChanged("IsEnabled");
                OnPropertyChanged("StateText");
                OnPropertyChanged("StateBrush");
                OnPropertyChanged("SwitchHint");
                OnPropertyChanged("ToggleAutomationName");
            }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set { _isBusy = value; OnPropertyChanged("IsBusy"); OnPropertyChanged("SwitchHint"); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged("IsSelected"); }
        }

        public int SortOrder
        {
            get { return _sortOrder; }
            set { _sortOrder = value; }
        }

        public string StateText
        {
            get
            {
                if (IsOrphan) return "阻止项";
                return IsEnabled ? "已启用" : "已禁用";
            }
        }

        public Brush StateBrush
        {
            get
            {
                if (IsOrphan) return Theme.MutedText;
                return IsEnabled ? Theme.Success : Theme.Danger;
            }
        }

        public bool HasIcon
        {
            get { return Icon != null; }
        }

        public string SwitchHint
        {
            get
            {
                if (IsBusy) return "处理中…";
                return IsEnabled ? "点击禁用" : "点击启用";
            }
        }

        public string DetailsAutomationName
        {
            get { return "查看 " + (Title ?? "项目") + " 的详情"; }
        }

        public string ToggleAutomationName
        {
            get { return (IsEnabled ? "禁用 " : "启用 ") + (Title ?? "项目"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        public void RefreshVisualState()
        {
            OnPropertyChanged("IsEnabled");
            OnPropertyChanged("StateText");
            OnPropertyChanged("StateBrush");
            OnPropertyChanged("IsExternalDisabled");
            OnPropertyChanged("DisabledReason");
        }
    }

    public static class Theme
    {
        public static readonly Brush Accent = Brush(0, 103, 192);
        public static readonly Brush AccentHover = Brush(0, 86, 163);
        public static readonly Brush AccentSoft = Brush(232, 241, 251);
        public static readonly Brush WindowBackground = Brush(243, 245, 248);
        public static readonly Brush Surface = Brush(255, 255, 255);
        public static readonly Brush SurfaceSoft = Brush(248, 249, 251);
        public static readonly Brush Border = Brush(229, 233, 239);
        public static readonly Brush BorderStrong = Brush(212, 218, 227);
        public static readonly Brush Text = Brush(26, 32, 44);
        public static readonly Brush MutedText = Brush(101, 111, 126);
        public static readonly Brush FaintText = Brush(145, 154, 168);
        public static readonly Brush Success = Brush(16, 124, 65);
        public static readonly Brush SuccessSoft = Brush(232, 247, 238);
        public static readonly Brush Danger = Brush(196, 43, 28);
        public static readonly Brush DangerSoft = Brush(253, 236, 234);
        public static readonly Brush Warning = Brush(157, 93, 0);
        public static readonly Brush WarningSoft = Brush(255, 246, 224);
        public static readonly Brush ModernBadge = Brush(230, 240, 253);
        public static readonly Brush ModernBadgeText = Brush(21, 91, 171);
        public static readonly Brush ClassicBadge = Brush(241, 243, 246);
        public static readonly Brush ClassicBadgeText = Brush(80, 91, 106);
        public static readonly Brush ShellBadge = Brush(242, 236, 252);
        public static readonly Brush ShellBadgeText = Brush(105, 65, 174);
        public static readonly Brush NewBadge = Brush(234, 247, 239);
        public static readonly Brush NewBadgeText = Brush(21, 115, 70);
        public static readonly Brush SendBadge = Brush(255, 244, 229);
        public static readonly Brush SendBadgeText = Brush(148, 87, 5);
        public static readonly Brush CustomBadge = Brush(231, 246, 248);
        public static readonly Brush CustomBadgeText = Brush(12, 104, 117);

        public static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public static SolidColorBrush Brush(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }
}
