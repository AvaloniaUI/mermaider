using System.Text.RegularExpressions;

namespace Mermaider.Parsing;

/// <summary>
/// Normalizes label text as it is read from diagram source, so every parser hands the same label form to the model.
/// </summary>
internal static partial class LabelNormalizer
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex BrTagPattern();

	[GeneratedRegex(@"\\n", RegexOptions.None, TimeoutMs)]
	private static partial Regex EscapedNewlinePattern();

	[GeneratedRegex(@"</?(?:sub|sup|small|mark)\s*>", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex UnsupportedTagPattern();

	[GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.None, TimeoutMs)]
	private static partial Regex MarkdownBoldPattern();

	[GeneratedRegex(@"(?<!\*)\*([^\s*](?:[^*]*[^\s*])?)\*(?!\*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex MarkdownItalicPattern();

	[GeneratedRegex(@"~~(.+?)~~", RegexOptions.None, TimeoutMs)]
	private static partial Regex MarkdownStrikethroughPattern();

	/// <summary>
	/// Normalize label text: strip quotes, convert &lt;br&gt; tags and \n to newlines,
	/// strip unsupported tags, convert markdown formatting to HTML tags.
	/// </summary>
	internal static string NormalizeBrTags(string label)
	{
		var unquoted = label is ['"', .., '"'] ? label[1..^1] : label;
		var result = BrTagPattern().Replace(unquoted, "\n");
		result = EscapedNewlinePattern().Replace(result, "\n");
		result = UnsupportedTagPattern().Replace(result, "");
		result = MarkdownBoldPattern().Replace(result, "<b>$1</b>");
		result = MarkdownItalicPattern().Replace(result, "<i>$1</i>");
		result = MarkdownStrikethroughPattern().Replace(result, "<s>$1</s>");
		return result;
	}
}
