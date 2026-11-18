using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Cmm
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly List<MenuEntry> _allEntries = new List<MenuEntry>();
        private List<MenuEntry> _fileTypeEntries = new List<MenuEntry>();
        private List<string> _warnings = new List<string>();
        private bool _isRefreshing;
        private bool _refreshQueued;
        private string _lastRefreshError;
        private int _refreshAppliedCount;
        private readonly Dispatcher _dispatcher;
        private bool _suppressFileTypeLoad;
        private string _currentCategory = Categories.Overview;
        private string _searchText = string.Empty;
        private EntryFilter _filter = EntryFilter.All;
        private bool _isBusy;
        private string _busyText = "正在扫描右键菜单…";
        private bool _isElevated;
        private bool _isClassicMenu;
        private string _pageTitle = "概览";
        private string _pageSubtitle = string.Empty;
        private string _emptyText = "这里没有匹配的项目";
        private string _visibleCountText = "显示 0 项";
        private string _permissionText;
        private string _permissionHint;
        private Brush _permissionBrush;
        private string _menuStyleText;
        private string _historySummary;
        private string _historyCountText;
        private bool _hasHistory;
        private FileTypeOption _selectedFileType;
        private int _allCount;
        private int _enabledCount;
        private int _disabledCount;
        private int _modernCount;
        private int _classicCount;
        private int _shellCount;
        private int _newCount;
        private int _sendToCount;
        private int _customCount;
        private int _externalDisabledCount;
        private int _thirdPartyCount;
        private int _filesCount;
        private int _foldersCount;
        private int _backgroundCount;
        private int _drivesCount;
        private int _desktopCount;

        public ObservableCollection<MenuEntry> Entries { get; private set; }
        public ObservableCollection<NavItem> NavItems { get; private set; }
        public ObservableCollection<FileTypeOption> FileTypes { get; private set; }
        public ObservableCollection<ChangeGroup> RecentChanges { get; private set; }

        public ICommand NavigateCommand { get; private set; }
        public ICommand GoToCategoryCommand { get; private set; }
        public ICommand ToggleCommand { get; private set; }
        public ICommand DetailsCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand UndoCommand { get; private set; }
        public ICommand RestoreAllCommand { get; private set; }
        public ICommand ElevateCommand { get; private set; }
        public ICommand RestartExplorerCommand { get; private set; }
        public ICommand SetFilterCommand { get; private set; }
        public ICommand AddCustomCommand { get; private set; }
        public ICommand OpenDataFolderCommand { get; private set; }
        public ICommand ShowWarningsCommand { get; private set; }
        public ICommand UseModernMenuCommand { get; private set; }
        public ICommand UseClassicMenuCommand { get; private set; }

        public event Action<ToastRequest> ToastRequested;
        public event Action<MenuEntry> DetailsRequested;
        public event Action<DialogRequest> DialogRequested;
        public event Action CustomRequested;

        public MainViewModel()
        {
            _dispatcher = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            Entries = new ObservableCollection<MenuEntry>();
            NavItems = new ObservableCollection<NavItem>();
            FileTypes = new ObservableCollection<FileTypeOption>();
            RecentChanges = new ObservableCollection<ChangeGroup>();
            BuildNavigation();
            _isElevated = Native.IsElevated();
            _isClassicMenu = Manager.IsClassicMenuActive();
            UpdatePermission();
            UpdateMenuStyle();
            _historySummary = string.Empty;
            _historyCountText = "0 条生效中的更改";
            _permissionText = _isElevated ? "管理员权限" : "标准权限";
            _permissionHint = _isElevated ? "可以修改系统级项目" : "系统级项目需要提升权限";
            _permissionBrush = _isElevated ? Theme.Success : Theme.Warning;

            NavigateCommand = new RelayCommand(delegate(object parameter)
            {
                NavItem item = parameter as NavItem;
                if (item != null) NavigateToCategory(item.Category);
            });
            GoToCategoryCommand = new RelayCommand(delegate(object parameter)
            {
                string category = Convert.ToString(parameter);
                if (!string.IsNullOrEmpty(category)) NavigateToCategory(category);
            });
            ToggleCommand = new RelayCommand(delegate(object parameter)
            {
                MenuEntry entry = parameter as MenuEntry;
                if (entry != null) ToggleEntry(entry);
            });
            DetailsCommand = new RelayCommand(delegate(object parameter)
            {
                MenuEntry entry = parameter as MenuEntry;
                if (entry != null) RequestDetails(entry);
            });
            RefreshCommand = new RelayCommand(delegate(object parameter) { RefreshCommandExec(); });
            UndoCommand = new RelayCommand(delegate(object parameter) { UndoCommandExec(); });
            RestoreAllCommand = new RelayCommand(delegate(object parameter) { RestoreAllCommandExec(); });
            ElevateCommand = new RelayCommand(delegate(object parameter) { Elevate(); }, delegate(object parameter) { return !IsElevated; });
            RestartExplorerCommand = new RelayCommand(delegate(object parameter) { RestartExplorerCommandExec(); });
            SetFilterCommand = new RelayCommand(delegate(object parameter)
            {
                string value = Convert.ToString(parameter);
                EntryFilter newFilter;
                if (Enum.TryParse(value, true, out newFilter))
                {
                    Filter = newFilter;
                }
            });
            AddCustomCommand = new RelayCommand(delegate(object parameter)
            {
                var h = CustomRequested;
                if (h != null) h();
            });
            OpenDataFolderCommand = new RelayCommand(delegate(object parameter) { OpenFolder(AppPaths.DataDir); });
            ShowWarningsCommand = new RelayCommand(delegate(object parameter) { ShowWarnings(); });
            UseModernMenuCommand = new RelayCommand(delegate(object parameter) { SwitchMenuStyle(false); });
            UseClassicMenuCommand = new RelayCommand(delegate(object parameter) { SwitchMenuStyle(true); });
        }

        private void BuildNavigation()
        {
            AddHeader("概览");
            AddNav(Categories.Overview, "仪表盘", "\uE80F");
            AddNav(Categories.All, "全部项目", "\uE8FD");
            AddNav(Categories.Disabled, "已禁用", "\uE711");
            AddHeader("右键位置");
            AddNav(Categories.Files, "文件", "\uE8A5");
            AddNav(Categories.Folders, "文件夹", "\uE8B7");
            AddNav(Categories.Background, "文件夹空白处", "\uE838");
            AddNav(Categories.Drives, "驱动器", "\uEDA2");
            AddNav(Categories.Desktop, "桌面", "\uE7F4");
            AddNav(Categories.FileTypes, "按文件类型", "\uE8B7");
            AddHeader("菜单来源");
            AddNav(Categories.Modern, "Win11 现代菜单", "\uE945");
            AddNav(Categories.Classic, "经典菜单", "\uE8FD");
            AddNav(Categories.ShellExt, "Shell 扩展", "\uE74C");
            AddNav(Categories.NewMenu, "新建菜单", "\uE710");
            AddNav(Categories.SendTo, "发送到", "\uE72D");
            AddNav(Categories.Custom, "我的自定义", "\uE70F");
            AddHeader("工具");
            AddNav(Categories.Settings, "设置与备份", "\uE713");
            foreach (NavItem item in NavItems) item.IsSelected = item.Category == Categories.Overview;
        }

        private void AddHeader(string title)
        {
            NavItems.Add(new NavItem { Title = title, IsHeader = true });
        }

        private void AddNav(string category, string title, string glyph)
        {
            NavItems.Add(new NavItem { Category = category, Title = title, Glyph = glyph, IsHeader = false });
        }

        public string CurrentCategory
        {
            get { return _currentCategory; }
        }

        public bool IsDashboard
        {
            get { return _currentCategory == Categories.Overview; }
        }

        public bool IsSettings
        {
            get { return _currentCategory == Categories.Settings; }
        }

        public bool IsList
        {
            get { return !IsDashboard && !IsSettings; }
        }

        public bool ShowSearch
        {
            get { return IsList; }
        }

        public bool ShowFilterBar
        {
            get { return IsList; }
        }

        public bool ShowFileTypeBar
        {
            get { return _currentCategory == Categories.FileTypes; }
        }

        public bool ShowAddCustom
        {
            get { return _currentCategory == Categories.Custom; }
        }

        public bool HasEntries
        {
            get { return Entries.Count > 0; }
        }

        public bool IsEmpty
        {
            get { return !HasEntries && !IsBusy; }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged("IsBusy");
                OnPropertyChanged("IsEmpty");
            }
        }

        public string BusyText
        {
            get { return _busyText; }
            set { _busyText = value; OnPropertyChanged("BusyText"); }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                string next = value ?? string.Empty;
                if (_searchText == next) return;
                _searchText = next;
                OnPropertyChanged("SearchText");
                OnPropertyChanged("IsSearchEmpty");
                RefreshVisible();
            }
        }

        public bool IsSearchEmpty
        {
            get { return string.IsNullOrEmpty(_searchText); }
        }

        public EntryFilter Filter
        {
            get { return _filter; }
            set
            {
                if (_filter == value) return;
                _filter = value;
                OnPropertyChanged("Filter");
                RefreshVisible();
            }
        }

        public FileTypeOption SelectedFileType
        {
            get { return _selectedFileType; }
            set
            {
                if (ReferenceEquals(_selectedFileType, value)) return;
                _selectedFileType = value;
                OnPropertyChanged("SelectedFileType");
                if (!_suppressFileTypeLoad && _currentCategory == Categories.FileTypes)
                    LoadFileTypeEntries();
            }
        }

        public bool IsElevated
        {
            get { return _isElevated; }
        }

        public bool CanElevate
        {
            get { return !_isElevated; }
        }

        public string PermissionText
        {
            get { return _permissionText; }
        }

        public string PermissionHint
        {
            get { return _permissionHint; }
        }

        public string PermissionPillText
        {
            get { return _isElevated ? "管理员" : "标准权限"; }
        }

        public Brush PermissionBrush
        {
            get { return _permissionBrush; }
        }

        public string PageTitle
        {
            get { return _pageTitle; }
        }

        public string PageSubtitle
        {
            get { return _pageSubtitle; }
        }

        public string VisibleCountText
        {
            get { return _visibleCountText; }
        }

        public string EmptyText
        {
            get { return _emptyText; }
        }

        public int AllCount { get { return _allCount; } }
        public int EnabledCount { get { return _enabledCount; } }
        public int DisabledCount { get { return _disabledCount; } }
        public int ModernCount { get { return _modernCount; } }
        public int ClassicCount { get { return _classicCount; } }
        public int ShellCount { get { return _shellCount; } }
        public int NewCount { get { return _newCount; } }
        public int SendToCount { get { return _sendToCount; } }
        public int CustomCount { get { return _customCount; } }
        public int ExternalDisabledCount { get { return _externalDisabledCount; } }
        public int ThirdPartyCount { get { return _thirdPartyCount; } }
        public int FilesCount { get { return _filesCount; } }
        public int FoldersCount { get { return _foldersCount; } }
        public int BackgroundCount { get { return _backgroundCount; } }
        public int DrivesCount { get { return _drivesCount; } }
        public int DesktopCount { get { return _desktopCount; } }

        public bool HasWarnings
        {
            get { return _warnings.Count > 0; }
        }

        public string WarningsText
        {
            get { return _warnings.Count == 0 ? "没有扫描警告" : string.Format("{0} 条扫描警告", _warnings.Count); }
        }

        public string MenuStyleText
        {
            get { return _menuStyleText; }
        }

        public bool IsClassicMenu
        {
            get { return _isClassicMenu; }
        }

        public string HistorySummary
        {
            get { return _historySummary; }
        }

        public string HistoryCountText
        {
            get { return _historyCountText; }
        }

        public bool HasHistory
        {
            get { return _hasHistory; }
        }

        public bool HasActiveChanges
        {
            get { return Manager.ActiveChangeCount > 0; }
        }

        public string VersionText
        {
            get { return "版本 1.0.0"; }
        }

        public string LastRefreshError
        {
            get { return _lastRefreshError; }
        }

        public int RefreshAppliedCount
        {
            get { return _refreshAppliedCount; }
        }

        public string DataPath
        {
            get { return AppPaths.DataDir; }
        }

        private void UpdatePermission()
        {
            _isElevated = Native.IsElevated();
            _permissionText = _isElevated ? "管理员权限" : "标准权限";
            _permissionHint = _isElevated ? "可修改所有用户和系统级菜单" : "系统级项目需要提升权限";
            _permissionBrush = _isElevated ? Theme.Success : Theme.Warning;
            OnPropertyChanged("IsElevated");
            OnPropertyChanged("CanElevate");
            OnPropertyChanged("PermissionText");
            OnPropertyChanged("PermissionHint");
            OnPropertyChanged("PermissionPillText");
            OnPropertyChanged("PermissionBrush");
        }

        private void UpdateMenuStyle()
        {
            _isClassicMenu = Manager.IsClassicMenuActive();
            _menuStyleText = _isClassicMenu ? "经典右键菜单（Windows 10 样式）" : "Windows 11 现代右键菜单";
            OnPropertyChanged("IsClassicMenu");
            OnPropertyChanged("MenuStyleText");
        }

        public void NavigateToCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return;
            category = category.Trim();
            if (!IsKnownCategory(category)) category = Categories.All;
            _currentCategory = category;
            if (!string.IsNullOrEmpty(_searchText))
            {
                _searchText = string.Empty;
                OnPropertyChanged("SearchText");
                OnPropertyChanged("IsSearchEmpty");
            }
            OnPropertyChanged("CurrentCategory");
            OnPropertyChanged("IsDashboard");
            OnPropertyChanged("IsSettings");
            OnPropertyChanged("IsList");
            OnPropertyChanged("ShowSearch");
            OnPropertyChanged("ShowFilterBar");
            OnPropertyChanged("ShowFileTypeBar");
            OnPropertyChanged("ShowAddCustom");
            foreach (NavItem item in NavItems)
                item.IsSelected = !item.IsHeader && item.Category == _currentCategory;
            if (_currentCategory == Categories.FileTypes)
            {
                EnsureFileTypeSelection();
                LoadFileTypeEntries();
            }
            RefreshVisible();
        }

        private static bool IsKnownCategory(string category)
        {
            return category == Categories.Overview || category == Categories.All ||
                category == Categories.Files || category == Categories.Folders ||
                category == Categories.Background || category == Categories.Drives ||
                category == Categories.Desktop || category == Categories.FileTypes ||
                category == Categories.Modern || category == Categories.Classic ||
                category == Categories.ShellExt || category == Categories.NewMenu ||
                category == Categories.SendTo || category == Categories.Custom ||
                category == Categories.Disabled || category == Categories.Settings;
        }

        private void EnsureFileTypeSelection()
        {
            if (_selectedFileType != null) return;
            if (FileTypes.Count == 0) return;
            _suppressFileTypeLoad = true;
            FileTypeOption preferred = FileTypes.FirstOrDefault(delegate(FileTypeOption o) { return o.Key == ".txt"; });
            _selectedFileType = preferred ?? FileTypes[0];
            OnPropertyChanged("SelectedFileType");
            _suppressFileTypeLoad = false;
        }

        private void LoadFileTypeEntries()
        {
            if (_allEntries.Count == 0) return;
            if (_selectedFileType == null) return;
            try
            {
                List<string> warnings = new List<string>();
                List<MenuEntry> list = Scanner.ScanFileType(_selectedFileType.Key, _allEntries, warnings, true);
                _fileTypeEntries = list;
                if (warnings.Count > 0 && _warnings.Count < 25)
                    _warnings.AddRange(warnings);
                OnPropertyChanged("HasWarnings");
                OnPropertyChanged("WarningsText");
            }
            catch (Exception ex)
            {
                ShowToast("读取文件类型菜单失败：" + ex.Message, false, true);
            }
            RefreshVisible();
        }

        private IEnumerable<MenuEntry> BaseListForCategory()
        {
            if (_currentCategory == Categories.FileTypes) return _fileTypeEntries;
            return _allEntries;
        }

        private IEnumerable<MenuEntry> FilteredBase()
        {
            IEnumerable<MenuEntry> source = BaseListForCategory();
            if (_currentCategory == Categories.FileTypes)
                return source;
            if (_currentCategory == Categories.All) return source;
            if (_currentCategory == Categories.Disabled) return source.Where(delegate(MenuEntry e) { return !e.IsEnabled; });
            return source.Where(delegate(MenuEntry e) { return e.Categories.Contains(_currentCategory); });
        }

        private void RefreshVisible()
        {
            IEnumerable<MenuEntry> source = FilteredBase();
            if (_filter == EntryFilter.Enabled)
                source = source.Where(delegate(MenuEntry e) { return e.IsEnabled; });
            else if (_filter == EntryFilter.Disabled)
                source = source.Where(delegate(MenuEntry e) { return !e.IsEnabled; });
            string query = (_searchText ?? string.Empty).Trim().ToLowerInvariant();
            if (query.Length > 0)
            {
                string[] tokens = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                source = source.Where(delegate(MenuEntry e)
                {
                    string text = e.SearchText ?? string.Empty;
                    return tokens.All(delegate(string t) { return text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0; });
                });
            }
            List<MenuEntry> list = source
                .OrderBy(delegate(MenuEntry e) { return EntryRank(e); })
                .ThenByDescending(delegate(MenuEntry e) { return e.IsEnabled; })
                .ThenBy(delegate(MenuEntry e) { return e.Title; }, StringComparer.CurrentCulture)
                .ToList();
            Entries.Clear();
            foreach (MenuEntry entry in list) Entries.Add(entry);
            _visibleCountText = string.Format("显示 {0} 项", Entries.Count);
            OnPropertyChanged("VisibleCountText");
            OnPropertyChanged("HasEntries");
            OnPropertyChanged("IsEmpty");
            UpdateHeader();
        }

        private static int EntryRank(MenuEntry entry)
        {
            if (entry.Kinds.Contains(EntryKind.Modern)) return 0;
            if (entry.Kinds.Contains(EntryKind.ShellExtension)) return 1;
            if (entry.Kinds.Contains(EntryKind.Classic)) return 2;
            if (entry.Kinds.Contains(EntryKind.NewMenu)) return 3;
            if (entry.Kinds.Contains(EntryKind.SendTo)) return 4;
            if (entry.Kinds.Contains(EntryKind.Custom)) return 5;
            return 6;
        }

        private void UpdateHeader()
        {
            _pageTitle = TitleForCategory(_currentCategory);
            int total = FilteredBase().Count();
            int enabled = FilteredBase().Count(delegate(MenuEntry e) { return e.IsEnabled; });
            int disabled = total - enabled;
            if (_currentCategory == Categories.Overview)
            {
                _pageSubtitle = string.Format("已扫描 {0} 个菜单项 · {1} 个 Shell 扩展 · {2} 个已禁用", _allCount, _shellCount, _disabledCount);
            }
            else if (_currentCategory == Categories.Settings)
            {
                _pageSubtitle = "管理权限、备份、资源管理器与菜单样式";
            }
            else if (_currentCategory == Categories.FileTypes)
            {
                string name = _selectedFileType == null ? "文件类型" : _selectedFileType.Display;
                _pageSubtitle = string.Format("{0} · {1} 个专属项目 · 当前显示 {2} 个", name, total, Entries.Count);
            }
            else if (_searchText.Trim().Length > 0)
            {
                _pageSubtitle = string.Format("搜索到 {0} 个结果 · 该分类共 {1} 个", Entries.Count, total);
            }
            else
            {
                _pageSubtitle = string.Format("共 {0} 个 · {1} 个启用 · {2} 个禁用", total, enabled, disabled);
            }
            _emptyText = EmptyTextForCategory(_currentCategory);
            OnPropertyChanged("PageTitle");
            OnPropertyChanged("PageSubtitle");
            OnPropertyChanged("EmptyText");
        }

        private static string TitleForCategory(string category)
        {
            switch (category)
            {
                case Categories.Overview: return "概览";
                case Categories.All: return "全部项目";
                case Categories.Files: return "文件右键菜单";
                case Categories.Folders: return "文件夹右键菜单";
                case Categories.Background: return "文件夹空白处";
                case Categories.Drives: return "驱动器右键菜单";
                case Categories.Desktop: return "桌面右键菜单";
                case Categories.FileTypes: return "按文件类型管理";
                case Categories.Modern: return "Windows 11 现代菜单";
                case Categories.Classic: return "经典右键菜单";
                case Categories.ShellExt: return "Shell 扩展";
                case Categories.NewMenu: return "新建菜单";
                case Categories.SendTo: return "发送到菜单";
                case Categories.Custom: return "我的自定义项";
                case Categories.Disabled: return "已禁用项目";
                case Categories.Settings: return "设置与备份";
                default: return "全部项目";
            }
        }

        private static string EmptyTextForCategory(string category)
        {
            switch (category)
            {
                case Categories.Custom: return "还没有自定义菜单项。点击右上角的“新建”添加一个。";
                case Categories.FileTypes: return "这个文件类型没有专属的右键菜单项。";
                case Categories.Disabled: return "当前没有已禁用的项目。";
                case Categories.Modern: return "没有找到 Windows 11 现代菜单项。";
                case Categories.ShellExt: return "没有找到 Shell 扩展。";
                case Categories.NewMenu: return "没有找到新建菜单项。";
                case Categories.SendTo: return "发送到菜单中没有项目。";
                default: return "这个分类中还没有匹配的项目。";
            }
        }

        private void UpdateCounts()
        {
            _allCount = _allEntries.Count;
            _enabledCount = _allEntries.Count(delegate(MenuEntry e) { return e.IsEnabled; });
            _disabledCount = _allCount - _enabledCount;
            _modernCount = _allEntries.Count(delegate(MenuEntry e) { return e.Kinds.Contains(EntryKind.Modern); });
            _classicCount = _allEntries.Count(delegate(MenuEntry e) { return e.Kinds.Contains(EntryKind.Classic); });
            _shellCount = _allEntries.Count(delegate(MenuEntry e) { return e.Kinds.Contains(EntryKind.ShellExtension); });
            _newCount = _allEntries.Count(delegate(MenuEntry e) { return e.Kinds.Contains(EntryKind.NewMenu); });
            _sendToCount = _allEntries.Count(delegate(MenuEntry e) { return e.Kinds.Contains(EntryKind.SendTo); });
            _customCount = _allEntries.Count(delegate(MenuEntry e) { return e.Kinds.Contains(EntryKind.Custom); });
            _externalDisabledCount = _allEntries.Count(delegate(MenuEntry e) { return !e.IsEnabled && e.IsExternalDisabled; });
            _thirdPartyCount = _allEntries.Count(delegate(MenuEntry e) { return !e.IsSystem && !e.IsOrphan && !e.Kinds.Contains(EntryKind.Custom); });
            _filesCount = _allEntries.Count(delegate(MenuEntry e) { return e.Categories.Contains(Categories.Files); });
            _foldersCount = _allEntries.Count(delegate(MenuEntry e) { return e.Categories.Contains(Categories.Folders); });
            _backgroundCount = _allEntries.Count(delegate(MenuEntry e) { return e.Categories.Contains(Categories.Background); });
            _drivesCount = _allEntries.Count(delegate(MenuEntry e) { return e.Categories.Contains(Categories.Drives); });
            _desktopCount = _allEntries.Count(delegate(MenuEntry e) { return e.Categories.Contains(Categories.Desktop); });

            string[] names = new[]
            {
                "AllCount", "EnabledCount", "DisabledCount", "ModernCount", "ClassicCount",
                "ShellCount", "NewCount", "SendToCount", "CustomCount", "ExternalDisabledCount",
                "ThirdPartyCount", "FilesCount", "FoldersCount", "BackgroundCount", "DrivesCount", "DesktopCount"
            };
            foreach (string name in names) OnPropertyChanged(name);
            foreach (NavItem item in NavItems)
            {
                if (item.IsHeader) continue;
                item.Count = CountForCategory(item.Category);
            }
        }

        private int CountForCategory(string category)
        {
            switch (category)
            {
                case Categories.All: return _allEntries.Count;
                case Categories.Files: return _filesCount;
                case Categories.Folders: return _foldersCount;
                case Categories.Background: return _backgroundCount;
                case Categories.Drives: return _drivesCount;
                case Categories.Desktop: return _desktopCount;
                case Categories.Modern: return _modernCount;
                case Categories.Classic: return _classicCount;
                case Categories.ShellExt: return _shellCount;
                case Categories.NewMenu: return _newCount;
                case Categories.SendTo: return _sendToCount;
                case Categories.Custom: return _customCount;
                case Categories.Disabled: return _disabledCount;
                default: return 0;
            }
        }

        private void RunOnUi(Action action)
        {
            if (_dispatcher.CheckAccess()) action();
            else _dispatcher.Invoke(action);
        }

        private Task RunOnUiAsync(Action action)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
                return Task.FromResult(0);
            }
            return _dispatcher.InvokeAsync(action).Task;
        }

        private void ApplyScanBundle(ScanResult result, List<FileTypeOption> options)
        {
            _allEntries.Clear();
            _allEntries.AddRange(result.Entries);
            _warnings = result.Warnings ?? new List<string>();
            OnPropertyChanged("HasWarnings");
            OnPropertyChanged("WarningsText");

            string previousKey = _selectedFileType == null ? null : _selectedFileType.Key;
            _suppressFileTypeLoad = true;
            FileTypes.Clear();
            foreach (FileTypeOption option in options) FileTypes.Add(option);
            FileTypeOption selected = null;
            if (!string.IsNullOrEmpty(previousKey))
                selected = FileTypes.FirstOrDefault(delegate(FileTypeOption o) { return string.Equals(o.Key, previousKey, StringComparison.OrdinalIgnoreCase); });
            if (selected == null)
                selected = FileTypes.FirstOrDefault(delegate(FileTypeOption o) { return o.Key == ".txt"; });
            if (selected == null && FileTypes.Count > 0) selected = FileTypes[0];
            _selectedFileType = selected;
            OnPropertyChanged("SelectedFileType");
            _suppressFileTypeLoad = false;

            UpdateCounts();
            RefreshHistory();
            if (_currentCategory == Categories.FileTypes) LoadFileTypeEntries();
            else RefreshVisible();
            _refreshAppliedCount++;
            OnPropertyChanged("RefreshAppliedCount");
        }

        public async Task RefreshAsync()
        {
            if (_isRefreshing)
            {
                _refreshQueued = true;
                return;
            }
            _isRefreshing = true;
            try
            {
                do
                {
                    _refreshQueued = false;
                    RunOnUi(delegate
                    {
                        IsBusy = true;
                        BusyText = "正在扫描右键菜单…";
                        _lastRefreshError = null;
                        OnPropertyChanged("LastRefreshError");
                    });
                    Tuple<ScanResult, List<FileTypeOption>> bundle = await Task.Run(delegate
                    {
                        ScanResult scanResult = Scanner.ScanAll(true, null);
                        List<FileTypeOption> optionList = Scanner.BuildFileTypeOptions();
                        return Tuple.Create(scanResult, optionList);
                    });
                    ScanResult result = bundle.Item1;
                    List<FileTypeOption> options = bundle.Item2;
                    await RunOnUiAsync(delegate { ApplyScanBundle(result, options); });
                }
                while (_refreshQueued);
            }
            catch (Exception ex)
            {
                RunOnUi(delegate
                {
                    _lastRefreshError = ex.ToString();
                    OnPropertyChanged("LastRefreshError");
                    ShowToast("扫描失败：" + ex.Message, false, true);
                });
            }
            finally
            {
                _isRefreshing = false;
                RunOnUi(delegate
                {
                    IsBusy = false;
                });
            }
        }

        private async void RefreshCommandExec()
        {
            await RefreshAsync();
        }

        private void RefreshHistory()
        {
            RecentChanges.Clear();
            foreach (ChangeGroup group in Manager.GetHistory(5)) RecentChanges.Add(group);
            _hasHistory = RecentChanges.Count > 0;
            _historyCountText = string.Format("{0} 条生效中的更改", Manager.ActiveChangeCount);
            _historySummary = _historyCountText;
            OnPropertyChanged("RecentChanges");
            OnPropertyChanged("HasHistory");
            OnPropertyChanged("HasActiveChanges");
            OnPropertyChanged("HistoryCountText");
            OnPropertyChanged("HistorySummary");
        }

        private void ToggleEntry(MenuEntry entry)
        {
            if (entry == null || entry.IsBusy) return;
            if (entry.RequiresAdmin && !_isElevated)
            {
                var dialog = new DialogRequest();
                dialog.Title = "需要管理员权限";
                dialog.Glyph = "\uE72E";
                dialog.Message = "这个项目位于系统级位置（HKEY_LOCAL_MACHINE 或所有用户目录），需要管理员权限才能修改。\n\n应用会以管理员身份重新启动，并保留当前分类。";
                dialog.PrimaryText = "以管理员身份重启";
                dialog.SecondaryText = "取消";
                dialog.PrimaryAction = delegate { Elevate(); };
                var handler = DialogRequested;
                if (handler != null) handler(dialog);
                return;
            }
            if (entry.IsEnabled && entry.PrimaryKind == EntryKind.ShellExtension && entry.IsSystem)
            {
                var dialog = new DialogRequest();
                dialog.Title = "禁用系统 Shell 扩展？";
                dialog.Glyph = "\uE7BA";
                dialog.Message = string.Format("“{0}”看起来是 Windows 自带的 Shell 扩展。禁用后，资源管理器中的某些功能可能会消失。\n\n仍然禁用它吗？", entry.Title);
                dialog.PrimaryText = "仍然禁用";
                dialog.Danger = true;
                dialog.SecondaryText = "取消";
                dialog.PrimaryAction = delegate { ApplyToggle(entry); };
                var handler = DialogRequested;
                if (handler != null) handler(dialog);
                return;
            }
            ApplyToggle(entry);
        }

        private void ApplyToggle(MenuEntry entry)
        {
            if (entry == null || entry.IsBusy) return;
            entry.IsBusy = true;
            OperationResult result;
            try
            {
                result = Manager.Toggle(entry, !entry.IsEnabled);
            }
            catch (Exception ex)
            {
                entry.IsBusy = false;
                ShowToast("操作失败：" + ex.Message, false, true);
                return;
            }
            entry.IsBusy = false;
            if (!result.Success)
            {
                ShowToast(result.Message, false, true);
                return;
            }
            UpdateCounts();
            RefreshVisible();
            RefreshHistory();
            string message = result.Message;
            if (entry.PrimaryKind == EntryKind.Modern)
                message += " · 若菜单没有变化，请重启资源管理器";
            ShowToast(message, true, false);
            if (entry.IsOrphan)
            {
                Task refreshTask = RefreshAsync();
            }
        }

        private async void UndoCommandExec()
        {
            OperationResult result = Manager.UndoLast();
            if (!result.Success)
            {
                ShowToast(result.Message, false, true);
                return;
            }
            await RefreshAsync();
            ShowToast(result.Message, false, false);
        }

        private void RestoreAllCommandExec()
        {
            int count = Manager.ActiveChangeCount;
            if (count == 0)
            {
                ShowToast("没有需要恢复的更改。", false, false);
                return;
            }
            var dialog = new DialogRequest();
            dialog.Title = "恢复所有由本工具做的更改？";
            dialog.Glyph = "\uE7A7";
            dialog.Message = string.Format("将撤销本工具记录的 {0} 组更改，把菜单项、阻止列表、新建菜单和发送到项目恢复到修改前的状态。\n\n不会影响其他工具或软件自己的设置。", count);
            dialog.PrimaryText = "全部恢复";
            dialog.SecondaryText = "取消";
            dialog.Danger = true;
            dialog.PrimaryAction = delegate { RestoreAllConfirmed(); };
            var handler = DialogRequested;
            if (handler != null) handler(dialog);
        }

        private async void RestoreAllConfirmed()
        {
            OperationResult result = Manager.RestoreAll();
            await RefreshAsync();
            ShowToast(result.Message, false, !result.Success);
        }

        private void Elevate()
        {
            if (_isElevated)
            {
                ShowToast("当前已经是管理员权限。", false, false);
                return;
            }
            string args = "--category " + _currentCategory;
            string error;
            if (Native.RelaunchElevated(args, out error))
            {
                Application.Current.Shutdown();
            }
            else
            {
                ShowToast(string.IsNullOrEmpty(error) ? "未能提升权限。" : error, false, true);
            }
        }

        private void RestartExplorerCommandExec()
        {
            var dialog = new DialogRequest();
            dialog.Title = "重启资源管理器？";
            dialog.Glyph = "\uE72C";
            dialog.Message = "重启后任务栏和桌面图标会短暂消失，然后自动恢复。这是让右键菜单改动立即生效的最可靠方式。\n\n现在重启吗？";
            dialog.PrimaryText = "立即重启";
            dialog.SecondaryText = "稍后";
            dialog.PrimaryAction = delegate
            {
                Manager.RestartExplorer();
                ShowToast("已重启资源管理器。", false, false);
            };
            var handler = DialogRequested;
            if (handler != null) handler(dialog);
        }

        private void SwitchMenuStyle(bool classic)
        {
            bool current = Manager.IsClassicMenuActive();
            if (current == classic)
            {
                ShowToast(classic ? "当前已经使用经典右键菜单。" : "当前已经使用 Windows 11 现代菜单。", false, false);
                return;
            }
            var dialog = new DialogRequest();
            dialog.Title = classic ? "切换到经典右键菜单？" : "恢复 Windows 11 现代菜单？";
            dialog.Glyph = "\uE8A7";
            dialog.Message = classic
                ? "这会让右键菜单恢复成 Windows 10 的完整菜单样式，所有第三方传统菜单项会直接显示。\n\n切换后需要重启资源管理器才能生效。"
                : "这会恢复 Windows 11 的紧凑现代菜单。部分传统项目会收进“显示更多选项”。\n\n切换后需要重启资源管理器才能生效。";
            dialog.PrimaryText = classic ? "使用经典菜单" : "使用现代菜单";
            dialog.SecondaryText = "取消";
            dialog.PrimaryAction = delegate
            {
                OperationResult result = Manager.SetClassicMenu(classic);
                UpdateMenuStyle();
                ShowToast(result.Message, false, !result.Success);
            };
            var handler = DialogRequested;
            if (handler != null) handler(dialog);
        }

        private void ShowWarnings()
        {
            var dialog = new DialogRequest();
            dialog.Title = "扫描提示";
            dialog.Glyph = "\uE946";
            dialog.Message = _warnings.Count == 0 ? "没有扫描警告。" : string.Join("\n\n", _warnings.Take(12).ToArray());
            dialog.PrimaryText = "知道了";
            dialog.SecondaryText = null;
            var handler = DialogRequested;
            if (handler != null) handler(dialog);
        }

        private void RequestDetails(MenuEntry entry)
        {
            var handler = DetailsRequested;
            if (handler != null) handler(entry);
        }

        private void ShowToast(string message, bool canUndo, bool isError)
        {
            var handler = ToastRequested;
            if (handler != null)
                handler(new ToastRequest { Message = message, CanUndo = canUndo, IsError = isError });
        }

        public OperationResult CreateCustom(string scope, string label, string command, string icon, bool extended)
        {
            OperationResult result = Manager.CreateCustom(scope, label, command, icon, extended);
            if (!result.Success) ShowToast(result.Message, false, true);
            return result;
        }

        public async Task ReloadAfterCustomAsync()
        {
            await RefreshAsync();
            NavigateToCategory(Categories.Custom);
            ShowToast("自定义菜单项已创建。", false, false);
        }

        public async Task DeleteCustomAsync(MenuEntry entry)
        {
            OperationResult result = Manager.DeleteCustom(entry);
            if (!result.Success)
            {
                ShowToast(result.Message, false, true);
                return;
            }
            await RefreshAsync();
            ShowToast(result.Message, false, false);
        }

        public void OpenRegeditFor(MenuEntry entry)
        {
            if (entry == null) return;
            string path = entry.TechnicalPath;
            if (string.IsNullOrEmpty(path))
            {
                ShowToast("这个项目没有可直接打开的注册表路径。", false, true);
                return;
            }
            if (!path.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
            {
                ShowToast("这不是注册表路径：" + path, false, true);
                return;
            }
            Native.OpenRegeditAt(path);
        }

        public void OpenDataFolder()
        {
            OpenFolder(AppPaths.DataDir);
        }

        private void OpenFolder(string path)
        {
            try
            {
                var psi = new ProcessStartInfo("explorer.exe", "\"" + path + "\"");
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowToast("打开目录失败：" + ex.Message, false, true);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
