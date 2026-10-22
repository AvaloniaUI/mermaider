using System.Text.Json;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// Strips YAML frontmatter and <c>%%{init:...}%%</c> directives from raw Mermaid input,
/// returning the cleaned text and any extracted <see cref="DiagramMetadata"/>, and splits
/// the cleaned text into the lines the diagram parsers consume.
/// </summary>
internal static partial class DiagramPreprocessor
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^%%\{init:\s*(\{.*\})\s*\}%%\s*$", RegexOptions.Singleline, TimeoutMs)]
	private static partial Regex InitDirectiveRegex();

	/// <summary>
	/// Pre-process raw Mermaid text: strip frontmatter and init directives,
	/// extract metadata, and return cleaned text ready for diagram parsing.
	/// </summary>
	internal static (string CleanedText, DiagramMetadata Metadata) Process(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return (text, DiagramMetadata.Empty);

		string? title = null;
		string? theme = null;
		IReadOnlyDictionary<string, string>? themeVariables = null;

		var cleaned = text;

		// 1. Strip YAML frontmatter (must be at the very start)
		var (afterFrontmatter, frontmatterTitle, config) = StripFrontmatter(cleaned);
		if (frontmatterTitle is not null)
			title = frontmatterTitle;
		cleaned = afterFrontmatter;

		// 2. Strip %%{init:...}%% directives (can appear on any line)
		var (afterInit, initTheme, initThemeVars, initTitle) = StripInitDirective(cleaned, ref config);
		cleaned = afterInit;

		// init directive values override frontmatter
		if (initTheme is not null)
			theme = initTheme;
		if (initThemeVars is not null)
			themeVariables = initThemeVars;
		if (initTitle is not null)
			title = initTitle;

		var metadata = title is null && theme is null && themeVariables is null && config is null
			? DiagramMetadata.Empty
			: new DiagramMetadata { Title = title, Theme = theme, ThemeVariables = themeVariables, Config = config };

		return (cleaned, metadata);
	}

	/// <summary>
	/// Split cleaned text into trimmed lines, dropping blank lines and <c>%%</c> comments.
	/// </summary>
	internal static string[] PreprocessLines(string text)
	{
		var rawLines = text.Split('\n');
		var count = 0;
		for (var i = 0; i < rawLines.Length; i++)
		{
			var trimmed = rawLines[i].Trim();
			if (trimmed.Length > 0 && !trimmed.StartsWith("%%", StringComparison.Ordinal))
			{
				rawLines[count] = trimmed;
				count++;
			}
		}
		return rawLines.AsSpan(0, count).ToArray();
	}

	/// <summary>
	/// Split cleaned text into lines that keep their leading indentation, for the diagram types whose
	/// structure is expressed by indentation. Blank lines and <c>%%</c> comments are dropped, and so are
	/// <c>accTitle</c>/<c>accDescr</c> lines when <paramref name="accessibility"/> has already captured them.
	/// </summary>
	internal static string[] PreprocessLinesPreserveIndent(string text, AccessibilityInfo? accessibility = null)
	{
		var rawLines = text.Split('\n');
		var result = new List<string>(rawLines.Length);
		for (var i = 0; i < rawLines.Length; i++)
		{
			var trimmed = rawLines[i].TrimEnd();
			var stripped = trimmed.Trim();
			if (stripped.Length == 0 || stripped.StartsWith("%%", StringComparison.Ordinal))
				continue;
			if (accessibility?.HasContent == true && IsAccessibilityLine(stripped, ref i, rawLines))
				continue;
			result.Add(trimmed);
		}
		return result.ToArray();
	}

	private static bool IsAccessibilityLine(string stripped, ref int i, string[] rawLines)
	{
		if (stripped.StartsWith("accTitle", StringComparison.Ordinal) && stripped.Contains(':'))
			return true;
		if (stripped.StartsWith("accDescr", StringComparison.Ordinal) && stripped.Contains(':'))
			return true;
		if (stripped.StartsWith("accDescr", StringComparison.Ordinal) && stripped.Contains('{'))
		{
			i++;
			while (i < rawLines.Length)
			{
				var inner = rawLines[i].Trim();
				if (inner == "}" || inner.StartsWith('}'))
					break;
				i++;
			}
			return true;
		}
		return false;
	}

	private static (string CleanedText, string? Title, Dictionary<string, string>? Config) StripFrontmatter(string text)
	{
		var span = text.AsSpan().TrimStart();
		if (!span.StartsWith("---"))
			return (text, null, null);

		// Find the opening "---" line end
		var firstNewline = span.IndexOf('\n');
		if (firstNewline < 0)
			return (text, null, null);

		// Check that the first line is exactly "---" (possibly with trailing whitespace)
		var firstLine = span[..firstNewline].TrimEnd();
		if (firstLine is not "---")
			return (text, null, null);

		// Find closing "---"
		var rest = span[(firstNewline + 1)..];
		var closingIndex = -1;
		var searchStart = 0;
		while (searchStart < rest.Length)
		{
			var lineStart = searchStart;
			var lineEnd = rest[searchStart..].IndexOf('\n');
			var line = lineEnd >= 0
				? rest[lineStart..(lineStart + lineEnd)].Trim()
				: rest[lineStart..].Trim();

			if (line is "---")
			{
				closingIndex = lineEnd >= 0 ? lineStart + lineEnd : rest.Length;
				break;
			}

			if (lineEnd < 0)
				break;
			searchStart = lineStart + lineEnd + 1;
		}

		if (closingIndex < 0)
			return (text, null, null);

		// Extract YAML content between the two --- markers
		var yamlContent = rest[..closingIndex].ToString();
		var afterBlock = closingIndex < rest.Length
			? rest[(closingIndex + 1)..].ToString()
			: string.Empty;

		// Read the title and the config section from simple YAML (no full YAML parser dependency).
		// Every other key is ignored and removed with the frontmatter block. Config is returned as
		// data only, so no setting reaches a parser or rendering sink unless a consumer reads it.
		string? title = null;
		foreach (var yamlLine in yamlContent.Split('\n'))
		{
			var trimmed = yamlLine.AsSpan().Trim();
			if (yamlLine.Length > 0 && !char.IsWhiteSpace(yamlLine[0]) && trimmed.StartsWith("title:"))
			{
				var value = trimmed["title:".Length..].Trim();
				// Strip surrounding quotes if present
				if (value.Length >= 2 &&
					((value[0] == '"' && value[^1] == '"') ||
					 (value[0] == '\'' && value[^1] == '\'')))
				{
					value = value[1..^1];
				}
				title = value.ToString();
			}
		}

		return (afterBlock, title, ReadConfig(yamlContent));
	}

	// Flattens the top-level `config:` map into dotted keys. A YAML subset: nested maps by indentation and scalar
	// values, quotes stripped; list items, flow collections and multi-line scalars are skipped.
	private static Dictionary<string, string>? ReadConfig(string yaml)
	{
		Dictionary<string, string>? config = null;
		var path = new List<(int Indent, string Key)>();
		var inConfig = false;
		foreach (var raw in yaml.Split('\n'))
		{
			var line = raw.TrimEnd();
			var trimmed = line.TrimStart();
			if (trimmed.Length == 0 || trimmed[0] is '#' or '-')
				continue;

			var colon = trimmed.IndexOf(':');
			if (colon <= 0)
				continue;

			var indent = line.Length - trimmed.Length;
			var key = trimmed[..colon].Trim();
			var value = trimmed[(colon + 1)..].Trim();
			if (indent == 0)
			{
				inConfig = key == "config" && value.Length == 0;
				path.Clear();
				continue;
			}

			if (!inConfig)
				continue;

			while (path.Count > 0 && path[^1].Indent >= indent)
				path.RemoveAt(path.Count - 1);

			if (value.Length == 0)
			{
				path.Add((indent, key));
				continue;
			}

			if (value[0] is '[' or '{' or '|' or '>')
				continue;

			var fullKey = path.Count == 0 ? key : string.Join('.', path.Select(p => p.Key)) + "." + key;
			(config ??= new Dictionary<string, string>(StringComparer.Ordinal))[fullKey] = Unquote(value);
		}

		return config;
	}

	private static string Unquote(string value)
		=> value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
			? value[1..^1]
			: value;

	private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> config)
	{
		foreach (var property in element.EnumerateObject())
		{
			var key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
			switch (property.Value.ValueKind)
			{
				case JsonValueKind.Object:
					FlattenJson(property.Value, key, config);
					break;
				case JsonValueKind.String:
					config[key] = property.Value.GetString()!;
					break;
				case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
					config[key] = property.Value.GetRawText();
					break;
			}
		}
	}

	private static (string CleanedText, string? Theme, IReadOnlyDictionary<string, string>? ThemeVariables, string? Title) StripInitDirective(string text, ref Dictionary<string, string>? config)
	{
		var lines = text.Split('\n');
		var resultLines = new List<string>(lines.Length);
		string? theme = null;
		Dictionary<string, string>? themeVariables = null;
		string? title = null;

		foreach (var line in lines)
		{
			var trimmed = line.Trim();
			var match = InitDirectiveRegex().Match(trimmed);
			if (match.Success)
			{
				var json = match.Groups[1].Value;
				try
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					if (root.ValueKind == JsonValueKind.Object)
						FlattenJson(root, string.Empty, config ??= new Dictionary<string, string>(StringComparer.Ordinal));

					if (root.TryGetProperty("theme", out var themeElement) &&
						themeElement.ValueKind == JsonValueKind.String)
					{
						theme = themeElement.GetString();
					}

					if (root.TryGetProperty("themeVariables", out var varsElement) &&
						varsElement.ValueKind == JsonValueKind.Object)
					{
						themeVariables ??= [];
						foreach (var prop in varsElement.EnumerateObject())
						{
							if (prop.Value.ValueKind == JsonValueKind.String)
								themeVariables[prop.Name] = prop.Value.GetString()!;
						}
					}

					// Mermaid also supports title in init
					if (root.TryGetProperty("title", out var titleElement) &&
						titleElement.ValueKind == JsonValueKind.String)
					{
						title = titleElement.GetString();
					}
				}
				catch (JsonException)
				{
					// Malformed JSON in init directive — skip extraction, still strip the line
				}

				continue; // Strip the init directive line from output
			}

			resultLines.Add(line);
		}

		var cleaned = string.Join('\n', resultLines);
		return (cleaned, theme, themeVariables, title);
	}
}
