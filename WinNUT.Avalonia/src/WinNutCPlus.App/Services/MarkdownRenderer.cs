using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace WinNutCPlus.App.Services;

/// <summary>
/// Renders a (deliberately small) subset of Markdown — headings, bold/italic, inline code,
/// links, and bullet/numbered lists — into Avalonia controls. Built for GitHub release notes
/// (see UpdateAvailableWindow's ChangeLog), not general-purpose Markdown, so anything fancier
/// (tables, images, nested blockquotes, fenced code blocks) just falls through as plain text
/// rather than pulling in a full Markdown package for a changelog box.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex InlineTokenPattern = new(
        @"\*\*(?<bold>[^*]+?)\*\*" +
        @"|__(?<bold2>[^_]+?)__" +
        @"|`(?<code>[^`]+?)`" +
        @"|\[(?<linktext>[^\]]+)\]\((?<linkurl>[^)]+)\)" +
        @"|\*(?<italic>[^*]+?)\*" +
        @"|_(?<italic2>[^_]+?)_",
        RegexOptions.Compiled);

    private static readonly Regex HeadingPattern = new(@"^(?<hashes>#{1,6})\s+(?<text>.+)$", RegexOptions.Compiled);
    private static readonly Regex BulletPattern = new(@"^(?<indent>\s*)[-*+]\s+(?<text>.+)$", RegexOptions.Compiled);
    private static readonly Regex NumberedPattern = new(@"^(?<indent>\s*)\d+[.)]\s+(?<text>.+)$", RegexOptions.Compiled);

    public static Control Render(string? markdown, IBrush textBrush, IBrush secondaryBrush, IBrush accentBrush)
    {
        var panel = new StackPanel { Spacing = 6 };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return panel;
        }

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (HeadingPattern.Match(line) is { Success: true } heading)
            {
                var level = heading.Groups["hashes"].Value.Length;
                var tb = new TextBlock
                {
                    FontSize = level switch { 1 => 17, 2 => 15, _ => 13.5 },
                    FontWeight = FontWeight.SemiBold,
                    Foreground = textBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, level <= 2 ? 6 : 2, 0, 0),
                };
                AppendInlines(tb, heading.Groups["text"].Value, textBrush, secondaryBrush, accentBrush);
                panel.Children.Add(tb);
                continue;
            }

            if (BulletPattern.Match(line) is { Success: true } bullet)
            {
                panel.Children.Add(BuildListItem("•", bullet.Groups["text"].Value, bullet.Groups["indent"].Value.Length,
                    textBrush, secondaryBrush, accentBrush));
                continue;
            }

            if (NumberedPattern.Match(line) is { Success: true } numbered)
            {
                panel.Children.Add(BuildListItem("-", numbered.Groups["text"].Value, numbered.Groups["indent"].Value.Length,
                    textBrush, secondaryBrush, accentBrush));
                continue;
            }

            var paragraph = new TextBlock
            {
                FontSize = 13,
                Foreground = textBrush,
                TextWrapping = TextWrapping.Wrap,
            };
            AppendInlines(paragraph, line, textBrush, secondaryBrush, accentBrush);
            panel.Children.Add(paragraph);
        }

        return panel;
    }

    private static Control BuildListItem(string marker, string text, int indentChars, IBrush textBrush,
        IBrush secondaryBrush, IBrush accentBrush)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Avalonia.Thickness(10 + indentChars * 7, 0, 0, 0),
        };

        row.Children.Add(new TextBlock { Text = marker, FontSize = 13, Foreground = secondaryBrush });

        var tb = new TextBlock { FontSize = 13, Foreground = textBrush, TextWrapping = TextWrapping.Wrap };
        AppendInlines(tb, text, textBrush, secondaryBrush, accentBrush);
        row.Children.Add(tb);

        return row;
    }

    private static void AppendInlines(TextBlock target, string text, IBrush textBrush, IBrush secondaryBrush, IBrush accentBrush)
    {
        var lastIndex = 0;
        foreach (Match match in InlineTokenPattern.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                target.Inlines!.Add(new Run(text[lastIndex..match.Index]) { Foreground = textBrush });
            }

            if (match.Groups["bold"].Success || match.Groups["bold2"].Success)
            {
                var content = match.Groups["bold"].Success ? match.Groups["bold"].Value : match.Groups["bold2"].Value;
                target.Inlines!.Add(new Run(content) { Foreground = textBrush, FontWeight = FontWeight.Bold });
            }
            else if (match.Groups["italic"].Success || match.Groups["italic2"].Success)
            {
                var content = match.Groups["italic"].Success ? match.Groups["italic"].Value : match.Groups["italic2"].Value;
                target.Inlines!.Add(new Run(content) { Foreground = textBrush, FontStyle = FontStyle.Italic });
            }
            else if (match.Groups["code"].Success)
            {
                target.Inlines!.Add(new Run(match.Groups["code"].Value)
                {
                    Foreground = textBrush,
                    FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
                    Background = secondaryBrush,
                });
            }
            else if (match.Groups["linktext"].Success)
            {
                // Styled but not click-to-open: a TextBlock only exposes pointer events at the
                // whole-block level, not per-Inline, so wiring a click here would open whichever
                // link's URL was captured last for any click anywhere in the paragraph —
                // including on unrelated text. Not worth that bug for changelog link references.
                target.Inlines!.Add(new Run(match.Groups["linktext"].Value)
                {
                    Foreground = accentBrush,
                    TextDecorations = Avalonia.Media.TextDecorations.Underline,
                });
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            target.Inlines!.Add(new Run(text[lastIndex..]) { Foreground = textBrush });
        }
    }
}
