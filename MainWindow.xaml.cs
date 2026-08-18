using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Huge;

public partial class MainWindow : Window
{
    private string? _projectPath;
    private string? _currentFile;
    private bool _isDirty;
    private bool _isEnglish;
    private bool _isDarkTheme;
    private Process? _hugoProcess;

    // ===== AI 助手 =====
    private readonly ApiSettings _apiSettings = ApiSettings.Load();
    private readonly List<DeepSeekClient.ChatMessage> _aiHistory = new();
    private CancellationTokenSource? _aiCts;
    // Cline 式流式气泡状态
    private TextBlock? _aiStreamTextBlock;   // 流式期间的纯文本宿主
    private TextBlock? _aiStreamStatus;      // 流式期间状态行（「ASSISTANT · 正在思考…」）
    private string? _aiStreamPlain;          // 流式累计纯文本
    private string? _lastUserText;           // 最近一次提问（用于重新生成）
    private string? _lastAssistantText;      // 最近一次回复（用于复制/插入）

    public MainWindow()
    {
        InitializeComponent();
        // 默认跟随系统主题
        _isDarkTheme = SystemThemeHelper.IsSystemDarkTheme();
        IsDarkTheme = _isDarkTheme;
        ApplyLanguage();
        ApplyTheme();

        // 通过路由事件挂接编辑器的滚动事件（TextBox 无 ScrollChanged 事件）
        EditorBox.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(EditorBox_ScrollChanged));

        // Ctrl+S 快速保存（全局快捷键，编辑器内生效）
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SaveCurrentFile();
                e.Handled = true;
            }
        };

        // AI 面板默认折叠：必须与 AiCollapseBtn_Click 一致地清理列宽，
        // 否则仅隐藏面板而列宽仍为 2*（MinWidth=220）会残留大片空白区域。
        var splitterCol = RootGrid.ColumnDefinitions[3];
        var aiCol = RootGrid.ColumnDefinitions[4];
        aiCol.MinWidth = 0;
        aiCol.MaxWidth = double.PositiveInfinity;
        splitterCol.Width = new GridLength(0);
        aiCol.Width = new GridLength(0);
        AiSplitter.Visibility = Visibility.Collapsed;
        AiPanel.Visibility = Visibility.Collapsed;
        AiExpandBtn.Visibility = Visibility.Visible;

        // AI 面板尺寸变化 → 重排消息，使其跟随面板宽度自适应
        AiChatPanel.SizeChanged += AiChatPanel_SizeChanged;

        // AI 助手欢迎语（后续按语言切换）
        AddAiBubble("assistant",
            "哈喽，我是AI小助手。可以分析当前文件、梳理项目结构等，回答您的任何问题。\n请点击右上角 ⚙ 配置 DeepSeek API。");
    }

    // 获取 Windows 系统当前是否为深色模式
    private static class SystemThemeHelper
    {
        public static bool IsSystemDarkTheme()
        {
            try
            {
                // Windows 10/11 注册表键：HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int value)
                {
                    // 0 = dark, 1 = light
                    return value == 0;
                }
            }
            catch
            {
                // 无法读取时默认亮色
            }
            return false;
        }
    }

    // Windows 10/11 深色标题栏支持
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // 让系统标题栏（顶部 Hugo - Markdown Client 图标区）跟随深色模式
    private void ApplyTitleBarTheme()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var dark = _isDarkTheme ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch
        {
            // 旧版 Windows 不支持时静默忽略
        }
    }

    // ===== 全局主题 =====
    public static bool IsDarkTheme { get; private set; }

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        IsDarkTheme = _isDarkTheme;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var resources = Application.Current.Resources;

        if (_isDarkTheme)
        {
            // 使用 SetResourceReference 保持 DynamicResource 绑定，避免直接赋值覆盖 XAML 绑定
            SetResourceReference(BackgroundProperty, "BgBrush");
            SetResourceReference(ForegroundProperty, "TextBrush");
            resources["BgBrush"] = resources["DarkBgBrush"];
            resources["PanelBrush"] = resources["DarkPanelBrush"];
            resources["BorderBrush"] = resources["DarkBorderBrush"];
            resources["TextBrush"] = resources["DarkTextBrush"];
            resources["MutedTextBrush"] = resources["DarkMutedTextBrush"];
            resources["EditorBgBrush"] = resources["DarkEditorBgBrush"];
            resources["TreeBgBrush"] = resources["DarkTreeBgBrush"];
            resources["AccentBrush"] = resources["DarkAccentBrush"];
            ThemeBtn.Content = "☀";
            ThemeBtn.ToolTip = _isEnglish ? "Switch to Light" : "切换到亮色主题";
            ApplyTitleBarTheme();
        }
        else
        {
            SetResourceReference(BackgroundProperty, "BgBrush");
            SetResourceReference(ForegroundProperty, "TextBrush");
            resources["BgBrush"] = new SolidColorBrush(Color.FromRgb(0xFE, 0xFE, 0xFB));
            resources["PanelBrush"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xEF, 0xEB));
            resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0xE3, 0xE1, 0xDB));
            resources["TextBrush"] = new SolidColorBrush(Color.FromRgb(0x24, 0x23, 0x1F));
            resources["MutedTextBrush"] = new SolidColorBrush(Color.FromRgb(0x78, 0x76, 0x70));
            resources["EditorBgBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            resources["TreeBgBrush"] = new SolidColorBrush(Colors.Transparent);
            resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0xC5, 0x64, 0x73));
            ThemeBtn.Content = "🌙";
            ThemeBtn.ToolTip = _isEnglish ? "Switch to Dark" : "切换到暗色主题";
            ApplyTitleBarTheme();
        }

        // 同步 AI 面板中已渲染消息的文字颜色
        RefreshAiThemeColors();
    }

    // 递归更新 FlowDocument 中所有 Block/Inline 的颜色（含代码块、表格等子级）
    private static void UpdateFlowDocumentColors(FlowDocument doc, Brush textBrush, Brush mutedBrush)
    {
        doc.Foreground = textBrush;
        foreach (var block in doc.Blocks)
        {
            UpdateBlockColors(block, textBrush, mutedBrush);
        }
    }

    private static void UpdateBlockColors(Block block, Brush textBrush, Brush mutedBrush)
    {
        if (block is Paragraph p)
        {
            p.Foreground = textBrush;
            foreach (var inline in p.Inlines)
            {
                if (inline is Run run)
                {
                    run.Foreground = textBrush;
                }
                else if (inline is Hyperlink link)
                {
                    link.Foreground = textBrush;
                    link.TextDecorations = null;
                }
            }
        }
        else if (block is Section section)
        {
            foreach (var child in section.Blocks)
            {
                UpdateBlockColors(child, textBrush, mutedBrush);
            }
        }
        else if (block is BlockUIContainer container)
        {
            // 代码块/表格内的 TextBox/TextBlock
            if (container.Child is System.Windows.Controls.TextBox codeBox)
            {
                codeBox.Foreground = textBrush;
            }
            else if (container.Child is System.Windows.Controls.Grid grid)
            {
                foreach (var cell in grid.Children.OfType<System.Windows.Controls.TextBlock>())
                {
                    cell.Foreground = textBrush;
                }
            }
        }
        else if (block is List list)
        {
            foreach (var item in list.ListItems)
            {
                foreach (var childBlock in item.Blocks)
                {
                    UpdateBlockColors(childBlock, textBrush, mutedBrush);
                }
            }
        }
        else if (block is Table table)
        {
            foreach (var row in table.RowGroups.SelectMany(rg => rg.Rows))
            {
                foreach (var cell in row.Cells)
                {
                    foreach (var childBlock in cell.Blocks)
                    {
                        UpdateBlockColors(childBlock, textBrush, mutedBrush);
                    }
                }
            }
        }
    }

    // 主题切换后刷新 AI 面板中已渲染消息的文字颜色
    private void RefreshAiThemeColors()
    {
        var textBrush = new SolidColorBrush(IsDarkTheme
            ? Color.FromRgb(0xD4, 0xD4, 0xD4)
            : Color.FromRgb(0x24, 0x23, 0x1F));
        var mutedBrush = new SolidColorBrush(IsDarkTheme
            ? Color.FromRgb(0x9D, 0x9D, 0x9D)
            : Color.FromRgb(0x78, 0x76, 0x70));

        foreach (var child in AiChatPanel.Children)
        {
            // 助手消息：StackPanel[标注行(TextBlock) + 内容(RichTextBox/TextBox) + 操作栏]
            if (child is StackPanel sp)
            {
                foreach (var item in sp.Children)
                {
                    if (item is TextBlock label && label != _aiStreamTextBlock && label != _aiStreamStatus)
                    {
                        label.Foreground = mutedBrush;
                    }
                    else if (item is RichTextBox rtb && rtb.Document != null)
                    {
                        UpdateFlowDocumentColors(rtb.Document, textBrush, mutedBrush);
                    }
                    else if (item is TextBox contentBox)
                    {
                        contentBox.Foreground = textBrush;
                    }
                }
            }
            // 用户消息：StackPanel[Border + 复制按钮] 白色文字不变
        }

        // 刷新流式状态行/内容颜色
        if (_aiStreamStatus != null) _aiStreamStatus.Foreground = mutedBrush;
        if (_aiStreamTextBlock != null) _aiStreamTextBlock.Foreground = textBrush;
    }

    // ===== 语言切换 =====
    private void LangBtn_Click(object sender, RoutedEventArgs e)
    {
        _isEnglish = !_isEnglish;
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        LangBtn.Content = _isEnglish ? "中" : "EN";
        Title = "Hugo - Markdown Client";
        OpenProjectBtn.Content = _isEnglish ? "Open Hugo Project" : "打开 Hugo 项目";
        SaveBtn.Content = _isEnglish ? "Save" : "保存";
        SaveAsBtn.Content = _isEnglish ? "Save As" : "另存为";
        NewFileBtn.Content = _isEnglish ? "New File" : "新建文件";
        NewFolderBtn.Content = _isEnglish ? "New Folder" : "新建文件夹";
        RenameBtn.Content = _isEnglish ? "Rename" : "重命名";
        DeleteBtn.Content = _isEnglish ? "Delete" : "删除";
        HugoServeBtn.Content = _isEnglish ? "▶ Start Hugo" : "▶ 启动 Hugo";
        HugoStopBtn.Content = _isEnglish ? "■ Stop Hugo" : "■ 停止 Hugo";
        GitCommitBtn.Content = _isEnglish ? "Commit & Push" : "提交推送";
        MirrorBtn.Content = _isEnglish ? "Mirror Copy" : "镜像复制";
        MirrorBtn.ToolTip = _isEnglish ? "Copy current file to mirror language folder" : "将当前文件复制到镜像语言文件夹";
        // 修复：切换语言时保留已打开的项目名
        StatusText.Text = _projectPath != null
            ? Path.GetFileName(_projectPath)
            : (_isEnglish ? "No project opened" : "未打开项目");
        ContentTitle.Text = _isEnglish ? "Content" : "内容";
        EditorTitle.Text = _isEnglish ? "No file selected" : "未选择文件";
        LogTitle.Text = _isEnglish ? "Log" : "日志";
        ClearLogBtn.Content = _isEnglish ? "Clear" : "清空";
        LangBtn.ToolTip = _isEnglish ? "Switch to Chinese" : "Switch to English";

        // 格式化工具栏提示
        FmtBoldBtn.ToolTip = _isEnglish ? "Bold" : "粗体";
        FmtItalicBtn.ToolTip = _isEnglish ? "Italic" : "斜体";
        FmtStrikeBtn.ToolTip = _isEnglish ? "Strikethrough" : "删除线";
        FmtH1Btn.ToolTip = _isEnglish ? "Heading 1" : "一级标题";
        FmtH2Btn.ToolTip = _isEnglish ? "Heading 2" : "二级标题";
        FmtH3Btn.ToolTip = _isEnglish ? "Heading 3" : "三级标题";
        FmtLinkBtn.ToolTip = _isEnglish ? "Insert Link" : "插入链接";
        FmtImageBtn.ToolTip = _isEnglish ? "Insert Image" : "插入图片";
        FmtQuoteBtn.ToolTip = _isEnglish ? "Blockquote" : "引用";
        FmtCodeBtn.ToolTip = _isEnglish ? "Code Block" : "代码块";
        FmtListBtn.ToolTip = _isEnglish ? "Unordered List" : "无序列表";
        FmtNumListBtn.ToolTip = _isEnglish ? "Ordered List" : "有序列表";
        FmtHrBtn.ToolTip = _isEnglish ? "Horizontal Rule" : "水平分割线";
        FmtTableBtn.ToolTip = _isEnglish ? "Insert Table" : "插入表格";
        FmtEditFrontmatterBtn.Content = _isEnglish ? "⚙ Frontmatter" : "⚙ Frontmatter";
        FmtEditFrontmatterBtn.ToolTip = _isEnglish ? "Edit Frontmatter Metadata" : "编辑 Frontmatter 元数据";

        // 右键菜单
        CtxNewFile.Header = _isEnglish ? "New File" : "新建文件";
        CtxNewFolder.Header = _isEnglish ? "New Folder" : "新建文件夹";
        CtxRename.Header = _isEnglish ? "Rename" : "重命名";
        CtxDelete.Header = _isEnglish ? "Delete" : "删除";
        CtxNewTemplate.Header = _isEnglish ? "New Template" : "新建模板";

        // 内容区标题/刷新
        RefreshTreeBtn.ToolTip = _isEnglish ? "Refresh File Tree" : "刷新文件树";
        CtxShowInExplorer.Header = _isEnglish ? "Show in File Explorer" : "在文件资源管理器显示";

        // 主题提示
        ThemeBtn.ToolTip = _isDarkTheme
            ? (_isEnglish ? "Switch to Light" : "切换到亮色主题")
            : (_isEnglish ? "Switch to Dark" : "切换到暗色主题");

        // AI 助手
        AiTitleText.Text = _isEnglish ? "AI Assistant" : "AI 助手";
        AiSettingsBtn.ToolTip = _isEnglish ? "API Settings" : "API 设置";
        AiCollapseBtn.ToolTip = _isEnglish ? "Collapse AI Assistant" : "折叠 AI 助手";
        AiExpandBtn.ToolTip = _isEnglish ? "Expand AI Assistant" : "展开 AI 助手";
        AiAnalyzeFileBtn.Content = _isEnglish ? "Analyze File" : "分析当前文件";
        AiAnalyzeProjectBtn.Content = _isEnglish ? "Analyze Project" : "分析项目结构";
        AiAnalyzeFileBtn.ToolTip = _isEnglish ? "Send current file to AI" : "将当前文件发送给 AI 分析";
        AiAnalyzeProjectBtn.ToolTip = _isEnglish ? "Send project structure to AI" : "将项目目录结构发送给 AI 分析";
        AiSendBtn.Content = _isEnglish ? "Send" : "发送";
        AiStopBtn.ToolTip = _isEnglish ? "Stop" : "停止生成";
        AiClearBtn.ToolTip = _isEnglish ? "Clear Conversation" : "清空对话";
        AiHintText.Text = _isEnglish ? "Enter to send  ·  Shift+Enter for newline" : "Enter 发送  ·  Shift+Enter 换行";
        AiModelText.Text = _apiSettings.Model;
    }

    // ===== 文件树节点 =====
    private class FileNode
    {
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
    }

    // ===== 日志 =====
    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogBox.ScrollToEnd();
        });
    }

    // 下载进度日志：进度更新时复用最后一行（带 [Hugo] 前缀），避免日志无限刷屏
    private void LogDownloadProgress(string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            const string marker = "[Hugo] ";
            var lines = LogBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length > 0 && lines[^1].TrimStart().StartsWith(marker, StringComparison.Ordinal))
            {
                // 替换最后一行，保留时间戳前缀风格
                var idx = lines[^1].IndexOf(']');
                var prefix = idx >= 0 ? lines[^1][..(idx + 1)] : "";
                lines[^1] = $"{prefix} {marker}{text}";
                LogBox.Text = string.Join(Environment.NewLine, lines);
            }
            else
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {marker}{text}");
            }
            LogBox.ScrollToEnd();
        });
    }

    // ===== 打开项目 =====
    private void OpenProjectBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 Hugo 项目文件夹" };
        if (dialog.ShowDialog() == true)
        {
            LoadProject(dialog.FolderName);
        }
    }

    private void LoadProject(string path)
    {
        _projectPath = path;
        _currentFile = null;
        _isDirty = false;
        EditorBox.Clear();
        EditorTitle.Text = _isEnglish ? "No file selected" : "未选择文件";
        EditorTitle.Visibility = Visibility.Visible;
        ImagePreviewPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = Path.GetFileName(path);
        SetProjectButtons(true);
        BuildFileTree();
        Log($"{(_isEnglish ? "Project opened: " : "已打开项目: ")}{path}");
    }

    private void SetProjectButtons(bool enabled)
    {
        SaveBtn.IsEnabled = enabled;
        SaveAsBtn.IsEnabled = enabled;
        NewFileBtn.IsEnabled = enabled;
        NewFolderBtn.IsEnabled = enabled;
        RenameBtn.IsEnabled = enabled;
        DeleteBtn.IsEnabled = enabled;
        HugoServeBtn.IsEnabled = enabled;
        GitCommitBtn.IsEnabled = enabled;
        MirrorBtn.IsEnabled = enabled;
        FormatBar.IsEnabled = false;
    }

    // ===== 构建文件树 =====
    // 参考 Huge Genie：只显示 content、static、assets 三个核心目录，避免杂乱的目录干扰
    private void BuildFileTree()
    {
        // 保存当前展开状态，重建后恢复，避免每次操作后文件树折叠
        var expandedPaths = new HashSet<string>();
        CollectExpandedPaths(FileTree.Items, expandedPaths);
        var selectedPath = (FileTree.SelectedItem as TreeViewItem)?.Tag is FileNode selNode ? selNode.FullPath : null;

        FileTree.Items.Clear();
        if (_projectPath == null) return;

        // 隐藏技术性/生成目录
        var hiddenDirs = new HashSet<string> { "public", "resources", ".git", ".hugo_build.lock", "themes", "node_modules" };

        // 核心目录：仅 content、static、assets
        var coreDirs = new[] { "content", "static", "assets" };

        var root = new TreeViewItem
        {
            Header = Path.GetFileName(_projectPath),
            Tag = new FileNode { FullPath = _projectPath, IsDirectory = true },
            // 默认折叠文件树，用户点击后展开；若之前展开过则恢复
            IsExpanded = expandedPaths.Contains(_projectPath)
        };
        FileTree.Items.Add(root);

        // 只遍历这三个核心目录，且它们必须存在
        foreach (var dirName in coreDirs)
        {
            var dirPath = Path.Combine(_projectPath, dirName);
            if (!Directory.Exists(dirPath)) continue;

            var dirNode = new TreeViewItem
            {
                Header = dirName,
                Tag = new FileNode { FullPath = dirPath, IsDirectory = true },
                IsExpanded = expandedPaths.Contains(dirPath)
            };
            root.Items.Add(dirNode);
            AddDirectory(dirNode, dirPath, hiddenDirs, expandedPaths);
        }

        // 恢复选中项
        if (selectedPath != null)
        {
            var item = FindTreeItem(FileTree.Items, selectedPath);
            if (item != null)
            {
                item.IsSelected = true;
                item.BringIntoView();
            }
        }
    }

    // 递归收集所有已展开的目录路径
    private static void CollectExpandedPaths(ItemCollection items, HashSet<string> paths)
    {
        foreach (var obj in items)
        {
            if (obj is TreeViewItem item && item.Tag is FileNode node && node.IsDirectory)
            {
                if (item.IsExpanded) paths.Add(node.FullPath);
                CollectExpandedPaths(item.Items, paths);
            }
        }
    }

    private void AddDirectory(TreeViewItem parent, string dirPath, HashSet<string> hiddenDirs, HashSet<string> expandedPaths)
    {
        foreach (var dir in Directory.GetDirectories(dirPath).OrderBy(d => d))
        {
            var name = Path.GetFileName(dir);
            if (hiddenDirs.Contains(name)) continue;

            var node = new TreeViewItem
            {
                Header = name,
                Tag = new FileNode { FullPath = dir, IsDirectory = true },
                IsExpanded = expandedPaths.Contains(dir)
            };
            parent.Items.Add(node);
            AddDirectory(node, dir, hiddenDirs, expandedPaths);
        }

        foreach (var file in Directory.GetFiles(dirPath).OrderBy(f => f))
        {
            var ext = Path.GetExtension(file).ToLower();
            if (ext is ".md" or ".markdown" or ".yaml" or ".yml" or ".toml" or ".json"
                or ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" or ".ico")
            {
                var node = new TreeViewItem
                {
                    Header = Path.GetFileName(file),
                    Tag = new FileNode { FullPath = file, IsDirectory = false }
                };
                parent.Items.Add(node);
            }
        }
    }

    // ===== 选择文件 =====
    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is FileNode node && !node.IsDirectory)
        {
            if (_isDirty)
            {
                var msg = _isEnglish ? "Current file has unsaved changes. Save?" : "当前文件有未保存的更改，是否保存？";
                var result = MessageBox.Show(msg, _isEnglish ? "Unsaved Changes" : "未保存更改",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes) SaveCurrentFile();
            }

            OpenFile(node.FullPath);
        }
    }

    // F2 重命名文件/文件夹（选中文件树节点后按 F2）
    private void FileTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && FileTree.SelectedItem is TreeViewItem item && item.Tag is FileNode node)
        {
            e.Handled = true;
            // TreeView.SelectedItem 是只读属性，F2 触发时 item 已经处于选中状态，
            // 直接调用 RenameBtn_Click 即可（内部通过 GetSelectedPath() 获取当前选中项）
            RenameBtn_Click(sender, e);
        }
    }

    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico"
    };

    private bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ImageExtensions.Contains(ext);
    }

    private void OpenFile(string path)
    {
        try
        {
            _currentFile = path;
            if (IsImageFile(path))
            {
                // 图片文件 → 右侧图片预览
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();

                ImagePreview.Source = bitmap;
                var fi = new FileInfo(path);
                ImageInfoText.Text = $"{Path.GetFileName(path)}  ·  {bitmap.PixelWidth} × {bitmap.PixelHeight} px  ·  {FormatFileSize(fi.Length)}";
                // 隐藏顶部标题，避免与左上角信息行重复显示文件名
                EditorTitle.Visibility = Visibility.Collapsed;
                ImagePreviewPanel.Visibility = Visibility.Visible;
                EditorBox.Visibility = Visibility.Collapsed;
                LineNumberBox.Visibility = Visibility.Collapsed;
                FormatBar.IsEnabled = false;
            }
            else
            {
                EditorBox.Text = File.ReadAllText(path);
                EditorTitle.Visibility = Visibility.Visible;
                EditorBox.Visibility = Visibility.Visible;
                LineNumberBox.Visibility = Visibility.Visible;
                ImagePreviewPanel.Visibility = Visibility.Collapsed;
                _isDirty = false;
                FormatBar.IsEnabled = true;
            }
            EditorTitle.Text = Path.GetFileName(path);
            Log($"{(_isEnglish ? "Opened: " : "已打开: ")}{path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{( _isEnglish ? "Cannot open file: " : "无法打开文件: ")}{ex.Message}",
                _isEnglish ? "Error" : "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{(bytes / 1024.0 / 1024.0):0.0} MB";
        if (bytes >= 1024) return $"{(bytes / 1024.0):0.0} KB";
        return $"{bytes} B";
    }

    // ===== 编辑器 =====
    private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_currentFile != null)
        {
            _isDirty = true;
            EditorTitle.Text = Path.GetFileName(_currentFile) + " *";
        }
        UpdateLineNumbers();
    }

    // 更新编辑器行号
    private void UpdateLineNumbers()
    {
        var lineCount = string.IsNullOrEmpty(EditorBox.Text) ? 1 : EditorBox.Text.Count(c => c == '\n') + 1;

        // 动态生成行号（最高 9999 行，超出后截断）
        var maxLines = Math.Min(lineCount, 9999);
        var sb = new System.Text.StringBuilder();
        for (var i = 1; i <= maxLines; i++)
        {
            sb.AppendLine(i.ToString());
        }
        LineNumberBox.Text = sb.ToString();
    }

    // 编辑器滚动时同步行号栏的滚动位置
    private void EditorBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0)
        {
            // 查找编辑器内部的 ScrollViewer 并同步到行号栏
            if (FindVisualChild<ScrollViewer>(EditorBox) is ScrollViewer editorScroll &&
                FindVisualChild<ScrollViewer>(LineNumberBox) is ScrollViewer lineScroll)
            {
                lineScroll.ScrollToVerticalOffset(editorScroll.VerticalOffset);
            }
        }
    }

    // 查找可视树中的子元素
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e) => SaveCurrentFile();

    private void SaveCurrentFile()
    {
        if (_currentFile == null) return;
        try
        {
            File.WriteAllText(_currentFile, EditorBox.Text);
            _isDirty = false;
            EditorTitle.Text = Path.GetFileName(_currentFile);
            Log($"{(_isEnglish ? "Saved: " : "已保存: ")}{_currentFile}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{( _isEnglish ? "Save failed: " : "保存失败: ")}{ex.Message}",
                _isEnglish ? "Error" : "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveAsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Markdown 文件 (*.md)|*.md|所有文件 (*.*)|*.*",
            FileName = _currentFile != null ? Path.GetFileName(_currentFile) : "new.md"
        };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, EditorBox.Text);
            _currentFile = dialog.FileName;
            _isDirty = false;
            EditorTitle.Text = Path.GetFileName(dialog.FileName);
            Log($"{(_isEnglish ? "Saved As: " : "已另存为: ")}{dialog.FileName}");
        }
    }

    // ===== 新建/重命名/删除 =====
    private string? GetSelectedPath()
    {
        if (FileTree.SelectedItem is TreeViewItem item && item.Tag is FileNode node)
            return node.FullPath;
        return _projectPath;
    }

    private void NewFileBtn_Click(object sender, RoutedEventArgs e)
    {
        var dir = GetSelectedPath();
        if (dir == null) return;
        if (!Directory.Exists(dir)) dir = Path.GetDirectoryName(dir)!;

        var name = InputDialog(_isEnglish ? "New File" : "新建文件",
            _isEnglish ? "File name (.md):" : "文件名（.md）:", "new.md");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!name.EndsWith(".md")) name += ".md";

        var path = Path.Combine(dir, name);
        if (File.Exists(path))
        {
            MessageBox.Show(_isEnglish ? "File already exists." : "文件已存在。",
                _isEnglish ? "Notice" : "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 文件夹模板：若当前文件夹（或上级）存在 _template.md，则以其为起点，并弹出 Frontmatter 编辑
        var content = GetTemplateContent(dir) ?? "---\ntitle: \"\"\ndate: \"" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz") + "\"\n---\n\n";
        File.WriteAllText(path, content);
        BuildFileTree();
        Log($"{(_isEnglish ? "Created: " : "已创建: ")}{path}");

        // 如果使用了模板，自动打开文件让用户编辑
        var treeItem = FindTreeItem(FileTree.Items, path);
        if (treeItem != null)
        {
            treeItem.IsSelected = true;
            OpenFile(path);
            // 自动弹出 Frontmatter 编辑
            FmtEditFrontmatter_Click(sender, e);
        }
    }

    // 查找文件树中的节点
    private TreeViewItem? FindTreeItem(ItemCollection items, string path)
    {
        foreach (var obj in items)
        {
            if (obj is TreeViewItem item)
            {
                if (item.Tag is FileNode node && node.FullPath == path)
                    return item;
                var child = FindTreeItem(item.Items, path);
                if (child != null) return child;
            }
        }
        return null;
    }

    // 查找文件夹模板：优先当前目录，其次逐级向上查找 _template.md
    private string? GetTemplateContent(string dir)
    {
        var current = new DirectoryInfo(dir);
        while (current != null)
        {
            var template = Path.Combine(current.FullName, "_template.md");
            if (File.Exists(template))
            {
                return File.ReadAllText(template);
            }
            current = current.Parent;
        }
        return null;
    }

    private void NewFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var dir = GetSelectedPath();
        if (dir == null) return;
        if (!Directory.Exists(dir)) dir = Path.GetDirectoryName(dir)!;

        var name = InputDialog(_isEnglish ? "New Folder" : "新建文件夹",
            _isEnglish ? "Folder name:" : "文件夹名:", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        var path = Path.Combine(dir, name);
        if (Directory.Exists(path))
        {
            MessageBox.Show(_isEnglish ? "Folder already exists." : "文件夹已存在。",
                _isEnglish ? "Notice" : "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Directory.CreateDirectory(path);
        BuildFileTree();
        Log($"{(_isEnglish ? "Folder created: " : "已创建文件夹: ")}{path}");
    }

    private void RenameBtn_Click(object sender, RoutedEventArgs e)
    {
        var path = GetSelectedPath();
        if (path == null || path == _projectPath) return;

        var isDir = Directory.Exists(path);
        var newName = InputDialog(_isEnglish ? "Rename" : "重命名",
            _isEnglish ? "New name:" : "新名称:", Path.GetFileName(path));
        if (string.IsNullOrWhiteSpace(newName)) return;

        var newPath = Path.Combine(Path.GetDirectoryName(path)!, newName);
        try
        {
            if (isDir) Directory.Move(path, newPath);
            else File.Move(path, newPath);
            if (_currentFile == path) _currentFile = newPath;
            BuildFileTree();
            Log($"{(_isEnglish ? "Renamed: " : "已重命名: ")}{path} → {newPath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{( _isEnglish ? "Rename failed: " : "重命名失败: ")}{ex.Message}",
                _isEnglish ? "Error" : "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        var path = GetSelectedPath();
        if (path == null || path == _projectPath) return;

        var msg = _isEnglish
            ? $"Delete \"{Path.GetFileName(path)}\"? This cannot be undone."
            : $"确定删除 \"{Path.GetFileName(path)}\" 吗？此操作不可撤销。";
        var result = MessageBox.Show(msg, _isEnglish ? "Confirm Delete" : "确认删除",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else File.Delete(path);
            if (_currentFile == path)
            {
                _currentFile = null;
                EditorBox.Clear();
                EditorTitle.Text = _isEnglish ? "No file selected" : "未选择文件";
                EditorTitle.Visibility = Visibility.Visible;
                ImagePreviewPanel.Visibility = Visibility.Collapsed;
                FormatBar.IsEnabled = false;
            }
            BuildFileTree();
            Log($"{(_isEnglish ? "Deleted: " : "已删除: ")}{path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{( _isEnglish ? "Delete failed: " : "删除失败: ")}{ex.Message}",
                _isEnglish ? "Error" : "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===== 右键菜单 =====
    private void CtxNewFile_Click(object sender, RoutedEventArgs e) => NewFileBtn_Click(sender, e);
    private void CtxNewFolder_Click(object sender, RoutedEventArgs e) => NewFolderBtn_Click(sender, e);
    private void CtxRename_Click(object sender, RoutedEventArgs e) => RenameBtn_Click(sender, e);
    private void CtxDelete_Click(object sender, RoutedEventArgs e) => DeleteBtn_Click(sender, e);

    private void CtxNewTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dir = GetSelectedPath();
        if (dir == null) return;
        if (!Directory.Exists(dir)) dir = Path.GetDirectoryName(dir)!;

        var path = Path.Combine(dir, "_template.md");
        if (File.Exists(path))
        {
            var overwrite = MessageBox.Show(
                _isEnglish ? "Template already exists. Overwrite?" : "模板已存在，是否覆盖？",
                _isEnglish ? "Notice" : "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes) return;
        }

        var template = "---\ntitle: \"\"\ndate: \"" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz") + "\"\n---\n\n<!-- " +
                       (_isEnglish ? "This is a folder template. New files created in this folder will start from here." :
                                     "这是文件夹模板。在此文件夹中新建的文件将以此作为起点。") + " -->\n";
        File.WriteAllText(path, template);
        BuildFileTree();
        Log($"{(_isEnglish ? "Template created: " : "已创建模板: ")}{path}");
        MessageBox.Show(
            _isEnglish ? "Template created. New files in this folder (and subfolders) will use it."
                       : "模板已创建。该文件夹（及子文件夹）中新建的文件将使用此模板。",
            _isEnglish ? "Template Created" : "模板已创建", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ===== Markdown 格式化 =====
    private void FmtBold_Click(object sender, RoutedEventArgs e) => WrapSelection("**", "**");
    private void FmtItalic_Click(object sender, RoutedEventArgs e) => WrapSelection("*", "*");
    private void FmtStrike_Click(object sender, RoutedEventArgs e) => WrapSelection("~~", "~~");
    private void FmtHeading_Click(object sender, RoutedEventArgs e) => PrefixLine("# ");
    private void FmtH2_Click(object sender, RoutedEventArgs e) => PrefixLine("## ");
    private void FmtH3_Click(object sender, RoutedEventArgs e) => PrefixLine("### ");
    private void FmtLink_Click(object sender, RoutedEventArgs e) => WrapSelection("[", "](url)");
    private void FmtImage_Click(object sender, RoutedEventArgs e) => WrapSelection("![", "](image-url)");
    private void FmtQuote_Click(object sender, RoutedEventArgs e) => PrefixLine("> ");
    private void FmtList_Click(object sender, RoutedEventArgs e) => PrefixLine("- ");
    private void FmtNumList_Click(object sender, RoutedEventArgs e) => PrefixLine("1. ");
    private void FmtHr_Click(object sender, RoutedEventArgs e)
    {
        InsertText("\n\n---\n\n");
    }

    private void FmtCode_Click(object sender, RoutedEventArgs e)
    {
        var sel = EditorBox.SelectedText;
        if (string.IsNullOrEmpty(sel))
        {
            InsertText("\n```\n\n```\n");
            EditorBox.CaretIndex = EditorBox.Text.Length - 4; // 定位到代码块中间
            return;
        }
        EditorBox.SelectedText = $"```\n{sel}\n```";
    }

    private void FmtTable_Click(object sender, RoutedEventArgs e)
    {
        var table = "\n| 列 1 | 列 2 | 列 3 |\n| --- | --- | --- |\n| 内容 | 内容 | 内容 |\n";
        if (_isEnglish)
            table = "\n| Col 1 | Col 2 | Col 3 |\n| --- | --- | --- |\n| Content | Content | Content |\n";
        InsertText(table);
    }

    // 在选区前后包裹文本
    private void WrapSelection(string prefix, string suffix)
    {
        var sel = EditorBox.SelectedText;
        var start = EditorBox.SelectionStart;
        var len = EditorBox.SelectionLength;

        if (string.IsNullOrEmpty(sel))
        {
            // 无选区：插入占位符
            var placeholder = prefix + "text" + suffix;
            EditorBox.SelectedText = placeholder;
            EditorBox.CaretIndex = start + prefix.Length;
        }
        else
        {
            EditorBox.SelectedText = prefix + sel + suffix;
            EditorBox.CaretIndex = start + prefix.Length + sel.Length + suffix.Length;
        }
    }

    // 在每行前面添加前缀
    private void PrefixLine(string prefix)
    {
        var start = EditorBox.SelectionStart;
        var len = EditorBox.SelectionLength;

        if (len == 0)
        {
            // 光标所在行
            var lineStart = EditorBox.Text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
            var lineEnd = EditorBox.Text.IndexOf('\n', start);
            if (lineEnd == -1) lineEnd = EditorBox.Text.Length;

            var line = EditorBox.Text[lineStart..lineEnd];
            // 避免重复添加前缀
            if (!line.StartsWith(prefix.TrimEnd()))
            {
                EditorBox.Text = EditorBox.Text[..lineStart] + prefix + EditorBox.Text[lineStart..];
                EditorBox.CaretIndex = start + prefix.Length;
            }
        }
        else
        {
            // 多行选区：每行都加前缀
            var selectedText = EditorBox.SelectedText;
            var lines = selectedText.Split('\n');
            var newText = string.Join('\n', lines.Select(l => prefix + l));
            EditorBox.SelectedText = newText;
        }
    }

    // 在光标处插入文本
    private void InsertText(string text)
    {
        var start = EditorBox.SelectionStart;
        EditorBox.SelectedText = text;
        EditorBox.Focus();
        EditorBox.CaretIndex = start + text.Length;
    }

    // ===== Frontmatter 编辑 =====
    private void FmtEditFrontmatter_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null) return;

        var content = EditorBox.Text;
        var (frontmatter, body) = SplitFrontmatter(content);

        var fields = ParseFrontmatter(frontmatter);
        var dialog = new FrontmatterDialog(fields, _isEnglish)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            var newFrontmatter = dialog.BuildFrontmatter();
            var separator = string.IsNullOrEmpty(body) ? "" : "\n\n";
            var newContent = newFrontmatter + separator + body;

            // 仅当内容有变化时才标记为已修改
            if (newContent != content)
            {
                EditorBox.Text = newContent;
                _isDirty = true;
                EditorTitle.Text = Path.GetFileName(_currentFile) + " *";
                Log(_isEnglish ? "Frontmatter updated." : "Frontmatter 已更新。");
            }
        }
    }

    // 拆分 frontmatter 和正文
    private (string frontmatter, string body) SplitFrontmatter(string content)
    {
        // 支持 --- yaml --- 和 +++ toml +++
        if (content.StartsWith("---"))
        {
            var endIdx = content.IndexOf("\n---", 3);
            if (endIdx > 0)
            {
                return (content[..(endIdx + 4)].TrimEnd(), content[(endIdx + 4)..].TrimStart('\n'));
            }
        }
        else if (content.StartsWith("+++"))
        {
            var endIdx = content.IndexOf("\n+++", 3);
            if (endIdx > 0)
            {
                return (content[..(endIdx + 4)].TrimEnd(), content[(endIdx + 4)..].TrimStart('\n'));
            }
        }
        // 没有 frontmatter
        return ("", content);
    }

    // 解析 YAML frontmatter 为 key-value 字典
    private Dictionary<string, string> ParseFrontmatter(string frontmatter)
    {
        var fields = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(frontmatter)) return fields;

        foreach (var line in frontmatter.Split('\n'))
        {
            if (line.StartsWith("---") || line.StartsWith("+++")) continue;
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            // 去掉引号
            value = value.Trim('"', '\'');
            if (key.Length > 0)
            {
                fields[key] = value;
            }
        }
        return fields;
    }

    // ===== Hugo 命令 =====
    // 版本需支持常见主题（如 hugo-theme-stack 要求 Min 0.157.0 extended）
    private const string HugoVersion = "0.157.0";

    // 获取 Hugo 可执行文件：优先用户指定路径，其次应用同目录内嵌的 hugo，最后 PATH
    private string? FindHugoExecutable()
    {
        // 0. 用户手动指定的路径（在设置中保存）
        if (!string.IsNullOrWhiteSpace(_apiSettings.HugoPath))
        {
            try
            {
                if (File.Exists(_apiSettings.HugoPath))
                    return _apiSettings.HugoPath;
            }
            catch { /* 路径无效时忽略 */ }
        }

        // 1. 应用所在目录下的 hugo.exe（build.bat 发布时会拷入 publish 目录）
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var localHugo = Path.Combine(appDir, "hugo.exe");
        if (File.Exists(localHugo))
            return localHugo;

        // 2. 应用所在目录下 bin/hugo.exe
        var localHugoBin = Path.Combine(appDir, "bin", "hugo.exe");
        if (File.Exists(localHugoBin))
            return localHugoBin;

        // 3. vendor/hugo/hugo.exe（README 描述的支持位置）
        var vendorHugo = Path.Combine(appDir, "vendor", "hugo", "hugo.exe");
        if (File.Exists(vendorHugo))
            return vendorHugo;

        // 4. PATH 环境变量（「hugo」命令是否存在）
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), "hugo.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { /* 忽略非法路径 */ }
        }

        // 未找到
        return null;
    }

    // 若 Hugo 不存在，让用户选择：手动指定路径 / 自动下载 / 取消
    private async Task<string?> EnsureHugoExecutableAsync()
    {
        var existing = FindHugoExecutable();
        if (existing != null) return existing;

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var target = Path.Combine(appDir, "hugo.exe");

        // 询问用户：手动选择 Hugo 路径 或 自动下载
        var choice = MessageBox.Show(
            _isEnglish
                ? "Hugo was not found on this computer.\n\n" +
                  "Choose an option:\n" +
                  "  Yes    → Select hugo.exe manually\n" +
                  "  No     → Download Hugo (~50 MB) automatically\n" +
                  "  Cancel → Skip"
                : "未在本机找到 Hugo。\n\n" +
                  "请选择操作：\n" +
                  "  是    → 手动选择 hugo.exe 路径\n" +
                  "  否    → 自动下载 Hugo（约 50 MB）\n" +
                  "  取消  → 跳过",
            _isEnglish ? "Hugo Not Found" : "未找到 Hugo",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        // 用户选择"是" → 手动选择 hugo.exe 路径
        if (choice == MessageBoxResult.Yes)
        {
            var dialog = new OpenFileDialog
            {
                Title = _isEnglish ? "Select hugo.exe" : "选择 hugo.exe",
                Filter = "Hugo Executable (hugo.exe)|hugo.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == true)
            {
                _apiSettings.HugoPath = dialog.FileName;
                _apiSettings.Save();
                Log(_isEnglish
                    ? $"Hugo path saved: {dialog.FileName}"
                    : $"Hugo 路径已保存：{dialog.FileName}");
                return dialog.FileName;
            }
            Log(_isEnglish ? "Hugo selection cancelled by user." : "用户取消了 Hugo 路径选择。");
            return null;
        }

        // 用户选择"否" → 自动下载
        if (choice == MessageBoxResult.No)
        {
            // 继续执行下面的下载逻辑
        }
        else
        {
            Log(_isEnglish
                ? "Hugo setup cancelled by user."
                : "用户取消了 Hugo 设置。");
            return null;
        }

        // extended 版：支持 SCSS/SASS 编译（现代 Hugo 主题普遍要求）
        var url =
            $"https://github.com/gohugoio/hugo/releases/download/v{HugoVersion}/hugo_extended_{HugoVersion}_windows-amd64.zip";
        var tempZip = Path.Combine(appDir, "hugo_download.zip");

        try
        {
            Log(_isEnglish
                ? $"Downloading Hugo v{HugoVersion} extended ... 0%"
                : $"正在下载 Hugo v{HugoVersion} extended ... 0%");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var lastPercent = -1;
                var lastUpdate = DateTime.MinValue;

                await using var source = await response.Content.ReadAsStreamAsync();
                await using var fs = File.Create(tempZip);
                var buffer = new byte[81920];
                long downloaded = 0;

                while (true)
                {
                    var read = await source.ReadAsync(buffer, System.Threading.CancellationToken.None);
                    if (read == 0) break;
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;

                    // 每 250ms 或进度变化时更新日志（复用同一行，避免刷屏）
                    var now = DateTime.Now;
                    var percent = totalBytes > 0 ? (int)(downloaded * 100 / totalBytes) : -1;
                    if (percent != lastPercent || now - lastUpdate > TimeSpan.FromMilliseconds(250))
                    {
                        lastPercent = percent;
                        lastUpdate = now;
                        var mb = downloaded / 1024.0 / 1024.0;
                        var totalMb = totalBytes / 1024.0 / 1024.0;
                        LogDownloadProgress(_isEnglish
                            ? (totalBytes > 0
                                ? $"Downloading Hugo v{HugoVersion} extended ... {percent}% ({mb:0.0} MB / {totalMb:0.0} MB)"
                                : $"Downloading Hugo v{HugoVersion} extended ... {mb:0.0} MB")
                            : (totalBytes > 0
                                ? $"正在下载 Hugo v{HugoVersion} extended ... {percent}% ({mb:0.0} MB / {totalMb:0.0} MB)"
                                : $"正在下载 Hugo v{HugoVersion} extended ... {mb:0.0} MB"));
                    }
                }
            }

            Log(_isEnglish ? "Extracting hugo.exe..." : "正在解压 hugo.exe...");
            using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Read))
            {
                var entry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("hugo.exe", StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    throw new InvalidOperationException("hugo.exe not found in archive");
                entry.ExtractToFile(target, overwrite: true);
            }

            File.Delete(tempZip);

            if (!File.Exists(target))
                throw new InvalidOperationException("Downloaded hugo.exe is missing");

            Log(_isEnglish
                ? $"Hugo downloaded to: {target}"
                : $"Hugo 已下载到：{target}");
            return target;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            Log(_isEnglish
                ? $"Failed to download Hugo: {ex.Message}"
                : $"Hugo 下载失败：{ex.Message}");
            MessageBox.Show(
                _isEnglish
                    ? $"Failed to download Hugo:\n{ex.Message}\n\n" +
                      "Please check your network, or manually place hugo.exe next to Huge.exe."
                    : $"Hugo 下载失败：\n{ex.Message}\n\n" +
                      "请检查网络，或手动将 hugo.exe 放到 Huge.exe 同目录下。",
                _isEnglish ? "Error" : "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private async void HugoServeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_projectPath == null) return;

        // 确定 Hugo 可执行文件；若不存在，自动询问并下载（无需用户安装/配置 PATH）
        var hugoPath = await EnsureHugoExecutableAsync();
        if (hugoPath == null)
        {
            // 用户取消下载或下载失败
            return;
        }

        try
        {
            _hugoProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = hugoPath,
                    Arguments = "server --disableFastRender",
                    WorkingDirectory = _projectPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    // Go 程序（Hugo）以 UTF-8 输出，必须显式指定 UTF-8 解码，
                    // 否则在中文系统（GBK 代码页）下会乱码
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };
            _hugoProcess.EnableRaisingEvents = true;

            var siteOpened = false;
            var readySeen = false;

            void CheckHugoOutput(string data)
            {
                if (string.IsNullOrEmpty(data)) return;
                Log(data);

                // Hugo 输出为英文，不随界面语言变化
                if (!readySeen &&
                    (data.Contains("Web Server is available") ||
                     data.Contains("Serving pages from") ||
                     data.Contains("localhost:1313")))
                {
                    readySeen = true;
                    Dispatcher.Invoke(() =>
                    {
                        if (!siteOpened)
                        {
                            siteOpened = true;
                            OpenSiteCore();
                        }
                    });
                }

                // 端口被占用时明确提示
                if (data.Contains("already in use") ||
                    data.Contains("address already in use"))
                {
                    Dispatcher.Invoke(() => MessageBox.Show(
                        _isEnglish
                            ? "Port 1313 is already in use. Please stop the other Hugo instance and try again."
                            : "端口 1313 已被占用。请先停止其他 Hugo 实例后重试。",
                        _isEnglish ? "Error" : "错误",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
                }
            }

            _hugoProcess.OutputDataReceived += (s, args) => CheckHugoOutput(args.Data ?? "");
            _hugoProcess.ErrorDataReceived += (s, args) => CheckHugoOutput(args.Data ?? "");

            // 进程意外退出时记录退出码，帮助排查
            _hugoProcess.Exited += (s, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    int? code = null;
                    try { code = _hugoProcess?.ExitCode; } catch { }
                    Log(_isEnglish
                        ? $"Hugo process exited (code {code?.ToString() ?? "unknown"})."
                        : $"Hugo 进程已退出（退出码 {code?.ToString() ?? "未知"}）。");
                    HugoServeBtn.IsEnabled = true;
                    HugoStopBtn.IsEnabled = false;
                });
            };

            _hugoProcess.Start();
            _hugoProcess.BeginOutputReadLine();
            _hugoProcess.BeginErrorReadLine();
            HugoServeBtn.IsEnabled = false;
            HugoStopBtn.IsEnabled = true;
            Log(_isEnglish ? "Hugo server starting..." : "Hugo server 正在启动...");

            // 兜底：12 秒内未检测到就绪标志则提示查看日志
            var timeout = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(12)
            };
            timeout.Tick += (s, args) =>
            {
                timeout.Stop();
                if (!readySeen)
                {
                    Log(_isEnglish
                        ? "Hugo did not report ready within 12s. Check the log above."
                        : "Hugo 12 秒内未就绪，请查看上方日志。");
                    if (_hugoProcess != null && _hugoProcess.HasExited)
                    {
                        HugoServeBtn.IsEnabled = true;
                        HugoStopBtn.IsEnabled = false;
                    }
                }
            };
            timeout.Start();
        }
        catch (Exception ex)
        {
            _hugoProcess?.Dispose();
            _hugoProcess = null;
            var msg = _isEnglish
                ? $"Failed to start Hugo: {ex.Message}\n\n" +
                  "Please make sure Hugo is available in one of these places:\n" +
                  "  1. Place hugo.exe next to Huge.exe (recommended)\n" +
                  "  2. Add Hugo to your system PATH"
                : $"启动 Hugo 失败: {ex.Message}\n\n" +
                  "请确保 Hugo 存在于以下任一位置：\n" +
                  "  1. 将 hugo.exe 放到 Huge.exe 同目录下（推荐）\n" +
                  "  2. 将 Hugo 添加到系统 PATH 环境变量";
            MessageBox.Show(msg, _isEnglish ? "Error" : "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 在浏览器中打开本地站点
    private void OpenSiteCore()
    {
        try
        {
            Process.Start(new ProcessStartInfo("http://localhost:1313") { UseShellExecute = true });
            Log(_isEnglish ? "Opened site in browser." : "已在浏览器中打开站点。");
        }
        catch (Exception ex)
        {
            Log($"{(_isEnglish ? "Cannot open browser: " : "无法打开浏览器: ")}{ex.Message}");
        }
    }

    private void HugoStopBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_hugoProcess != null && !_hugoProcess.HasExited)
        {
            _hugoProcess.Kill();
            _hugoProcess.Dispose();
            _hugoProcess = null;
            Log(_isEnglish ? "Hugo server stopped." : "Hugo server 已停止。");
        }
        HugoServeBtn.IsEnabled = true;
        HugoStopBtn.IsEnabled = false;
    }

    // ===== Git =====
    private void GitCommitBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_projectPath == null) return;
        var message = InputDialog(_isEnglish ? "Commit & Push" : "提交推送",
            _isEnglish ? "Commit message:" : "提交信息:", "update");
        if (string.IsNullOrWhiteSpace(message)) return;

        RunGitCommand("add -A");
        RunGitCommand($"commit -m \"{message.Replace("\"", "\\\"")}\"");
        RunGitCommand("push");
    }

    private void RunGitCommand(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = _projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                // Git 输出为 UTF-8，显式指定编码避免中文乱码
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (!string.IsNullOrEmpty(output)) Log(output.Trim());
            if (!string.IsNullOrEmpty(error)) Log(error.Trim());
        }
        catch (Exception ex)
        {
            Log($"{(_isEnglish ? "Git command failed: " : "Git 命令失败: ")}{ex.Message}");
        }
    }

    // ===== 文件夹镜像 =====
    private void MirrorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null)
        {
            MessageBox.Show(_isEnglish ? "Please select a file first." : "请先选择一个文件。",
                _isEnglish ? "Notice" : "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 保存当前更改
        if (_isDirty) SaveCurrentFile();

        // 查找 content 目录下的语言子文件夹（镜像组）
        var contentDir = Path.Combine(_projectPath!, "content");
        if (!Directory.Exists(contentDir))
        {
            MessageBox.Show(_isEnglish ? "No content directory found." : "未找到 content 目录。",
                _isEnglish ? "Notice" : "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 获取当前文件相对 content 的路径
        var relPath = Path.GetRelativePath(contentDir, _currentFile);
        var langDirs = Directory.GetDirectories(contentDir)
            .Where(d => IsLanguageDir(Path.GetFileName(d)))
            .OrderBy(d => d)
            .ToList();

        if (langDirs.Count < 2)
        {
            var msg = _isEnglish
                ? "At least two language folders are needed for mirroring.\nExample: content/en, content/zh"
                : "需要至少两个语言文件夹才能使用镜像功能。\n例如：content/en、content/zh";
            MessageBox.Show(msg, _isEnglish ? "Notice" : "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 选择目标语言
        var currentLang = GetCurrentLanguage(contentDir, _currentFile);
        var options = langDirs
            .Where(d => Path.GetFileName(d) != currentLang)
            .Select(d => Path.GetFileName(d))
            .ToArray();

        var target = InputDialog(_isEnglish ? "Mirror Copy" : "镜像复制",
            _isEnglish ? "Select target language folder:" : "选择目标语言文件夹:", options[0]);
        if (string.IsNullOrWhiteSpace(target)) return;
        if (!options.Contains(target))
        {
            MessageBox.Show(_isEnglish ? "Invalid language folder." : "无效的语言文件夹。",
                _isEnglish ? "Notice" : "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targetPath = Path.Combine(contentDir, target, relPath);
        var targetDir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDir);
        File.Copy(_currentFile, targetPath, true);
        BuildFileTree();

        // 在新位置打开目标文件以便继续编辑
        Log($"{(_isEnglish ? "Mirrored: " : "已镜像复制: ")}{_currentFile} → {targetPath}");
        MessageBox.Show(
            _isEnglish ? $"Copied to {target} language folder. Opening the target file for editing..."
                       : $"已复制到 {target} 语言文件夹。正在打开目标文件以进行编辑...",
            _isEnglish ? "Mirror Copy Complete" : "镜像复制完成", MessageBoxButton.OK, MessageBoxImage.Information);

        // 打开镜像复制的文件
        var newItem = FindTreeItem(FileTree.Items, targetPath);
        if (newItem != null)
        {
            newItem.IsSelected = true;
            OpenFile(targetPath);
        }
    }

    private bool IsLanguageDir(string name)
    {
        // 常见语言代码：en, zh, fr, de, es, ja, ko, ru, ro 等
        return name.Length is 2 or 3 && name.All(char.IsLetter);
    }

    private string? GetCurrentLanguage(string contentDir, string filePath)
    {
        var rel = Path.GetRelativePath(contentDir, filePath);
        var first = rel.Split(Path.DirectorySeparatorChar)[0];
        return IsLanguageDir(first) ? first : null;
    }

    // 刷新文件树（供导入的新文件刷新显示）
    private void RefreshTreeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_projectPath == null)
        {
            Log(_isEnglish ? "No project opened." : "未打开项目。");
            return;
        }
        BuildFileTree();
        Log(_isEnglish ? "File tree refreshed." : "文件树已刷新。");
    }

    // 在文件资源管理器中显示所选文件/文件夹
    private void CtxShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var path = GetSelectedPath();
        if (path == null)
        {
            MessageBox.Show(_isEnglish ? "Please select a file or folder first." : "请先选择一个文件或文件夹。",
                _isEnglish ? "Notice" : "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (File.Exists(path))
            {
                // 打开文件所在目录并选中文件
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{(_isEnglish ? "Failed to open explorer: " : "无法打开资源管理器: ")}{ex.Message}",
                _isEnglish ? "Error" : "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearLogBtn_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    // ===== 关闭时清理 =====
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isDirty)
        {
            var msg = _isEnglish ? "There are unsaved changes. Save?" : "有未保存的更改，是否保存？";
            var result = MessageBox.Show(msg, _isEnglish ? "Unsaved Changes" : "未保存更改",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (result == MessageBoxResult.Yes) SaveCurrentFile();
        }
        if (_hugoProcess != null && !_hugoProcess.HasExited)
        {
            _hugoProcess.Kill();
            _hugoProcess.Dispose();
        }
        base.OnClosing(e);
    }

    // ===== 简单输入对话框（跟随全局主题）=====
    private static string? InputDialog(string title, string prompt, string defaultValue)
    {
        var window = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow
        };
        ThemeHelper.ApplyTheme(window);
        var panel = new StackPanel { Margin = new Thickness(15) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var textBox = new TextBox { Text = defaultValue, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        panel.Children.Add(textBox);
        var okBtn = new Button { Content = "确定", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(okBtn);
        window.Content = panel;
        window.Loaded += (s, e) => { textBox.Focus(); textBox.SelectAll(); };
        okBtn.Click += (s, e) => { window.DialogResult = true; window.Close(); };
        textBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { window.DialogResult = true; window.Close(); } };
        return window.ShowDialog() == true ? textBox.Text : null;
    }

    // ===== AI 助手 =====
    private void AiSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ApiSettingsDialog(_apiSettings) { Owner = this };
        dialog.ShowDialog();
    }

    // 折叠/展开：完全隐藏 AI 面板 + 其左侧分隔条（第3、4列）。
    // 关键：ColumnDefinition 的 MinWidth=220 会强制保留至少 220px，
    // 只把 Width 设 0 无法隐藏——正是“残留窗口”的根因，必须先清除该约束。
    private void AiCollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        // 折叠前保存当前面板宽度（像素），供下次展开时恢复
        var aiCol = RootGrid.ColumnDefinitions[4];
        if (aiCol.ActualWidth > 0)
        {
            _apiSettings.AiPanelWidth = aiCol.ActualWidth;
            _apiSettings.Save();
        }

        var splitterCol = RootGrid.ColumnDefinitions[3];
        // 清除列最小/最大约束，否则 MinWidth=220 留下 220px 空白区
        aiCol.MinWidth = 0;
        aiCol.MaxWidth = double.PositiveInfinity;
        splitterCol.Width = new GridLength(0);
        aiCol.Width = new GridLength(0);
        AiSplitter.Visibility = Visibility.Collapsed;
        AiPanel.Visibility = Visibility.Collapsed;
        AiExpandBtn.Visibility = Visibility.Visible;
    }

    private void AiExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        var splitterCol = RootGrid.ColumnDefinitions[3];
        var aiCol = RootGrid.ColumnDefinitions[4];
        // 恢复最小约束
        aiCol.MinWidth = 220;
        splitterCol.Width = new GridLength(5);

        // 若保存过面板宽度，则恢复上次的宽度（像素）；否则用默认 2* 比例
        if (_apiSettings.AiPanelWidth > 0)
        {
            aiCol.Width = new GridLength(_apiSettings.AiPanelWidth);
        }
        else
        {
            aiCol.Width = new GridLength(2, GridUnitType.Star);
        }

        AiSplitter.Visibility = Visibility.Visible;
        AiPanel.Visibility = Visibility.Visible;
        AiExpandBtn.Visibility = Visibility.Collapsed;
    }

    // GridSplitter 拖拽完成后：把 AI 面板列换算为与编辑区的相对比例（star），
    // 这样拖拽后窗口缩放仍能保持比例自适应，而非固定像素
    private void AiSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var editorCol = RootGrid.ColumnDefinitions[2];
        var aiCol = RootGrid.ColumnDefinitions[4];

        if (editorCol.ActualWidth > 0 && aiCol.ActualWidth > 0)
        {
            // 以编辑区为基础 1*，AI 按实际宽度换算（夹在合理区间内）
            var ratio = Math.Clamp(aiCol.ActualWidth / editorCol.ActualWidth, 0.15, 1.0);
            editorCol.Width = new GridLength(1, GridUnitType.Star);
            aiCol.Width = new GridLength(ratio, GridUnitType.Star);

            // 保存当前宽度（像素），供折叠后恢复
            _apiSettings.AiPanelWidth = aiCol.ActualWidth;
            _apiSettings.Save();
        }
    }

    // ===== Cline 式消息渲染 =====
    // User：右侧紧凑气泡（紫色底 + 白字），无多余标注
    // Assistant：左侧起全宽 Markdown 文本，顶部小字标注「ASSISTANT · 模型名·时间」，
    //            底部操作栏（复制 / 插入编辑器 / 重新生成）

    private void AddAiBubble(string role, string text, bool renderMarkdown = false)
    {
        if (role == "user")
        {
            AddUserBubble(text);
        }
        else
        {
            AddAssistantBubble(text, renderMarkdown);
        }
    }

    // User 消息：右侧紧凑气泡 + 底部复制按钮（TextBlock 不可选中，需提供复制入口）
    private void AddUserBubble(string text)
    {
        var outer = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 8)
        };

        var bubble = new Border
        {
            CornerRadius = new CornerRadius(8, 8, 2, 8),
            Padding = new Thickness(10, 7, 10, 7),
            MaxWidth = Math.Max(150, AiChatPanel.ActualWidth - 60),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(0xC5, 0x64, 0x73)),
            Child = new TextBox
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                IsTabStop = false,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji, Microsoft YaHei UI")
            }
        };
        outer.Children.Add(bubble);

        // 复制按钮：用户消息也支持一键复制
        var copyBtn = CreateActionButton("⧉ 复制", _isEnglish ? "Copy" : "复制");
        copyBtn.HorizontalAlignment = HorizontalAlignment.Right;
        copyBtn.Margin = new Thickness(0, 2, 0, 0);
        copyBtn.Click += (s, e) => CopyAiReply(text);
        outer.Children.Add(copyBtn);

        AiChatPanel.Children.Add(outer);
        ScrollAiToEnd();
    }

    // Assistant 消息：全宽 Markdown + 标注行 + 操作栏（仿 Cline）
    private void AddAssistantBubble(string text, bool renderMarkdown)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

        // 标注行（小字：ASSISTANT · 模型 · 时间）
        var label = new TextBlock
        {
            Text = $"ASSISTANT · {_apiSettings.Model} · {DateTime.Now:HH:mm}",
            FontSize = 10,
            Foreground = new SolidColorBrush(IsDarkTheme
                ? Color.FromRgb(0x9D, 0x9D, 0x9D)
                : Color.FromRgb(0x78, 0x76, 0x70)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        outer.Children.Add(label);

        // 内容：Markdown 渲染用只读 RichTextBox（支持文本选择/复制），纯文本用只读 TextBox
        if (renderMarkdown && !string.IsNullOrEmpty(text))
        {
            var richBox = new RichTextBox
            {
                Document = MarkdownRenderer.Render(text, Math.Max(180, AiChatPanel.ActualWidth)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                IsReadOnly = true,
                IsDocumentEnabled = true,
                IsTabStop = false,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji, Microsoft YaHei UI")
            };
            // RichTextBox 即使禁用滚动条也会拦截鼠标滚轮，
            // 导致悬停其上时外层 AiScroll 无法滚动。这里把滚轮事件转发给 AiScroll。
            richBox.PreviewMouseWheel += (s, e) =>
            {
                var scroll = AiScroll;
                if (scroll == null) return;
                var delta = e.Delta;
                if (delta < 0) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 40);
                else scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 40);
                e.Handled = true;
            };
            outer.Children.Add(richBox);
        }
        else
        {
            // 只读 TextBox 支持文本选择/复制
            outer.Children.Add(new TextBox
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                IsTabStop = false,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(IsDarkTheme
                    ? Color.FromRgb(0xD4, 0xD4, 0xD4)
                    : Color.FromRgb(0x24, 0x23, 0x1F)),
                FontSize = 12.5,
                FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji, Microsoft YaHei UI")
            });
        }

        // 操作栏
        if (renderMarkdown && !string.IsNullOrEmpty(text))
        {
            var actionBar = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = false };
            var copyBtn = CreateActionButton("⧉ 复制", _isEnglish ? "Copy" : "复制");
            copyBtn.Click += (s, e) => CopyAiReply(text);
            DockPanel.SetDock(copyBtn, Dock.Left);
            actionBar.Children.Add(copyBtn);

            var insertBtn = CreateActionButton("⤓ 插入编辑器", _isEnglish ? "Insert to Editor" : "插入编辑器");
            insertBtn.Click += (s, e) => InsertAiReplyToEditor(text);
            DockPanel.SetDock(insertBtn, Dock.Left);
            actionBar.Children.Add(insertBtn);

            // 方形圆角 ↻ 按钮：置于"插入编辑器"右侧，紧凑样式
            var regenBtn = CreateActionButton("↻", _isEnglish ? "Regenerate" : "重新生成");
            regenBtn.Click += async (s, e) => await RegenerateAiReplyAsync();
            regenBtn.Width = 24;
            regenBtn.Height = 22;
            regenBtn.Padding = new Thickness(0);
            regenBtn.FontSize = 11;
            DockPanel.SetDock(regenBtn, Dock.Left);
            actionBar.Children.Add(regenBtn);

            outer.Children.Add(actionBar);
        }

        AiChatPanel.Children.Add(outer);
        ScrollAiToEnd();
    }

    // 流式 Assistant 消息（仿 Cline）：顶部小字「正在思考…」，内容纯文本实时追加
    private void BeginAiStreamBubble()
    {
        var outer = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

        _aiStreamStatus = new TextBlock
        {
            Text = "ASSISTANT · 正在思考…",
            FontSize = 10,
            Foreground = new SolidColorBrush(IsDarkTheme
                ? Color.FromRgb(0x9D, 0x9D, 0x9D)
                : Color.FromRgb(0x78, 0x76, 0x70)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        outer.Children.Add(_aiStreamStatus);

        _aiStreamTextBlock = new TextBlock
        {
            Text = "",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(IsDarkTheme
                ? Color.FromRgb(0xD4, 0xD4, 0xD4)
                : Color.FromRgb(0x24, 0x23, 0x1F)),
            FontSize = 12.5,
            LineHeight = 18,
            FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji, Microsoft YaHei UI")
        };
        outer.Children.Add(_aiStreamTextBlock);

        AiChatPanel.Children.Add(outer);
        _aiStreamPlain = "";
        ScrollAiToEnd();
    }

    // 流式完成：移除流式块，替换为静态 Assistant 消息
    private void FinishAiStream(string finalText)
    {
        if (_aiStreamStatus != null)
        {
            var parent = _aiStreamStatus.Parent as StackPanel;
            if (parent != null) AiChatPanel.Children.Remove(parent);
        }
        _aiStreamTextBlock = null;
        _aiStreamStatus = null;

        _lastAssistantText = finalText;
        AddAssistantBubble(finalText, renderMarkdown: true);
    }

    // ===== AI 创建文件/文件夹能力 =====
    // AI 通过以下结构化标记请求创建 Markdown 文章或文件夹：
    // [CREATE_FILE path="posts/my-post.md"]
    // ...文件内容（含 frontmatter）...
    // [/END_FILE]
    // [CREATE_DIR path="posts/2025"]  （可选，无需结束标记）
    // 解析后自动写入 content 目录、刷新文件树并在对话中追加结果。
    private void HandleAiCreateFileCommands(ref string aiText)
    {
        if (_projectPath == null) return;

        var contentDir = Path.Combine(_projectPath, "content");
        if (!Directory.Exists(contentDir))
        {
            AddAiBubble("assistant", _isEnglish
                ? "Cannot create files: no content/ directory found in this project."
                : "无法创建文件：当前项目没有 content/ 目录。");
            return;
        }

        var created = new List<string>();
        var errors = new List<string>();

        var filePattern = @"\[CREATE_FILE path=""([^""]+)""\]([\s\S]*?)\[/END_FILE\]";
        var fileMatches = Regex.Matches(aiText, filePattern);

        foreach (Match m in fileMatches)
        {
            var relPath = m.Groups[1].Value.Trim();
            var body = m.Groups[2].Value.Trim().TrimStart('\n');

            if (string.IsNullOrWhiteSpace(relPath) || Path.IsPathRooted(relPath))
            {
                errors.Add(relPath);
                continue;
            }

            // 强制 .md 扩展名
            if (!relPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                relPath += ".md";

            // 防止路径穿越
            var fullPath = Path.GetFullPath(Path.Combine(contentDir, relPath));
            if (!fullPath.StartsWith(contentDir, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(relPath);
                continue;
            }

            try
            {
                if (File.Exists(fullPath))
                {
                    errors.Add(relPath + (_isEnglish ? " (already exists)" : "（已存在）"));
                    continue;
                }
                var dir = Path.GetDirectoryName(fullPath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, body, Encoding.UTF8);
                created.Add(relPath);
            }
            catch (Exception ex)
            {
                errors.Add($"{relPath}: {ex.Message}");
            }
        }

        // 创建文件夹标记：[CREATE_DIR path="posts/2025"]
        var dirPattern = @"\[CREATE_DIR path=""([^""]+)""\]";
        var dirMatches = Regex.Matches(aiText, dirPattern);
        foreach (Match m in dirMatches)
        {
            var relPath = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(relPath) || Path.IsPathRooted(relPath))
            {
                errors.Add(relPath);
                continue;
            }

            var fullDir = Path.GetFullPath(Path.Combine(contentDir, relPath));
            if (!fullDir.StartsWith(contentDir, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(relPath);
                continue;
            }

            try
            {
                Directory.CreateDirectory(fullDir);
                created.Add(relPath + "/");
            }
            catch (Exception ex)
            {
                errors.Add($"{relPath}: {ex.Message}");
            }
        }

        if (created.Count > 0)
        {
            BuildFileTree();
            Log(_isEnglish
                ? $"AI created {created.Count} item(s)"
                : $"AI 已创建 {created.Count} 个项目");
        }

        // 将创建结果追加到 AI 回复（移除标记）
        aiText = Regex.Replace(aiText, filePattern, "").Trim();
        aiText = Regex.Replace(aiText, dirPattern, "").Trim();

        var sb = new StringBuilder();
        if (created.Count > 0)
        {
            sb.AppendLine(_isEnglish
                ? $"\n\n✅ 已创建："
                : $"\n\n✅ 已创建：");
            foreach (var c in created)
                sb.AppendLine($"- `{c}`");
        }
        if (errors.Count > 0)
        {
            sb.AppendLine(_isEnglish
                ? $"\n⚠️ 以下操作失败："
                : $"\n⚠️ 以下操作失败：");
            foreach (var err in errors)
                sb.AppendLine($"- `{err}`");
        }

        // 若 AI 原始回复只有标记没有其他内容，则只显示结果
        if (string.IsNullOrWhiteSpace(aiText))
        {
            aiText = sb.ToString().Trim();
        }
        else
        {
            aiText += sb.ToString();
        }
    }

    // 清空对话
    private void AiClearBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_aiCts != null)
        {
            _aiCts.Cancel();
            _aiCts = null;
        }
        AiChatPanel.Children.Clear();
        _aiHistory.Clear();
        _lastUserText = null;
        _lastAssistantText = null;
        AiStopBtn.Visibility = Visibility.Collapsed;
        AiSendBtn.IsEnabled = true;
        AiInputBox.IsEnabled = true;
        AddAiBubble("assistant", _isEnglish
            ? "Conversation cleared. How can I help you?"
            : "对话已清空。有什么可以帮你的？");
    }

    // 复制回复
    private void CopyAiReply(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            Log(_isEnglish ? "AI reply copied." : "已复制 AI 回复。");
        }
        catch (Exception ex)
        {
            Log($"{( _isEnglish ? "Copy failed: " : "复制失败: ")}{ex.Message}");
        }
    }

    // 插入回复到 Markdown 编辑器光标处
    private void InsertAiReplyToEditor(string text)
    {
        if (_currentFile == null)
        {
            AddAiBubble("assistant", _isEnglish
                ? "Please open a Markdown file first to insert the reply."
                : "请先打开一个 Markdown 文件，才能插入回复。");
            return;
        }
        var start = EditorBox.SelectionStart;
        EditorBox.SelectedText = "\n" + text.Trim() + "\n";
        EditorBox.Focus();
        EditorBox.CaretIndex = start + text.Length + 2;
        Log(_isEnglish ? "AI reply inserted into editor." : "已将 AI 回复插入编辑器。");
    }

    // 重新生成：重发最后一条用户消息
    private async Task RegenerateAiReplyAsync()
    {
        if (string.IsNullOrEmpty(_lastUserText)) return;
        // 移除最后一条 assistant 回复（静态气泡）
        if (_lastAssistantText != null)
        {
            // Cline 风格：assistant 消息 = StackPanel[标注行(TextBlock ASSISTANT…) + 内容(FlowDocumentScrollViewer/TextBlock) + 操作栏(DockPanel)]
            for (var i = AiChatPanel.Children.Count - 1; i >= 0; i--)
            {
                if (AiChatPanel.Children[i] is StackPanel sp &&
                    sp.Children.Count >= 3 &&
                    sp.Children[0] is TextBlock lbl &&
                    lbl.Text.StartsWith("ASSISTANT ·") &&
                    sp.Children[^1] is DockPanel)
                {
                    AiChatPanel.Children.RemoveAt(i);
                    break;
                }
            }
        }

        if (_aiHistory.Count >= 1 && _aiHistory[^1].Role == "assistant")
        {
            _aiHistory.RemoveAt(_aiHistory.Count - 1);
        }
        _lastAssistantText = null;
        await SendAiMessageAsync(_lastUserText!);
    }

    // ===== AI 面板自适应 =====
    // 面板宽度变化时：重排已渲染消息（用户气泡宽度、助手 Markdown 页宽），
    // 使窗口放大/缩小、拖拽 AI 分隔条时内容实时跟随
    private int _debounceTick;
    private void AiChatPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 尺寸未变化则不处理
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1) return;

        // 简单防抖：避免拖拽过程中高频重排导致卡顿（延迟 120ms 合并）
        _debounceTick++;
        var myTick = _debounceTick;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                if (myTick != _debounceTick) return; // 已有更新的尺寸事件，跳过
                RelayoutAiMessages();
            }));
    }

    // 重排 AI 消息
    private void RelayoutAiMessages()
    {
        var panelWidth = Math.Max(160, AiChatPanel.ActualWidth);

        foreach (var child in AiChatPanel.Children)
        {
            if (child is not StackPanel sp) continue;
            if (sp.Children.Count == 0) continue;

            // 用户消息：StackPanel[Border + 复制按钮]，限制气泡最大宽度
            if (sp.Children[0] is Border userBorder &&
                userBorder.HorizontalAlignment == HorizontalAlignment.Right)
            {
                userBorder.MaxWidth = Math.Max(150, panelWidth - 60);
                continue;
            }

            // 助手消息：StackPanel[标注行 + RichTextBox/TextBox + 操作栏(DockPanel)]
            foreach (var item in sp.Children)
            {
                // 更新 Markdown 渲染页宽 → FlowDocument 自动重排换行
                if (item is RichTextBox rtb && rtb.Document != null)
                {
                    rtb.Document.PageWidth = Math.Max(120, panelWidth - 24);
                    rtb.Document.PagePadding = new Thickness(0);
                }
            }
        }
    }

    // 输入框高度自适应：内容增多时高度 40→120 增长，清空回到 40
    private void AiInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var tb = (TextBox)sender;
        var lineCount = tb.Text.Count(c => c == '\n') + 1;
        // 每行约 22px + 上下 padding 12px，夹在 48~120 之间
        var desiredHeight = Math.Clamp(lineCount * 22 + 12, 48, 120);
        if (Math.Abs(tb.Height - desiredHeight) > 2)
        {
            tb.Height = desiredHeight;
        }
    }

    // 小型操作按钮
    private static Button CreateActionButton(string icon, string tooltip)
    {
        return new Button
        {
            Content = icon,
            ToolTip = tooltip,
            Padding = new Thickness(4, 1, 4, 1),
            FontSize = 10,
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    private void ScrollAiToEnd()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => AiScroll.ScrollToEnd()));
    }

    private async void AiSendBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = AiInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        AiInputBox.Clear();
        await SendAiMessageAsync(text);
    }

    private async void AiInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter 发送，Shift+Enter 换行
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            var text = AiInputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            AiInputBox.Clear();
            await SendAiMessageAsync(text);
        }
    }

    private void AiStopBtn_Click(object sender, RoutedEventArgs e)
    {
        _aiCts?.Cancel();
        _aiCts = null;
        AiStopBtn.Visibility = Visibility.Collapsed;
        AiSendBtn.IsEnabled = true;
        AiInputBox.IsEnabled = true;
        // 保留已流式输出的部分，转为静态以展示
        if (_aiStreamTextBlock != null && _aiStreamStatus != null)
        {
            var partial = _aiStreamPlain ?? "";
            FinishAiStream(partial + (_isEnglish ? "\n\n[stopped]" : "\n\n[已停止]"));
        }
    }

    // 分析当前文件：将文件内容发送给 AI
    private async void AiAnalyzeFileBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null)
        {
            AddAiBubble("assistant", _isEnglish
                ? "Please open a file first."
                : "请先打开一个文件。");
            return;
        }
        EnsureAiPanelExpanded();
        var content = File.ReadAllText(_currentFile);
        await SendAiMessageAsync(_isEnglish
            ? $"Please analyze the following file (name: {Path.GetFileName(_currentFile)}) and provide a summary, frontmatter suggestions, and improvement advice:\n\n{Truncate(content, 8000)}"
            : $"请分析以下文件（文件名：{Path.GetFileName(_currentFile)}），给出内容摘要、Frontmatter 建议和改进意见：\n\n{Truncate(content, 8000)}");
    }

    // 分析项目结构：将项目目录树发送给 AI
    private async void AiAnalyzeProjectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_projectPath == null)
        {
            AddAiBubble("assistant", _isEnglish
                ? "Please open a project first."
                : "请先打开一个项目。");
            return;
        }
        EnsureAiPanelExpanded();
        var tree = GetProjectTreeContext();
        await SendAiMessageAsync(_isEnglish
            ? $"Here is the project structure (Hugo site). Please analyze it and suggest improvements, organization tips, or potential issues:\n\n{tree}"
            : $"以下是 Hugo 项目的目录结构，请分析并给出改进建议、组织方式建议或潜在问题：\n\n{tree}");
    }

    // 若 AI 面板已折叠，先展开
    private void EnsureAiPanelExpanded()
    {
        if (AiPanel.Visibility != Visibility.Visible)
        {
            AiExpandBtn_Click(this, new RoutedEventArgs());
        }
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text[..max] + "\n...(truncated)";
    }

    // 生成项目树文本（供 AI 分析）
    // 与左侧文件树（BuildFileTree）保持一致：只展示 content/static/assets 三个核心目录，
    // 跳过 layouts/ 等渲染主题目录与后台生成目录，避免 AI 被无关结构干扰。
    private string GetProjectTreeContext()
    {
        if (_projectPath == null) return "";
        var sb = new StringBuilder();
        sb.AppendLine(Path.GetFileName(_projectPath) + "/");

        // 隐藏技术性/生成目录（与文件树一致）
        var hiddenDirs = new HashSet<string> { "public", "resources", ".git", ".hugo_build.lock", "themes", "node_modules", "layouts" };

        // 核心目录：仅 content、static、assets
        var coreDirs = new[] { "content", "static", "assets" };

        foreach (var dirName in coreDirs)
        {
            var dirPath = Path.Combine(_projectPath, dirName);
            if (!Directory.Exists(dirPath)) continue;

            sb.AppendLine("  " + dirName + "/");
            AppendDirContext(sb, dirPath, "    ", hiddenDirs);
        }
        return sb.ToString();
    }

    // 递归追加目录结构（两级缩进，跳过隐藏目录）
    private void AppendDirContext(StringBuilder sb, string dirPath, string indent, HashSet<string> hiddenDirs)
    {
        foreach (var dir in Directory.GetDirectories(dirPath).OrderBy(d => d))
        {
            var name = Path.GetFileName(dir);
            if (hiddenDirs.Contains(name)) continue;
            sb.AppendLine($"{indent}{name}/");
            AppendDirContext(sb, dir, indent + "  ", hiddenDirs);
        }

        foreach (var file in Directory.GetFiles(dirPath).OrderBy(f => f))
        {
            var ext = Path.GetExtension(file).ToLower();
            if (ext is ".md" or ".markdown" or ".yaml" or ".yml" or ".toml" or ".json"
                or ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" or ".ico")
            {
                sb.AppendLine($"{indent}{Path.GetFileName(file)}");
            }
        }
    }

    // 发送消息 + 流式接收回复（Cline 式：开始 → 思考中 → 逐字流式 → 完成转 Markdown）
    private async Task SendAiMessageAsync(string userText)
    {
        if (string.IsNullOrWhiteSpace(_apiSettings.ApiKey))
        {
            AddAiBubble("assistant", _isEnglish
                ? "Please configure the DeepSeek API key first (⚙ button in the AI panel)."
                : "请先配置 DeepSeek API Key（AI 面板右上角 ⚙ 按钮）。");
            return;
        }

        _lastUserText = userText;
        AddAiBubble("user", userText);
        _aiHistory.Add(new DeepSeekClient.ChatMessage { Role = "user", Content = userText });

        // 控制 UI 状态
        AiSendBtn.IsEnabled = false;
        AiInputBox.IsEnabled = false;
        AiStopBtn.Visibility = Visibility.Visible;
        _aiCts = new CancellationTokenSource();

        // 创建流式气泡（思考中状态）
        BeginAiStreamBubble();
        _aiStreamPlain = "";
        string finalText;

        try
        {
            var client = new DeepSeekClient(_apiSettings);

            var msgs = new List<DeepSeekClient.ChatMessage>
            {
                new() { Role = "system", Content = BuildAiSystemPrompt() }
            };
            // 只保留最近 20 条上下文，避免 Token 膨胀
            msgs.AddRange(_aiHistory.Skip(Math.Max(0, _aiHistory.Count - 20)));

            await client.ChatStreamAsync(msgs, delta =>
            {
                _aiStreamPlain += delta;
                if (_aiStreamTextBlock != null)
                {
                    _aiStreamTextBlock.Text = _aiStreamPlain;
                }
                ScrollAiToEnd();
            }, _aiCts.Token);

            finalText = _aiStreamPlain ?? "";
        }
        catch (OperationCanceledException)
        {
            finalText = (_aiStreamPlain ?? "") + (_isEnglish ? "\n\n[stopped]" : "\n\n[已停止]");
        }
        catch (Exception ex)
        {
            finalText = _isEnglish ? $"Request failed: {ex.Message}" : $"请求失败：{ex.Message}";
        }

        // 处理 AI 创建文件指令（在 AI 输入框内显示原文，但执行创建并追加结果）
        var displayText = finalText;
        if (_projectPath != null)
        {
            HandleAiCreateFileCommands(ref displayText);
            finalText = displayText;
        }

        // 完成：转静态 Markdown 气泡
        FinishAiStream(finalText);
        _aiHistory.Add(new DeepSeekClient.ChatMessage
        {
            Role = "assistant",
            Content = finalText
        });

        AiStopBtn.Visibility = Visibility.Collapsed;
        AiSendBtn.IsEnabled = true;
        AiInputBox.IsEnabled = true;
        _aiCts?.Dispose();
        _aiCts = null;
        _aiStreamPlain = null;
    }

    // 系统提示词：让 AI 理解自己是 Hugo 内容助手
    private string BuildAiSystemPrompt()
    {
        var currentFileContext = "";
        if (_currentFile != null && !IsImageFile(_currentFile))
        {
            currentFileContext =
                $"\n当前打开的文件：{_currentFile}\n文件内容（截断）：\n{Truncate(File.ReadAllText(_currentFile), 4000)}";
        }

        return "你是一个 Hugo 静态网站内容管理助手，运行在 Hugo - Markdown Client 桌面应用中。" +
               "你的核心能力：\n" +
               "1. 分析 Markdown 文章，给出总结、Frontmatter（title/date/draft/tags/categories 等）建议与写作改进意见。\n" +
               "2. 理解 Hugo 项目结构（content/static/assets），帮助用户梳理组织方式、发现潜在问题。\n" +
               "3. 将文章翻译为其他语言（保留 Markdown 格式与 frontmatter），用于多语言镜像维护。\n" +
               "4. 回答关于 Hugo、Markdown、Frontmatter、多语言站点搭建等问题，给出可操作建议。\n" +
               "\n=== 文件创建能力 ===\n" +
               "当用户要求你写一篇新文章（如 posts/says/thoughts/note 等）时，你必须使用以下结构化标记：\n" +
               "\\[CREATE_FILE path=\"posts/文件名.md\"\\]\n" +
               "---\ntitle: \"文章标题\"\ndate: \"当前 UTC 时间\"\ntags: []\n---\n\n文章正文（完整 Markdown）\n" +
               "\\[/END_FILE\\]\n" +
               "当用户要求创建文件夹/分类目录时，使用：\\[CREATE_DIR path=\"posts/2025\"\\]\n" +
               "path 是相对于 content/ 目录的相对路径（使用正斜杠），文件/文件夹会自动写入并出现在文件树中。" +
               "请勿在对话中展示这些标记本身，直接输出文章内容即可。" +
               "\n回复应使用与用户提问相同的语言。简洁、实用，必要时给出可直接复制的代码或 YAML 片段。" +
               currentFileContext;
    }
}

// ===== 主题辅助类 =====
public static class ThemeHelper
{
    // 返回当前主题的背景/前景色，供对话框使用
    public static (Color bg, Color fg, Color panel, Color border, Color muted) GetColors()
    {
        var dark = MainWindow.IsDarkTheme;
        return dark
            ? (Color.FromRgb(0x1E, 0x1E, 0x1E), Color.FromRgb(0xD4, 0xD4, 0xD4),
               Color.FromRgb(0x25, 0x25, 0x26), Color.FromRgb(0x3F, 0x3F, 0x46),
               Color.FromRgb(0x9D, 0x9D, 0x9D))
            : (Color.FromRgb(0xFE, 0xFE, 0xFB), Color.FromRgb(0x24, 0x23, 0x1F),
               Color.FromRgb(0xF0, 0xEF, 0xEB), Color.FromRgb(0xE3, 0xE1, 0xDB),
               Color.FromRgb(0x78, 0x76, 0x70));
    }

    // 将主题应用到窗口
    public static void ApplyTheme(Window window)
    {
        var (bg, fg, panel, border, muted) = GetColors();
        window.Background = new SolidColorBrush(bg);
        window.Foreground = new SolidColorBrush(fg);
    }
}

// ===== Frontmatter 编辑对话框（跟随全局主题）=====
public class FrontmatterDialog : Window
{
    private readonly Dictionary<string, string> _fields;
    private readonly bool _isEnglish;
    private readonly List<(TextBox KeyBox, TextBox ValueBox)> _rows = new();
    private readonly SolidColorBrush _bgBrush;
    private readonly SolidColorBrush _fgBrush;
    private readonly SolidColorBrush _panelBrush;
    private readonly SolidColorBrush _borderBrush;
    private readonly SolidColorBrush _mutedBrush;

    public FrontmatterDialog(Dictionary<string, string> fields, bool isEnglish)
    {
        _fields = fields;
        _isEnglish = isEnglish;

        var (bg, fg, panel, border, muted) = ThemeHelper.GetColors();
        _bgBrush = new SolidColorBrush(bg);
        _fgBrush = new SolidColorBrush(fg);
        _panelBrush = new SolidColorBrush(panel);
        _borderBrush = new SolidColorBrush(border);
        _mutedBrush = new SolidColorBrush(muted);

        InitializeUi();
    }

    private void InitializeUi()
    {
        Title = _isEnglish ? "Edit Frontmatter" : "编辑 Frontmatter";
        Width = 520;
        MinWidth = 420;
        MaxWidth = 800;
        Height = 400;
        MinHeight = 300;
        MaxHeight = 650;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = _bgBrush;
        Foreground = _fgBrush;

        var rootPanel = new StackPanel { Margin = new Thickness(12) };
        Content = rootPanel;

        var hint = new TextBlock
        {
            Text = _isEnglish
                ? "Edit the metadata fields below. The frontmatter will be generated automatically."
                : "编辑下方的元数据字段，将自动生成 Frontmatter。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = _mutedBrush,
            Margin = new Thickness(0, 0, 0, 10)
        };
        rootPanel.Children.Add(hint);

        // 字段编辑区（可滚动）
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 260 };
        var fieldPanel = new StackPanel();
        scroll.Content = fieldPanel;
        rootPanel.Children.Add(scroll);

        // 在已有字段基础上添加默认字段
        var keys = new List<string> { "title", "date", "draft", "tags", "categories", "description" };
        foreach (var k in keys)
        {
            if (!_fields.ContainsKey(k)) _fields[k] = "";
        }

        // 保留原有字段顺序，先显示已有的
        foreach (var kv in _fields)
        {
            if (string.IsNullOrEmpty(kv.Value))
            {
                // 空值字段放后面
                if (keys.Contains(kv.Key)) continue;
            }
            AddRow(fieldPanel, kv.Key, kv.Value);
        }

        // 添加默认字段（空值）
        foreach (var k in keys)
        {
            if (_fields.ContainsKey(k))
            {
                AddRow(fieldPanel, k, _fields[k]);
            }
        }

        // 添加新字段按钮
        var addBtn = new Button
        {
            Content = _isEnglish ? "+ Add Field" : "+ 添加字段",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 3, 8, 3),
            Background = _panelBrush,
            BorderBrush = _borderBrush,
            Foreground = _fgBrush
        };
        addBtn.Click += (s, e) => AddRow(fieldPanel, "", "");
        rootPanel.Children.Add(addBtn);

        // 按钮区
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        rootPanel.Children.Add(btnPanel);

        var okBtn = new Button
        {
            Content = _isEnglish ? "OK" : "确定",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(10, 5, 10, 5),
            Background = _panelBrush,
            BorderBrush = _borderBrush,
            Foreground = _fgBrush
        };
        okBtn.Click += (s, e) => { DialogResult = true; Close(); };
        btnPanel.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = _isEnglish ? "Cancel" : "取消",
            Width = 80,
            Padding = new Thickness(10, 5, 10, 5),
            Background = _panelBrush,
            BorderBrush = _borderBrush,
            Foreground = _fgBrush
        };
        cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
        btnPanel.Children.Add(cancelBtn);
    }

    private void AddRow(StackPanel parent, string key, string value)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        var keyBox = new TextBox
        {
            Text = key,
            Width = 130,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Background = _panelBrush,
            BorderBrush = _borderBrush,
            Foreground = _fgBrush
        };
        DockPanel.SetDock(keyBox, Dock.Left);
        row.Children.Add(keyBox);

        var valueBox = new TextBox
        {
            Text = value,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = _panelBrush,
            BorderBrush = _borderBrush,
            Foreground = _fgBrush
        };
        row.Children.Add(valueBox);

        _rows.Add((keyBox, valueBox));
        parent.Children.Add(row);
    }

    public string BuildFrontmatter()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("---");
        foreach (var (keyBox, valueBox) in _rows)
        {
            var key = keyBox.Text.Trim();
            if (string.IsNullOrEmpty(key)) continue;

            var value = valueBox.Text.Trim();
            // 判断是否为列表（逗号分隔）
            if (value.StartsWith("[") && value.EndsWith("]"))
            {
                sb.AppendLine($"{key}: {value}");
            }
            else if (value.Contains(',') && key is "tags" or "categories")
            {
                var items = value.Split(',', StringSplitOptions.TrimEntries);
                sb.AppendLine($"{key}:");
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item))
                        sb.AppendLine($"  - {item.Trim('"', '\'')}");
                }
            }
            else
            {
                sb.AppendLine($"{key}: \"{value.Trim('"', '\'')}\"");
            }
        }
        sb.Append("---");
        return sb.ToString();
    }
}