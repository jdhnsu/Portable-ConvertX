using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace ConvertXPortable.Controls;

public sealed partial class MarkdownViewer : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownViewer),
        new PropertyMetadata("", (_, _) => { }));

    public static readonly DependencyProperty BubbleForegroundProperty = DependencyProperty.Register(
        nameof(BubbleForeground),
        typeof(MediaBrush),
        typeof(MarkdownViewer),
        new PropertyMetadata(MediaBrushes.Black, (_, _) => { }));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MediaBrush BubbleForeground
    {
        get => (MediaBrush)GetValue(BubbleForegroundProperty);
        set => SetValue(BubbleForegroundProperty, value);
    }

    public MarkdownViewer()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        IsToolBarVisible = false;
        Background = MediaBrushes.Transparent;
        Loaded += (_, _) => Render();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == MarkdownProperty || e.Property == BubbleForegroundProperty)
        {
            Render();
        }
    }

    private void Render()
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontSize = FontSize > 0 ? FontSize : 13,
            FontFamily = FontFamily,
            Foreground = BubbleForeground,
            Background = MediaBrushes.Transparent
        };

        var text = Markdown ?? "";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inCode = false;
        var codeLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    AddCodeBlock(document, string.Join(Environment.NewLine, codeLines));
                    codeLines.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }

                continue;
            }

            if (inCode)
            {
                codeLines.Add(rawLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 6) });
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                AddParagraph(document, line[4..], FontWeights.SemiBold, 14);
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AddParagraph(document, line[3..], FontWeights.SemiBold, 15);
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                AddParagraph(document, line[2..], FontWeights.Bold, 16);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                var paragraph = CreateParagraph();
                paragraph.Inlines.Add(new Run("• ") { FontWeight = FontWeights.SemiBold });
                AddInlineRuns(paragraph, line[2..]);
                document.Blocks.Add(paragraph);
            }
            else
            {
                var paragraph = CreateParagraph();
                AddInlineRuns(paragraph, line);
                document.Blocks.Add(paragraph);
            }
        }

        if (codeLines.Count > 0)
        {
            AddCodeBlock(document, string.Join(Environment.NewLine, codeLines));
        }

        Document = document;
    }

    private void AddParagraph(FlowDocument document, string text, FontWeight weight, double size)
    {
        var paragraph = CreateParagraph();
        paragraph.FontWeight = weight;
        paragraph.FontSize = size;
        AddInlineRuns(paragraph, text);
        document.Blocks.Add(paragraph);
    }

    private Paragraph CreateParagraph()
    {
        return new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 7),
            LineHeight = 18,
            Foreground = BubbleForeground
        };
    }

    private void AddInlineRuns(Paragraph paragraph, string text)
    {
        var pattern = new Regex(@"(\*\*[^*]+\*\*|`[^`]+`)");
        var last = 0;
        foreach (Match match in pattern.Matches(text))
        {
            if (match.Index > last)
            {
                paragraph.Inlines.Add(new Run(text[last..match.Index]));
            }

            var value = match.Value;
            if (value.StartsWith("**", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Run(value[2..^2]) { FontWeight = FontWeights.Bold });
            }
            else if (value.StartsWith('`'))
            {
                paragraph.Inlines.Add(new Run(value[1..^1])
                {
                    FontFamily = new MediaFontFamily("Consolas"),
                    Background = new SolidColorBrush(MediaColor.FromArgb(28, 0, 0, 0)),
                    FontSize = Math.Max(11, paragraph.FontSize - 1)
                });
            }

            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            paragraph.Inlines.Add(new Run(text[last..]));
        }
    }

    private static void AddCodeBlock(FlowDocument document, string code)
    {
        var textBlock = new TextBlock
        {
            Text = code,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new MediaFontFamily("Consolas, Cascadia Mono"),
            FontSize = 12,
            Foreground = MediaBrushes.White,
            Padding = new Thickness(10, 8, 10, 8)
        };

        var border = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(8, 18, 38)),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 4, 0, 10),
            Child = textBlock
        };

        document.Blocks.Add(new BlockUIContainer(border) { Margin = new Thickness(0) });
    }
}
