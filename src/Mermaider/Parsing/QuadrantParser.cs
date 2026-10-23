using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class QuadrantParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^x-axis\s+(.+?)(?:\s*-->\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex XAxisPattern();

	[GeneratedRegex(@"^y-axis\s+(.+?)(?:\s*-->\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex YAxisPattern();

	[GeneratedRegex(@"^quadrant-([1-4])\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex QuadrantLabelPattern();

	[GeneratedRegex(@"^(.+?)(?::::([\w-]+))?:\s*\[\s*(-?[0-9]*\.?[0-9]+)\s*,\s*(-?[0-9]*\.?[0-9]+)\s*\]\s*(.*)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex PointPattern();

	[GeneratedRegex(@"^classDef\s+([\w-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefPattern();

	internal static QuadrantChart Parse(string[] lines)
	{
		try
		{
			return ParseCore(lines);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException(
				$"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.",
				ex);
		}
	}

	private static QuadrantChart ParseCore(string[] lines)
	{
		var (_, title) = DiagramHeaderOptions.Parse(lines[0], "quadrantChart");
		string? xAxisLeft = null, xAxisRight = null;
		string? yAxisBottom = null, yAxisTop = null;
		string? q1 = null, q2 = null, q3 = null, q4 = null;
		var points = new List<(QuadrantPoint Point, string? ClassName)>();
		var classes = new Dictionary<string, QuadrantPointStyle>(StringComparer.Ordinal);

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = Unquote(titleMatch.Groups[1].Value);
				continue;
			}

			var xMatch = XAxisPattern().Match(line);
			if (xMatch.Success)
			{
				xAxisLeft = Unquote(xMatch.Groups[1].Value);
				xAxisRight = xMatch.Groups[2].Success ? Unquote(xMatch.Groups[2].Value) : null;
				continue;
			}

			var yMatch = YAxisPattern().Match(line);
			if (yMatch.Success)
			{
				yAxisBottom = Unquote(yMatch.Groups[1].Value);
				yAxisTop = yMatch.Groups[2].Success ? Unquote(yMatch.Groups[2].Value) : null;
				continue;
			}

			var qMatch = QuadrantLabelPattern().Match(line);
			if (qMatch.Success)
			{
				var label = Unquote(qMatch.Groups[2].Value);
				switch (qMatch.Groups[1].Value)
				{
					case "1":
						q1 = label;
						break;
					case "2":
						q2 = label;
						break;
					case "3":
						q3 = label;
						break;
					case "4":
						q4 = label;
						break;
				}
				continue;
			}

			var classMatch = ClassDefPattern().Match(line);
			if (classMatch.Success)
			{
				classes[classMatch.Groups[1].Value] = ParseStyle(classMatch.Groups[2].Value);
				continue;
			}

			var pMatch = PointPattern().Match(line);
			if (pMatch.Success)
			{
				var label = Unquote(pMatch.Groups[1].Value);
				var x = double.Parse(pMatch.Groups[3].Value, CultureInfo.InvariantCulture);
				var y = double.Parse(pMatch.Groups[4].Value, CultureInfo.InvariantCulture);
				x = Math.Clamp(x, 0, 1);
				y = Math.Clamp(y, 0, 1);
				var style = pMatch.Groups[5].Value.Trim() is { Length: > 0 } styles ? ParseStyle(styles) : null;
				points.Add((new QuadrantPoint(label, x, y, style), pMatch.Groups[2].Success ? pMatch.Groups[2].Value : null));
			}
		}

		// classDef may follow the points that use it, so classes are applied once every line is read.
		var resolved = new List<QuadrantPoint>(points.Count);
		foreach (var (point, className) in points)
		{
			var classStyle = className is not null && classes.TryGetValue(className, out var found) ? found : null;
			resolved.Add(classStyle is null ? point : point with { Style = Merge(classStyle, point.Style) });
		}

		return new QuadrantChart
		{
			Title = title,
			XAxisLeft = xAxisLeft,
			XAxisRight = xAxisRight,
			YAxisBottom = yAxisBottom,
			YAxisTop = yAxisTop,
			Quadrant1 = q1,
			Quadrant2 = q2,
			Quadrant3 = q3,
			Quadrant4 = q4,
			Points = resolved,
		};
	}

	// `radius: 12, color: #ff3300, stroke-color: #10f0f0, stroke-width: 5px`; unknown keys are ignored.
	private static QuadrantPointStyle ParseStyle(string text)
	{
		var style = new QuadrantPointStyle();
		foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var colon = part.IndexOf(':');
			if (colon <= 0)
				continue;

			var key = part[..colon].Trim().ToLowerInvariant();
			var value = part[(colon + 1)..].Trim();
			style = key switch
			{
				"color" => style with { Color = value },
				"radius" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) => style with { Radius = radius },
				"stroke-color" => style with { StrokeColor = value },
				"stroke-width" => style with { StrokeWidth = value },
				_ => style,
			};
		}

		return style;
	}

	private static QuadrantPointStyle Merge(QuadrantPointStyle classStyle, QuadrantPointStyle? own)
		=> own is null
			? classStyle
			: new QuadrantPointStyle(
				own.Color ?? classStyle.Color,
				own.Radius ?? classStyle.Radius,
				own.StrokeColor ?? classStyle.StrokeColor,
				own.StrokeWidth ?? classStyle.StrokeWidth);

	private static string Unquote(string text)
	{
		var trimmed = text.Trim();
		return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' ? trimmed[1..^1] : trimmed;
	}
}
