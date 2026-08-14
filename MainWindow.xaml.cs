using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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

        // 主题提示
        ThemeBtn.ToolTip = _isDarkTheme
            ? (_isEnglish ? "Switch to Light" : "切换到亮色主题")
            : (_isEnglish ? "Switch to Dark" : "切换到暗色主题");
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
        FileTree.Items.Clear();
        if (_projectPath == null) return;

        // 隐藏技术性/生成目录
        var hiddenDirs = new HashSet<string> { "public", "resources", ".git", ".hugo_build.lock", "themes", "node_modules" };

        // 核心目录：仅 content、static、assets
        var coreDirs = new[] { "content", "static", "assets" };

        var root = new TreeViewItem
        {
            Header = Path.GetFileName(_projectPath),
            Tag = new FileNode { FullPath = _projectPath, IsDirectory = true }
        };
        root.IsExpanded = true;
        FileTree.Items.Add(root);

        // 只遍历这三个核心目录，且它们必须存在
        foreach (var dirName in coreDirs)
        {
            var dirPath = Path.Combine(_projectPath, dirName);
            if (!Directory.Exists(dirPath)) continue;

            var dirNode = new TreeViewItem
            {
                Header = dirName,
                Tag = new FileNode { FullPath = dirPath, IsDirectory = true }
            };
            root.Items.Add(dirNode);
            AddDirectory(dirNode, dirPath, hiddenDirs);
        }
    }

    private void AddDirectory(TreeViewItem parent, string dirPath, HashSet<string> hiddenDirs)
    {
        foreach (var dir in Directory.GetDirectories(dirPath).OrderBy(d => d))
        {
            var name = Path.GetFileName(dir);
            if (hiddenDirs.Contains(name)) continue;

            var node = new TreeViewItem
            {
                Header = name,
                Tag = new FileNode { FullPath = dir, IsDirectory = true }
            };
            parent.Items.Add(node);
            AddDirectory(node, dir, hiddenDirs);
        }

        foreach (var file in Directory.GetFiles(dirPath).OrderBy(f => f))
        {
            var ext = Path.GetExtension(file).ToLower();
            if (ext is ".md" or ".markdown" or ".yaml" or ".yml" or ".toml" or ".json")
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

    private void OpenFile(string path)
    {
        try
        {
            _currentFile = path;
            EditorBox.Text = File.ReadAllText(path);
            EditorTitle.Text = Path.GetFileName(path);
            _isDirty = false;
            FormatBar.IsEnabled = true;
            Log($"{(_isEnglish ? "Opened: " : "已打开: ")}{path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{( _isEnglish ? "Cannot open file: " : "无法打开文件: ")}{ex.Message}",
                _isEnglish ? "Error" : "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
    private const string HugoVersion = "0.145.0";

    // 获取 Hugo 可执行文件：优先应用同目录内嵌的 hugo，其次 PATH（实测存在）
    private string? FindHugoExecutable()
    {
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

    // 若 Hugo 不存在，自动下载 ~50MB 的 hugo.exe 到应用目录（无需用户安装/配置 PATH）
    private async Task<string?> EnsureHugoExecutableAsync()
    {
        var existing = FindHugoExecutable();
        if (existing != null) return existing;

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var target = Path.Combine(appDir, "hugo.exe");

        // 询问用户是否下载
        var confirm = MessageBox.Show(
            _isEnglish
                ? "Hugo was not found on this computer.\n\n" +
                  "Download Hugo (~50 MB) automatically to the app folder? " +
                  "No installation or PATH setup needed."
                : "未在本机找到 Hugo。\n\n" +
                  "是否自动下载 Hugo（约 50 MB）到程序目录？无需安装、无需配置 PATH。",
            _isEnglish ? "Hugo Not Found" : "未找到 Hugo",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            Log(_isEnglish
                ? "Hugo download cancelled by user."
                : "用户取消了 Hugo 自动下载。");
            return null;
        }

        var url =
            $"https://github.com/gohugoio/hugo/releases/download/v{HugoVersion}/hugo_{HugoVersion}_windows-amd64.zip";
        var tempZip = Path.Combine(appDir, "hugo_download.zip");

        try
        {
            Log(_isEnglish
                ? $"Downloading Hugo v{HugoVersion}..."
                : $"正在下载 Hugo v{HugoVersion}...");
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                using var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                await using (var fs = File.Create(tempZip))
                {
                    await response.Content.CopyToAsync(fs);
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
        Height = 400;
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