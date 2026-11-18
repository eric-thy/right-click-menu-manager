using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Cmm
{
    public class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly string _initialCategory;
        private Grid _root;
        private DispatcherTimer _toastTimer;
        private Action _toastAction;
        private bool _loaded;
        private Border _toastHost;
        private TextBlock _toastGlyph;
        private TextBlock _toastText;
        private Button _toastActionButton;
        private Grid _overlay;
        private Border _dialogCard;
        private ContentControl _dialogBody;
        private Image _titleIcon;
        private Button _btnMaximize;
        private TextBlock _btnMaximizeGlyph;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public MainWindow(string initialCategory)
        {
            _initialCategory = initialCategory;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            AllowsTransparency = false;
            Background = Theme.WindowBackground;
            Width = 1200;
            Height = 820;
            MinWidth = 1040;
            MinHeight = 690;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Title = "右键菜单管理器";
            FontFamily = new FontFamily("Microsoft YaHei UI");
            LoadXaml();
            var chrome = new System.Windows.Shell.WindowChrome();
            chrome.CaptionHeight = 52;
            chrome.ResizeBorderThickness = new Thickness(7);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.UseAeroCaptionButtons = false;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);

            _vm = new MainViewModel();
            DataContext = _vm;
            WireViewModel();
            LoadWindowIcon();
            SourceInitialized += OnSourceInitialized;
            StateChanged += OnWindowStateChanged;
            Loaded += OnLoaded;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void LoadXaml()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream stream = assembly.GetManifestResourceStream("Cmm.MainWindow.xaml");
            if (stream == null)
            {
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MainWindow.xaml");
                if (File.Exists(local)) stream = File.OpenRead(local);
            }
            if (stream == null)
                throw new InvalidOperationException("找不到界面资源 Cmm.MainWindow.xaml。");
            try
            {
                object parsed = XamlReader.Load(stream);
                _root = parsed as Grid;
                if (_root == null) throw new InvalidOperationException("界面根元素不是 Grid。");
                Content = _root;
            }
            finally
            {
                stream.Dispose();
            }

            _toastHost = (Border)_root.FindName("ToastHost");
            _toastGlyph = (TextBlock)_root.FindName("ToastGlyph");
            _toastText = (TextBlock)_root.FindName("ToastText");
            _toastActionButton = (Button)_root.FindName("ToastAction");
            _overlay = (Grid)_root.FindName("Overlay");
            _dialogCard = (Border)_root.FindName("DialogCard");
            _dialogBody = (ContentControl)_root.FindName("DialogBody");
            _titleIcon = (Image)_root.FindName("TitleIcon");
            _btnMaximize = (Button)_root.FindName("BtnMaximize");
            _btnMaximizeGlyph = (TextBlock)_root.FindName("BtnMaximizeGlyph");

            Button minimize = (Button)_root.FindName("BtnMinimize");
            Button close = (Button)_root.FindName("BtnClose");
            Button toastClose = (Button)_root.FindName("ToastClose");
            if (minimize != null) minimize.Click += delegate { WindowState = WindowState.Minimized; };
            if (_btnMaximize != null) _btnMaximize.Click += delegate { ToggleMaximize(); };
            if (close != null) close.Click += delegate { Close(); };
            if (toastClose != null) toastClose.Click += delegate { HideToast(); };
            if (_toastActionButton != null) _toastActionButton.Click += OnToastAction;
            if (_overlay != null) _overlay.MouseLeftButtonDown += OnOverlayMouseDown;

            _toastTimer = new DispatcherTimer();
            _toastTimer.Interval = TimeSpan.FromSeconds(6);
            _toastTimer.Tick += delegate { HideToast(); };
        }

        private void WireViewModel()
        {
            _vm.ToastRequested += ShowToast;
            _vm.DetailsRequested += ShowDetails;
            _vm.DialogRequested += ShowDialog;
            _vm.CustomRequested += ShowCustomForm;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                await _vm.RefreshAsync();
                if (!string.IsNullOrEmpty(_initialCategory))
                    _vm.NavigateToCategory(_initialCategory);
            }
            catch (Exception ex)
            {
                ShowToast("初始化失败：" + ex.Message, false, true);
            }
        }

        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            else WindowState = WindowState.Maximized;
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (_btnMaximizeGlyph == null) return;
            _btnMaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            if (_btnMaximize != null)
                _btnMaximize.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
            }
            catch
            {
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _overlay != null && _overlay.Visibility == Visibility.Visible)
            {
                HideOverlay();
                e.Handled = true;
            }
        }

        private void LoadWindowIcon()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("Cmm.app.png"))
                {
                    if (stream != null)
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.EndInit();
                        image.Freeze();
                        Icon = image;
                        if (_titleIcon != null) _titleIcon.Source = image;
                        return;
                    }
                }
            }
            catch
            {
            }
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                {
                    if (icon == null) return;
                    BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    if (source.CanFreeze) source.Freeze();
                    Icon = source;
                    if (_titleIcon != null) _titleIcon.Source = source;
                }
            }
            catch
            {
            }
        }

        private void ShowToast(ToastRequest request)
        {
            if (request == null || _toastHost == null) return;
            _toastHost.Visibility = Visibility.Visible;
            _toastText.Text = request.Message ?? string.Empty;
            if (request.IsError)
            {
                _toastHost.Background = Theme.Brush("#7A271A");
                _toastGlyph.Text = "\uE711";
                _toastGlyph.Foreground = Theme.Brush("#FFC9C0");
            }
            else
            {
                _toastHost.Background = Theme.Brush("#1F2937");
                _toastGlyph.Text = "\uE73E";
                _toastGlyph.Foreground = Theme.Brush("#8CE0B3");
            }
            _toastAction = null;
            if (request.CanUndo)
            {
                _toastActionButton.Visibility = Visibility.Visible;
                _toastAction = delegate { _vm.UndoCommand.Execute(null); };
            }
            else
            {
                _toastActionButton.Visibility = Visibility.Collapsed;
            }
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void ShowToast(string message, bool canUndo, bool isError)
        {
            ShowToast(new ToastRequest { Message = message, CanUndo = canUndo, IsError = isError });
        }

        private void OnToastAction(object sender, RoutedEventArgs e)
        {
            Action action = _toastAction;
            HideToast();
            if (action != null) action();
        }

        private void HideToast()
        {
            if (_toastTimer != null) _toastTimer.Stop();
            if (_toastHost != null) _toastHost.Visibility = Visibility.Collapsed;
        }

        private void OnOverlayMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == _overlay) HideOverlay();
        }

        private void HideOverlay()
        {
            if (_overlay != null) _overlay.Visibility = Visibility.Collapsed;
            if (_dialogBody != null) _dialogBody.Content = null;
        }

        private void SetDialogContent(UIElement content, double width)
        {
            _dialogCard.Width = width;
            _dialogBody.Content = content;
            _overlay.Visibility = Visibility.Visible;
        }

        private Style StyleRes(string key)
        {
            object style = _root.Resources[key];
            return style as Style;
        }

        private Button MakeButton(string text, string styleKey, RoutedEventHandler handler)
        {
            var button = new Button();
            button.Content = text;
            button.Style = StyleRes(styleKey);
            if (handler != null) button.Click += handler;
            return button;
        }

        private TextBlock MakeText(string text, double size, FontWeight weight, Brush color)
        {
            var tb = new TextBlock();
            tb.Text = text;
            tb.FontSize = size;
            tb.FontWeight = weight;
            tb.Foreground = color;
            tb.TextWrapping = TextWrapping.Wrap;
            return tb;
        }

        private Grid MakeDialogHeader(string title, string glyph, out Button closeButton)
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var iconBorder = new Border();
            iconBorder.Width = 34;
            iconBorder.Height = 34;
            iconBorder.CornerRadius = new CornerRadius(9);
            iconBorder.Background = Theme.AccentSoft;
            iconBorder.VerticalAlignment = VerticalAlignment.Center;
            var icon = new TextBlock();
            icon.Text = string.IsNullOrEmpty(glyph) ? "\uE946" : glyph;
            icon.FontFamily = new FontFamily("Segoe Fluent Icons");
            icon.FontSize = 15;
            icon.Foreground = Theme.Accent;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            iconBorder.Child = icon;
            header.Children.Add(iconBorder);
            var titleBlock = MakeText(title, 17, FontWeights.SemiBold, Theme.Text);
            titleBlock.Margin = new Thickness(10, 0, 0, 0);
            titleBlock.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(titleBlock, 1);
            header.Children.Add(titleBlock);
            closeButton = new Button();
            closeButton.Style = StyleRes("IconButtonStyle");
            closeButton.Content = new TextBlock { Text = "\uE8BB", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 10 };
            closeButton.ToolTip = "关闭";
            closeButton.Click += delegate { HideOverlay(); };
            Grid.SetColumn(closeButton, 2);
            header.Children.Add(closeButton);
            return header;
        }

        private void ShowDialog(DialogRequest request)
        {
            if (request == null) return;
            double width = request.Extra != null ? 620 : 560;
            var panel = new StackPanel();
            panel.Margin = new Thickness(24);
            Button close;
            Grid header = MakeDialogHeader(request.Title, request.Glyph, out close);
            panel.Children.Add(header);
            var message = MakeText(request.Message ?? string.Empty, 12.5, FontWeights.Normal, Theme.MutedText);
            message.Margin = new Thickness(0, 16, 0, 0);
            panel.Children.Add(message);
            if (request.Extra != null)
            {
                FrameworkElement extra = request.Extra as FrameworkElement;
                if (extra != null) extra.Margin = new Thickness(0, 16, 0, 0);
                panel.Children.Add(request.Extra);
            }
            var footer = new StackPanel();
            footer.Orientation = Orientation.Horizontal;
            footer.HorizontalAlignment = HorizontalAlignment.Right;
            footer.Margin = new Thickness(0, 22, 0, 0);
            if (!string.IsNullOrEmpty(request.SecondaryText) || request.PrimaryAction != null)
            {
                var cancel = MakeButton(string.IsNullOrEmpty(request.SecondaryText) ? "取消" : request.SecondaryText,
                    "SecondaryButton", delegate
                    {
                        HideOverlay();
                        if (request.SecondaryAction != null) request.SecondaryAction();
                    });
                cancel.Margin = new Thickness(0, 0, 8, 0);
                footer.Children.Add(cancel);
            }
            if (!string.IsNullOrEmpty(request.PrimaryText))
            {
                var primary = MakeButton(request.PrimaryText, request.Danger ? "DangerButton" : "PrimaryButton", delegate
                {
                    HideOverlay();
                    if (request.PrimaryAction != null)
                    {
                        try { request.PrimaryAction(); }
                        catch (Exception ex) { ShowToast("操作失败：" + ex.Message, false, true); }
                    }
                });
                footer.Children.Add(primary);
            }
            panel.Children.Add(footer);
            SetDialogContent(panel, width);
        }

        private void ShowDetails(MenuEntry entry)
        {
            if (entry == null) return;
            var panel = new StackPanel();
            panel.Margin = new Thickness(24);
            Button close;
            Grid header = MakeDialogHeader("菜单项详情", "\uE946", out close);
            panel.Children.Add(header);

            var identity = new Grid();
            identity.Margin = new Thickness(0, 16, 0, 0);
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var iconGrid = new Grid();
            iconGrid.Width = 44;
            iconGrid.Height = 44;
            var iconBg = new Border();
            iconBg.Background = entry.IconBackground ?? Theme.ClassicBadge;
            iconBg.CornerRadius = new CornerRadius(11);
            iconGrid.Children.Add(iconBg);
            var letter = MakeText(entry.IconLetter ?? "?", 16, FontWeights.SemiBold, entry.BadgeForeground ?? Theme.Text);
            letter.HorizontalAlignment = HorizontalAlignment.Center;
            letter.VerticalAlignment = VerticalAlignment.Center;
            iconGrid.Children.Add(letter);
            if (entry.Icon != null)
            {
                var img = new Image();
                img.Source = entry.Icon;
                img.Width = 30;
                img.Height = 30;
                img.Stretch = Stretch.Uniform;
                iconGrid.Children.Add(img);
            }
            identity.Children.Add(iconGrid);
            var titleStack = new StackPanel();
            titleStack.Margin = new Thickness(12, 0, 0, 0);
            var title = MakeText(entry.Title, 18, FontWeights.SemiBold, Theme.Text);
            titleStack.Children.Add(title);
            var subtitle = MakeText(entry.Subtitle, 11.5, FontWeights.Normal, Theme.MutedText);
            subtitle.Margin = new Thickness(0, 3, 0, 0);
            titleStack.Children.Add(subtitle);
            Grid.SetColumn(titleStack, 1);
            identity.Children.Add(titleStack);
            panel.Children.Add(identity);

            var statusRow = new StackPanel();
            statusRow.Orientation = Orientation.Horizontal;
            statusRow.Margin = new Thickness(0, 14, 0, 0);
            var stateBorder = new Border();
            stateBorder.Background = entry.IsEnabled ? Theme.SuccessSoft : Theme.DangerSoft;
            stateBorder.CornerRadius = new CornerRadius(5);
            stateBorder.Padding = new Thickness(7, 3, 7, 3);
            stateBorder.Child = MakeText(entry.StateText, 10.5, FontWeights.SemiBold, entry.IsEnabled ? Theme.Success : Theme.Danger);
            statusRow.Children.Add(stateBorder);
            var badge = new Border();
            badge.Background = entry.BadgeBackground ?? Theme.ClassicBadge;
            badge.CornerRadius = new CornerRadius(5);
            badge.Padding = new Thickness(7, 3, 7, 3);
            badge.Margin = new Thickness(6, 0, 0, 0);
            badge.Child = MakeText(entry.Badge ?? "菜单项", 10.5, FontWeights.SemiBold, entry.BadgeForeground ?? Theme.MutedText);
            statusRow.Children.Add(badge);
            if (entry.RequiresAdmin)
            {
                var admin = MakeText("需要管理员权限", 10.5, FontWeights.Normal, Theme.Warning);
                admin.Margin = new Thickness(8, 2, 0, 0);
                statusRow.Children.Add(admin);
            }
            panel.Children.Add(statusRow);

            var scroll = new ScrollViewer();
            scroll.MaxHeight = 386;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.Margin = new Thickness(0, 16, 0, 0);
            var detailsPanel = new StackPanel();
            var rows = BuildDetailRows(entry);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = new Grid();
                row.Margin = new Thickness(0, 0, 0, 1);
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var label = MakeText(rows[i].Key, 11, FontWeights.Normal, Theme.FaintText);
                label.Margin = new Thickness(12, 8, 8, 8);
                label.VerticalAlignment = VerticalAlignment.Top;
                row.Children.Add(label);
                var value = new TextBlock();
                value.Text = rows[i].Value;
                value.FontSize = 11.5;
                value.Foreground = Theme.Text;
                value.TextWrapping = TextWrapping.Wrap;
                value.Margin = new Thickness(0, 8, 12, 8);
                value.VerticalAlignment = VerticalAlignment.Top;
                if (rows[i].Key.Contains("位置") || rows[i].Key.Contains("路径") || rows[i].Key.Contains("命令") || rows[i].Key.Contains("处理程序") || rows[i].Key.Contains("CLSID"))
                    value.FontFamily = new FontFamily("Consolas");
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                if (i % 2 == 0)
                {
                    var bg = new Border();
                    bg.Background = Theme.Brush("#F7F9FC");
                    Grid.SetColumnSpan(bg, 2);
                    row.Children.Insert(0, bg);
                }
                detailsPanel.Children.Add(row);
            }
            var border = new Border();
            border.BorderBrush = Theme.Border;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(9);
            border.Child = detailsPanel;
            scroll.Content = border;
            panel.Children.Add(scroll);

            if (!string.IsNullOrEmpty(entry.ActionHint))
            {
                var hint = MakeText(entry.ActionHint, 10.5, FontWeights.Normal, Theme.MutedText);
                hint.Margin = new Thickness(0, 10, 0, 0);
                panel.Children.Add(hint);
            }

            var footer = new StackPanel();
            footer.Orientation = Orientation.Horizontal;
            footer.HorizontalAlignment = HorizontalAlignment.Right;
            footer.Margin = new Thickness(0, 18, 0, 0);
            var copy = MakeButton("复制信息", "SecondaryButton", delegate
            {
                try
                {
                    Clipboard.SetText(BuildReport(entry));
                    ShowToast("详情已复制到剪贴板。", false, false);
                }
                catch (Exception ex)
                {
                    ShowToast("复制失败：" + ex.Message, false, true);
                }
            });
            copy.Margin = new Thickness(0, 0, 8, 0);
            footer.Children.Add(copy);

            if (!string.IsNullOrEmpty(entry.TechnicalPath) && entry.TechnicalPath.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
            {
                var regedit = MakeButton("打开注册表", "SecondaryButton", delegate { _vm.OpenRegeditFor(entry); });
                regedit.Margin = new Thickness(0, 0, 8, 0);
                footer.Children.Add(regedit);
            }

            if (entry.IsCustom)
            {
                var delete = MakeButton("删除自定义项", "DangerButton", delegate
                {
                    var dialog = new DialogRequest();
                    dialog.Title = "删除这个自定义菜单项？";
                    dialog.Glyph = "\uE74D";
                    dialog.Message = string.Format("将删除“{0}”。删除记录会保留在备份中，可通过“恢复全部”找回。", entry.Title);
                    dialog.PrimaryText = "删除";
                    dialog.Danger = true;
                    dialog.SecondaryText = "取消";
                    dialog.PrimaryAction = delegate { DeleteCustomConfirmed(entry); };
                    ShowDialog(dialog);
                });
                delete.Margin = new Thickness(0, 0, 8, 0);
                footer.Children.Add(delete);
            }

            var toggle = MakeButton(entry.IsEnabled ? "禁用此项目" : "启用此项目",
                entry.IsEnabled ? "PrimaryButton" : "SecondaryButton", delegate
                {
                    HideOverlay();
                    _vm.ToggleCommand.Execute(entry);
                });
            toggle.Margin = new Thickness(0, 0, 8, 0);
            footer.Children.Add(toggle);
            var done = MakeButton("关闭", "SecondaryButton", delegate { HideOverlay(); });
            footer.Children.Add(done);
            panel.Children.Add(footer);
            SetDialogContent(panel, 720);
        }

        private async void DeleteCustomConfirmed(MenuEntry entry)
        {
            await _vm.DeleteCustomAsync(entry);
        }

        private List<KeyValuePair<string, string>> BuildDetailRows(MenuEntry entry)
        {
            var list = new List<KeyValuePair<string, string>>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> add = delegate(string key, string value)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
                if (!seenKeys.Add(key)) return;
                list.Add(new KeyValuePair<string, string>(key, value));
            };
            add("状态", entry.IsEnabled ? "已启用" : "已禁用");
            add("来源", entry.Badge);
            add("适用范围", entry.ScopeText);
            add("注册表位置", entry.TechnicalPath);
            add("CLSID", entry.Clsid);
            add("命令", entry.Command);
            add("应用包", entry.PackageDisplay);
            add("处理程序", entry.HandlerPath);
            add("禁用原因", entry.DisabledReason);
            add("操作方式", entry.ActionHint);
            add("说明", entry.Description);
            foreach (KeyValuePair<string, string> pair in entry.DetailRows)
                add(pair.Key, pair.Value);
            return list;
        }

        private string BuildReport(MenuEntry entry)
        {
            var sb = new StringBuilder();
            sb.AppendLine("右键菜单管理器 · 项目详情");
            sb.AppendLine("名称：" + entry.Title);
            sb.AppendLine("状态：" + entry.StateText);
            sb.AppendLine("来源：" + entry.Badge);
            sb.AppendLine("范围：" + entry.ScopeText);
            if (!string.IsNullOrEmpty(entry.TechnicalPath)) sb.AppendLine("位置：" + entry.TechnicalPath);
            if (!string.IsNullOrEmpty(entry.Clsid)) sb.AppendLine("CLSID：" + entry.Clsid);
            if (!string.IsNullOrEmpty(entry.Command)) sb.AppendLine("命令：" + entry.Command);
            if (!string.IsNullOrEmpty(entry.PackageDisplay)) sb.AppendLine("应用包：" + entry.PackageDisplay);
            if (!string.IsNullOrEmpty(entry.HandlerPath)) sb.AppendLine("处理程序：" + entry.HandlerPath);
            if (!string.IsNullOrEmpty(entry.DisabledReason)) sb.AppendLine("禁用原因：" + entry.DisabledReason);
            if (!string.IsNullOrEmpty(entry.ActionHint)) sb.AppendLine("说明：" + entry.ActionHint);
            foreach (KeyValuePair<string, string> pair in entry.DetailRows)
                sb.AppendLine(pair.Key + "：" + pair.Value);
            return sb.ToString();
        }

        private void ShowCustomForm()
        {
            var panel = new StackPanel();
            panel.Margin = new Thickness(24);
            Button close;
            panel.Children.Add(MakeDialogHeader("新建自定义菜单项", "\uE710", out close));
            var intro = MakeText("把常用命令放进右键菜单。项目会直接写入当前用户的注册表，删除和修改都可在本工具中恢复。",
                11.5, FontWeights.Normal, Theme.MutedText);
            intro.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(intro);

            var form = new StackPanel();
            form.Margin = new Thickness(0, 14, 0, 0);
            var label1 = MakeText("显示名称", 11, FontWeights.SemiBold, Theme.Text);
            form.Children.Add(label1);
            var nameBox = new TextBox();
            nameBox.Style = StyleRes("InputTextBox");
            nameBox.Margin = new Thickness(0, 5, 0, 12);
            AutomationProperties.SetName(nameBox, "显示名称");
            form.Children.Add(nameBox);

            var label2 = MakeText("显示位置", 11, FontWeights.SemiBold, Theme.Text);
            form.Children.Add(label2);
            var scopeCombo = new ComboBox();
            scopeCombo.Style = StyleRes("ComboBoxStyle");
            scopeCombo.Margin = new Thickness(0, 5, 0, 12);
            AddScopeItem(scopeCombo, "文件（所有文件）", ScopeKeys.AllFiles);
            AddScopeItem(scopeCombo, "文件夹", ScopeKeys.Directory);
            AddScopeItem(scopeCombo, "文件夹空白处", ScopeKeys.DirectoryBackground);
            AddScopeItem(scopeCombo, "驱动器", ScopeKeys.Drive);
            scopeCombo.SelectedIndex = 0;
            AutomationProperties.SetName(scopeCombo, "显示位置");
            form.Children.Add(scopeCombo);

            var label3 = MakeText("命令", 11, FontWeights.SemiBold, Theme.Text);
            form.Children.Add(label3);
            var commandBox = new TextBox();
            commandBox.Style = StyleRes("InputTextBox");
            commandBox.Margin = new Thickness(0, 5, 0, 4);
            commandBox.Text = "notepad.exe \"%1\"";
            AutomationProperties.SetName(commandBox, "命令");
            form.Children.Add(commandBox);
            form.Children.Add(MakeText("用 %1 表示选中的文件，用 %V 表示当前文件夹。建议给程序路径加上英文双引号。", 10.5, FontWeights.Normal, Theme.FaintText));
            form.Children.Add(new Border { Height = 12 });

            var label4 = MakeText("图标（可选）", 11, FontWeights.SemiBold, Theme.Text);
            form.Children.Add(label4);
            var iconBox = new TextBox();
            iconBox.Style = StyleRes("InputTextBox");
            iconBox.Margin = new Thickness(0, 5, 0, 12);
            iconBox.ToolTip = "例如 C:\\Windows\\System32\\notepad.exe 或 %SystemRoot%\\System32\\shell32.dll";
            AutomationProperties.SetName(iconBox, "图标路径");
            form.Children.Add(iconBox);

            var extended = new CheckBox();
            extended.Content = "只在按住 Shift 右键时显示";
            extended.FontSize = 11.5;
            extended.Foreground = Theme.Text;
            extended.Margin = new Thickness(0, 2, 0, 0);
            form.Children.Add(extended);
            var error = MakeText(string.Empty, 10.5, FontWeights.Normal, Theme.Danger);
            error.Margin = new Thickness(0, 10, 0, 0);
            error.Visibility = Visibility.Collapsed;
            form.Children.Add(error);
            panel.Children.Add(form);

            var footer = new StackPanel();
            footer.Orientation = Orientation.Horizontal;
            footer.HorizontalAlignment = HorizontalAlignment.Right;
            footer.Margin = new Thickness(0, 18, 0, 0);
            var cancel = MakeButton("取消", "SecondaryButton", delegate { HideOverlay(); });
            cancel.Margin = new Thickness(0, 0, 8, 0);
            footer.Children.Add(cancel);
            var create = MakeButton("创建", "PrimaryButton", null);
            create.Click += async delegate
            {
                string label = nameBox.Text == null ? string.Empty : nameBox.Text.Trim();
                string command = commandBox.Text == null ? string.Empty : commandBox.Text.Trim();
                string icon = iconBox.Text == null ? string.Empty : iconBox.Text.Trim();
                if (label.Length == 0)
                {
                    error.Text = "请填写显示名称。";
                    error.Visibility = Visibility.Visible;
                    return;
                }
                if (command.Length == 0)
                {
                    error.Text = "请填写要执行的命令。";
                    error.Visibility = Visibility.Visible;
                    return;
                }
                ComboBoxItem selectedItem = scopeCombo.SelectedItem as ComboBoxItem;
                string scope = selectedItem == null ? ScopeKeys.AllFiles : Convert.ToString(selectedItem.Tag);
                OperationResult result = _vm.CreateCustom(scope, label, command, icon, extended.IsChecked == true);
                if (!result.Success)
                {
                    error.Text = result.Message;
                    error.Visibility = Visibility.Visible;
                    return;
                }
                HideOverlay();
                await _vm.ReloadAfterCustomAsync();
            };
            footer.Children.Add(create);
            panel.Children.Add(footer);
            SetDialogContent(panel, 620);
            nameBox.Focus();
        }

        private static void AddScopeItem(ComboBox combo, string label, string value)
        {
            var item = new ComboBoxItem();
            item.Content = label;
            item.Tag = value;
            combo.Items.Add(item);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_toastTimer != null) _toastTimer.Stop();
            base.OnClosed(e);
        }
    }
}
