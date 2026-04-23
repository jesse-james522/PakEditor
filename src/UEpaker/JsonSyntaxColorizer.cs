using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace UEpaker;

public static class JsonSyntaxColorizer
{
    private const int MaxChars = 400_000;

    // Token order matters: keys before strings so the key lookahead takes priority.
    private static readonly Regex TokenRegex = new(
        @"""(?:[^""\\]|\\.)*""\s*(?=:)" +   // key string (followed by :)
        @"|""(?:[^""\\]|\\.)*""" +           // string value
        @"|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?" + // number
        @"|\b(?:true|false|null)\b" +        // keyword
        @"|[{}\[\]:,]" +                     // punctuation
        @"|\s+",                             // whitespace (preserve indentation)
        RegexOptions.Compiled);

    // Light-theme colour palette
    private static readonly SolidColorBrush KeyBrush      = Brush("#001080"); // dark blue
    private static readonly SolidColorBrush StringBrush   = Brush("#A31515"); // dark red
    private static readonly SolidColorBrush NumberBrush   = Brush("#098658"); // dark green
    private static readonly SolidColorBrush KeywordBrush  = Brush("#0000FF"); // blue
    private static readonly SolidColorBrush PunctBrush    = Brush("#808080"); // gray
    private static readonly SolidColorBrush DefaultBrush  = Brush("#1E1E1E"); // near-black

    public static void Apply(RichTextBox rtb, string json)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        var para = new Paragraph { Margin = new Thickness(0) };

        var text = json.Length > MaxChars
            ? json[..MaxChars] + $"\n\n... (truncated — {json.Length / 1024} KB total)"
            : json;

        foreach (Match m in TokenRegex.Matches(text))
        {
            var value = m.Value;
            SolidColorBrush color;

            if (m.Value.TrimEnd().EndsWith(':'))
                // key: ends with optional whitespace then : lookahead already matched
                // but the match itself includes the trailing whitespace before :
                color = KeyBrush;
            else if (value.StartsWith('"'))
                color = StringBrush;
            else if (char.IsDigit(value[0]) || (value[0] == '-' && value.Length > 1))
                color = NumberBrush;
            else if (value is "true" or "false" or "null")
                color = KeywordBrush;
            else if (value is "{" or "}" or "[" or "]" or ":" or ",")
                color = PunctBrush;
            else
                color = DefaultBrush;

            para.Inlines.Add(new Run(value) { Foreground = color });
        }

        doc.Blocks.Add(para);
        rtb.Document = doc;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
