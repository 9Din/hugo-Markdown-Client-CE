using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Huge;

/// <summary>
/// 轻量 Markdown 渲染器：把 AI 回复的 Markdown 文本转换为 FlowDocument，
/// 支持标题(#)、粗体(**)、斜体(*)、行内代码(`)、代码块(```)、列表(-/1.)、
/// 引用(>)、分隔线(---)、表格(| 列 |)。不做完整规范，够聊天窗口展示用。
/// </summary>
public static class MarkdownRenderer
{
    // 主题相关颜色（随 MainWindow.IsDarkTheme 动态取）
    private static SolidColorBrush TextBrush =>
        new(MainWindow.IsDarkTheme
            ? Color.FromRgb(0xD4, 0xD4, 0xD4)
            : Color.FromRgb(0x24, 0x23, 0x1F));

    private static SolidColorBrush MutedBrush =>
        new(MainWindow.IsDarkTheme
            ? Color.FromRgb(0x9D, 0x9D, 0x9D)
            : Color.FromRgb(0x78, 0x76, 0x70));

    private static SolidColorBrush CodeBgBrush =>
        new(MainWindow.IsDarkTheme
            ? Color.FromRgb(0x2D, 0x2D, 0x30)
            : Color.FromRgb(0xF2, 0xF0, 0xEC));

    public static FlowDocument Render(string markdown, double pageWidth)
    {
        var doc = new FlowDocument
        {
            PageWidth = Math.Max(120, pageWidth - 20),
            FontSize = 12.5,
            // 复合字体：普通文字用 Segoe UI，emoji 回退到 Segoe UI Emoji，中文回退到微软雅黑
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Segoe UI Emoji, Microsoft YaHei UI"),
            Foreground = TextBrush,
            LineHeight = 18,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left
        };

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd();

            // 空行
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // 围栏代码块 ```lang
            if (line.TrimStart().StartsWith("```"))
            {
                var sb = new System.Text.StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                {
                    sb.AppendLine(lines[i]);
                    i++;
                }
                i++; // 跳过结尾 ```
                doc.Blocks.Add(BuildCodeBlock(sb.ToString()));
                continue;
            }

            // 分隔线 --- / *** / ___
            if (line is "---" or "***" or "___" || line.Trim().StartsWith("---"))
            {
                doc.Blocks.Add(new Paragraph(new Run(new string('─', 24)))
                {
                    Foreground = MutedBrush,
                    FontSize = 10,
                    Margin = new Thickness(0, 4, 0, 4)
                });
                i++;
                continue;
            }

            // 引用 >
            if (line.TrimStart().StartsWith(">"))
            {
                var quote = line.TrimStart()[1..].TrimStart();
                var quoteP = new Paragraph
                {
                    Foreground = MutedBrush,
                    Margin = new Thickness(8, 2, 0, 2),
                    FontStyle = FontStyles.Italic
                };
                quoteP.Inlines.AddRange(BuildInlines(quote));
                doc.Blocks.Add(quoteP);
                i++;
                continue;
            }

            // 标题 #######
            var trimmed = line.TrimStart();
            var hashCount = 0;
            while (hashCount < trimmed.Length && trimmed[hashCount] == '#') hashCount++;
            if (hashCount is >= 1 and <= 6 && hashCount < trimmed.Length && trimmed[hashCount] == ' ')
            {
                var headingText = trimmed[(hashCount + 1)..];
                var fontSize = hashCount switch
                {
                    1 => 17.0,
                    2 => 15.5,
                    3 => 14.0,
                    _ => 13.0
                };
                var headingP = new Paragraph
                {
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 6, 0, 3),
                    Foreground = TextBrush
                };
                headingP.Inlines.AddRange(BuildInlines(headingText));
                doc.Blocks.Add(headingP);
                i++;
                continue;
            }

            // 表格 | a | b |
            if (line.StartsWith("|") && i + 1 < lines.Length &&
                lines[i + 1].Trim().StartsWith("|") &&
                lines[i + 1].Contains("---"))
            {
                var header = ParseTableRow(line);
                var rows = new List<string[]>();
                i += 2; // 跳过表头与分隔行
                while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                {
                    rows.Add(ParseTableRow(lines[i]));
                    i++;
                }
                doc.Blocks.Add(BuildTable(header, rows));
                continue;
            }

            // 无序列表 - / * / + 
            if (line.TrimStart() is { } lt && (lt.StartsWith("- ") || lt.StartsWith("* ") || lt.StartsWith("+ ")))
            {
                var content = lt[2..];
                var p = new Paragraph
                {
                    Margin = new Thickness(12, 1, 0, 1),
                    Foreground = TextBrush
                };
                p.Inlines.Add(new Run("•  ") { Foreground = MutedBrush, FontSize = 11 });
                p.Inlines.AddRange(BuildInlines(content));
                doc.Blocks.Add(p);
                i++;
                continue;
            }

            // 有序列表 1. 
            if (line.TrimStart() is { } lt2 && char.IsDigit(lt2[0]))
            {
                var dotIdx = lt2.IndexOf(". ");
                if (dotIdx > 0 && char.IsDigit(lt2[dotIdx - 1]))
                {
                    var num = lt2[..dotIdx];
                    var content = lt2[(dotIdx + 2)..];
                    var p = new Paragraph
                    {
                        Margin = new Thickness(12, 1, 0, 1),
                        Foreground = TextBrush
                    };
                    p.Inlines.Add(new Run($"{num}.  ") { Foreground = MutedBrush });
                    p.Inlines.AddRange(BuildInlines(content));
                    doc.Blocks.Add(p);
                    i++;
                    continue;
                }
            }

            // 常规段落
            var para = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 4),
                Foreground = TextBrush
            };
            para.Inlines.AddRange(BuildInlines(line));
            doc.Blocks.Add(para);
            i++;
        }

        return doc;
    }

    // 代码块：等宽字体 + 背景色块
    private static Block BuildCodeBlock(string code)
    {
        var border = new System.Windows.Controls.Border
        {
            Background = CodeBgBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 4, 0, 6)
        };
        border.Child = new System.Windows.Controls.TextBox
        {
            Text = code.TrimEnd('\n'),
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11.5,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            MaxHeight = 320
        };

        return new BlockUIContainer(border) { Margin = new Thickness(0) };
    }

    // 表格：用 Grid 网格实现
    private static Block BuildTable(string[] header, List<string[]> rows)
    {
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 4, 0, 6) };
        for (var c = 0; c < header.Length; c++)
        {
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        }

        var rowIndex = 0;
        void FillRow(string[] cells, bool isHeader)
        {
            // 每行只添加一个 RowDefinition，所有单元格共用同一行号
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            for (var c = 0; c < header.Length; c++)
            {
                var cellText = c < cells.Length ? cells[c] : "";
                var tb = new System.Windows.Controls.TextBlock
                {
                    Text = cellText,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11.5,
                    Foreground = isHeader ? TextBrush : TextBrush,
                    FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                    Padding = new Thickness(6, 3, 6, 3),
                    Background = isHeader
                        ? new SolidColorBrush(MainWindow.IsDarkTheme
                            ? Color.FromRgb(0x35, 0x35, 0x38)
                            : Color.FromRgb(0xE9, 0xE7, 0xE2))
                        : Brushes.Transparent
                };
                System.Windows.Controls.Grid.SetColumn(tb, c);
                System.Windows.Controls.Grid.SetRow(tb, rowIndex);
                grid.Children.Add(tb);
            }
            rowIndex++;
        }

        FillRow(header, true);
        foreach (var row in rows) FillRow(row, false);

        return new BlockUIContainer(grid) { Margin = new Thickness(0) };
    }

    private static string[] ParseTableRow(string line)
    {
        var inner = line.Trim().TrimStart('|').TrimEnd('|');
        return inner.Split('|').Select(c => c.Trim()).ToArray();
    }

    // 行内解析：**粗体**、*斜体*、`代码`、[文本](链接)
    private static List<Inline> BuildInlines(string text)
    {
        var inlines = new List<Inline>();
        var i = 0;

        while (i < text.Length)
        {
            // 行内代码 `code`
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    inlines.Add(new Run(text[(i + 1)..end])
                    {
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 11.5,
                        Background = CodeBgBrush
                    });
                    i = end + 1;
                    continue;
                }
            }

            // **bold**
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 1)
                {
                    inlines.Add(new Run(text[(i + 2)..end]) { FontWeight = FontWeights.Bold });
                    i = end + 2;
                    continue;
                }
            }

            // *italic*
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] != '*' && text[i + 1] != ' ')
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i + 1)
                {
                    inlines.Add(new Run(text[(i + 1)..end]) { FontStyle = FontStyles.Italic });
                    i = end + 1;
                    continue;
                }
            }

            // [text](url)
            if (text[i] == '[')
            {
                var close = text.IndexOf(']', i + 1);
                if (close > i && close + 1 < text.Length && text[close + 1] == '(')
                {
                    var parenClose = text.IndexOf(')', close + 2);
                    if (parenClose > close + 1)
                    {
                        var label = text[(i + 1)..close];
                        var url = text[(close + 2)..parenClose];
                        var hyperlink = new Hyperlink(new Run(label))
                        {
                            NavigateUri = new Uri(url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url)
                        };
                        hyperlink.RequestNavigate += (s, e) =>
                        {
                            try { Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true }); }
                            catch { }
                        };
                        inlines.Add(hyperlink);
                        i = parenClose + 1;
                        continue;
                    }
                }
            }

            // 代理对（emoji 由两个 UTF-16 字符组成）：合并为一个 Run 并显式使用 Emoji 字体，
            // 否则被拆成两个字符导致无法合成字形、显示为方框
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                inlines.Add(new Run(text.Substring(i, 2))
                {
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji")
                });
                i += 2;
                continue;
            }

            inlines.Add(new Run(text[i].ToString()));
            i++;
        }

        return inlines;
    }
}