using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// Parses mermaid.js block diagrams: blocks in grids of <c>columns</c>, with spans (<c>a:2</c>), spaces
/// (<c>space:3</c>), every flowchart node shape, block arrows, nested <c>block:id:span … end</c> composites that lay out
/// their own grid, edges with labels (<c>A -- "x" --&gt; B</c>, <c>A --&gt;|x| B</c>) that may define their blocks
/// inline, and <c>style</c>, <c>classDef</c> and <c>class</c>.
/// </summary>
internal static partial class BlockParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^block(?:-beta)?(?:\s+title\s+(.+))?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^columns\s+(\d+|auto)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ColumnsPattern();

	[GeneratedRegex(@"^style\s+([\w,.-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StylePattern();

	[GeneratedRegex(@"^classDef\s+([\w-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefPattern();

	[GeneratedRegex(@"^class\s+([\w,.-]+)\s+([\w-]+)\s*;?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassPattern();

	// An unlabelled operator with an optional |label|, or a label between the operator's halves.
	[GeneratedRegex(@"^(?:(-{2,}>|-{3,}|-\.+->|-\.+-|={2,}>|={3,})(?:\|([^|]*)\|)?|(--|-\.|==)\s*(?:""([^""]*)""|([^""]+?))\s*(-{2,}>|-{3,}|\.+->|\.+-|={2,}>|={3,}))", RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgeOperatorPattern();

	// Shape delimiters, longest opener first so `((` is not read as `(`.
	private static readonly (string Open, string[] Close, BlockShape Shape)[] Shapes =
	[
		("(((", [")))"], BlockShape.DoubleCircle),
		("((", ["))"], BlockShape.Circle),
		("([", ["])"], BlockShape.Stadium),
		("[[", ["]]"], BlockShape.Subroutine),
		("[(", [")]"], BlockShape.Cylinder),
		("[/", ["/]", "\\]"], BlockShape.Parallelogram),
		("[\\", ["\\]", "/]"], BlockShape.ParallelogramAlt),
		("{{", ["}}"], BlockShape.Hexagon),
		("[", ["]"], BlockShape.Square),
		("(", [")"], BlockShape.Rounded),
		("{", ["}"], BlockShape.Diamond),
		(">", ["]"], BlockShape.Asymmetric),
	];

	internal static BlockDiagram Parse(string[] lines)
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

	private static BlockDiagram ParseCore(string[] lines)
	{
		string? title = null;
		var root = new NodeBuilder("", isComposite: true);
		var scopes = new Stack<NodeBuilder>();
		scopes.Push(root);
		var byId = new Dictionary<string, NodeBuilder>(StringComparer.Ordinal);
		var edges = new List<BlockEdge>();
		var classDefs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
		var classAssignments = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		var nodeStyles = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
		var spaces = 0;
		var anonymous = 0;

		var header = HeaderPattern().Match(lines[0]);
		if (header.Success && header.Groups[1].Success)
			title = Unquote(header.Groups[1].Value.Trim());

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i].Trim();

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = Unquote(titleMatch.Groups[1].Value.Trim());
				continue;
			}

			var colMatch = ColumnsPattern().Match(line);
			if (colMatch.Success)
			{
				var scope = scopes.Peek();
				scope.ExplicitColumns = true;
				scope.Columns = int.TryParse(colMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
				continue;
			}

			if (line == "end")
			{
				if (scopes.Count > 1)
					_ = scopes.Pop();
				continue;
			}

			var styleMatch = StylePattern().Match(line);
			if (styleMatch.Success)
			{
				var declarations = ParseDeclarations(styleMatch.Groups[2].Value);
				foreach (var id in SplitIds(styleMatch.Groups[1].Value))
					nodeStyles[id] = declarations;
				continue;
			}

			var classDefMatch = ClassDefPattern().Match(line);
			if (classDefMatch.Success)
			{
				classDefs[classDefMatch.Groups[1].Value] = ParseDeclarations(classDefMatch.Groups[2].Value);
				continue;
			}

			var classMatch = ClassPattern().Match(line);
			if (classMatch.Success)
			{
				foreach (var id in SplitIds(classMatch.Groups[1].Value))
					AddClass(classAssignments, id, classMatch.Groups[2].Value);
				continue;
			}

			var tokens = Tokenize(line);
			var hasEdge = tokens.Exists(t => t.Kind == TokenKind.Edge);
			NodeBuilder? previous = null;
			Token? pendingEdge = null;
			foreach (var token in tokens)
			{
				switch (token.Kind)
				{
					case TokenKind.BlockStart:
						{
							var id = token.Id ?? ("block-" + (++anonymous).ToString(CultureInfo.InvariantCulture));
							var composite = new NodeBuilder(id, isComposite: true) { Span = token.Span };
							scopes.Peek().Children.Add(composite);
							byId[id] = composite;
							scopes.Push(composite);
							break;
						}
					case TokenKind.Space:
						scopes.Peek().Children.Add(new NodeBuilder("space-" + (++spaces).ToString(CultureInfo.InvariantCulture), isComposite: false) { IsSpace = true, Span = token.Span });
						break;
					case TokenKind.Node:
						{
							var node = Upsert(token, byId, scopes.Peek());
							if (token.ClassName is { } className)
								AddClass(classAssignments, node.Id, className);
							if (pendingEdge is { } edge && previous is not null)
								edges.Add(new BlockEdge(previous.Id, node.Id, edge.Label) { Line = edge.Line, HasArrowEnd = edge.HasArrowEnd });
							previous = hasEdge ? node : null;
							pendingEdge = null;
							break;
						}
					case TokenKind.Edge:
						pendingEdge = token;
						break;
				}
			}
		}

		var rootColumns = root.Columns ?? Math.Max(1, root.Children.Sum(c => Math.Max(1, c.Span)));
		return new BlockDiagram
		{
			Title = title,
			Columns = rootColumns,
			Nodes = root.Children.Select(c => c.Build()).ToList(),
			Edges = edges,
			ClassDefs = classDefs,
			ClassAssignments = classAssignments.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.Ordinal),
			NodeStyles = nodeStyles,
		};
	}

	// A block named again updates its label and shape where the new mention gives them; a new one joins the current grid.
	private static NodeBuilder Upsert(Token token, Dictionary<string, NodeBuilder> byId, NodeBuilder scope)
	{
		if (!byId.TryGetValue(token.Id!, out var node))
		{
			node = new NodeBuilder(token.Id!, isComposite: false);
			byId[node.Id] = node;
			scope.Children.Add(node);
		}

		if (token.Label is { } label)
		{
			node.Label = label;
			node.Shape = token.Shape;
			node.ArrowDirections = token.ArrowDirections;
		}

		if (token.Span > 1)
			node.Span = token.Span;
		return node;
	}

	private static void AddClass(Dictionary<string, List<string>> assignments, string id, string className)
	{
		if (!assignments.TryGetValue(id, out var classes))
			assignments[id] = classes = [];
		if (!classes.Contains(className))
			classes.Add(className);
	}

	private static List<Token> Tokenize(string line)
	{
		var tokens = new List<Token>();
		var i = 0;
		while (i < line.Length)
		{
			while (i < line.Length && char.IsWhiteSpace(line[i]))
				i++;
			if (i >= line.Length)
				break;

			if (line[i] is '-' or '=')
			{
				var match = EdgeOperatorPattern().Match(line[i..]);
				if (!match.Success)
					break;

				var op = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value + match.Groups[6].Value;
				var label = match.Groups[2].Success ? match.Groups[2].Value.Trim()
					: match.Groups[4].Success ? match.Groups[4].Value
					: match.Groups[5].Success ? match.Groups[5].Value.Trim()
					: null;
				tokens.Add(new Token(TokenKind.Edge)
				{
					Label = label is { Length: > 0 } ? label : null,
					Line = op.Contains('.') ? BlockEdgeLine.Dotted : op.Contains('=') ? BlockEdgeLine.Thick : BlockEdgeLine.Solid,
					HasArrowEnd = op.EndsWith('>'),
				});
				i += match.Length;
				continue;
			}

			var start = i;
			var id = ReadId(line, ref i);
			if (id.Length == 0)
				break;

			if (id == "block" && (i >= line.Length || line[i] == ':' || char.IsWhiteSpace(line[i])))
			{
				string? compositeId = null;
				var span = 1;
				if (i < line.Length && line[i] == ':')
				{
					i++;
					var name = ReadId(line, ref i);
					if (name.Length > 0 && !name.All(char.IsAsciiDigit))
						compositeId = name;
					else if (name.Length > 0)
						span = int.Parse(name, CultureInfo.InvariantCulture);
					span = Math.Max(span, ParseSpan(line, ref i));
				}

				tokens.Add(new Token(TokenKind.BlockStart) { Id = compositeId, Span = span });
				continue;
			}

			if (id == "space" && (i >= line.Length || line[i] == ':' || char.IsWhiteSpace(line[i])))
			{
				tokens.Add(new Token(TokenKind.Space) { Span = ParseSpan(line, ref i) });
				continue;
			}

			var token = new Token(TokenKind.Node) { Id = id };
			if (!TryParseShape(line, ref i, token))
			{
				i = start + id.Length;
			}

			if (i + 3 <= line.Length && line.AsSpan(i).StartsWith(":::"))
			{
				i += 3;
				token = token with { ClassName = ReadId(line, ref i) };
			}

			token = token with { Span = ParseSpan(line, ref i) };
			tokens.Add(token);
		}

		return tokens;
	}

	// Letters, digits and underscores, and hyphens that do not begin an edge operator (`LINE-ITEM` but not `A-->B`).
	private static string ReadId(string line, ref int i)
	{
		var start = i;
		while (i < line.Length)
		{
			var c = line[i];
			if (char.IsLetterOrDigit(c) || c is '_' or '.')
			{
				i++;
				continue;
			}

			if (c == '-' && i > start && i + 1 < line.Length && char.IsLetterOrDigit(line[i + 1]))
			{
				i++;
				continue;
			}

			break;
		}

		return line[start..i];
	}

	private static bool TryParseShape(string line, ref int i, Token token)
	{
		if (i >= line.Length)
			return false;

		// Block arrow: id<["label"]>(right) or (x, down)
		if (line.AsSpan(i).StartsWith("<["))
		{
			var close = line.IndexOf("]>", i + 2, StringComparison.Ordinal);
			if (close < 0)
				return false;
			var label = Unquote(line[(i + 2)..close].Trim());
			var j = close + 2;
			var directions = BlockArrowDirection.None;
			if (j < line.Length && line[j] == '(')
			{
				var end = line.IndexOf(')', j);
				if (end < 0)
					return false;
				foreach (var part in line[(j + 1)..end].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				{
					directions |= part.ToLowerInvariant() switch
					{
						"right" => BlockArrowDirection.Right,
						"left" => BlockArrowDirection.Left,
						"up" => BlockArrowDirection.Up,
						"down" => BlockArrowDirection.Down,
						"x" => BlockArrowDirection.Left | BlockArrowDirection.Right,
						"y" => BlockArrowDirection.Up | BlockArrowDirection.Down,
						_ => BlockArrowDirection.None,
					};
				}

				j = end + 1;
			}

			token.Label = label;
			token.Shape = BlockShape.BlockArrow;
			token.ArrowDirections = directions;
			i = j;
			return true;
		}

		foreach (var (open, closes, shape) in Shapes)
		{
			if (!line.AsSpan(i).StartsWith(open))
				continue;

			var j = i + open.Length;
			string label;
			int labelEnd;
			if (j < line.Length && line[j] == '"')
			{
				var quote = line.IndexOf('"', j + 1);
				if (quote < 0)
					return false;
				label = line[(j + 1)..quote];
				labelEnd = quote + 1;
			}
			else
			{
				labelEnd = -1;
				label = "";
				foreach (var close in closes)
				{
					var at = line.IndexOf(close, j, StringComparison.Ordinal);
					if (at >= 0 && (labelEnd < 0 || at < labelEnd))
						labelEnd = at;
				}

				if (labelEnd < 0)
					return false;
				label = line[j..labelEnd].Trim();
			}

			foreach (var close in closes)
			{
				if (!line.AsSpan(labelEnd).StartsWith(close))
					continue;

				token.Label = label;
				token.Shape = shape switch
				{
					BlockShape.Parallelogram when close == "\\]" => BlockShape.Trapezoid,
					BlockShape.ParallelogramAlt when close == "/]" => BlockShape.TrapezoidAlt,
					_ => shape,
				};
				i = labelEnd + close.Length;
				return true;
			}

			return false;
		}

		return false;
	}

	// `A:2` — the block covers two grid columns
	private static int ParseSpan(string line, ref int i)
	{
		if (i >= line.Length || line[i] != ':' || (i + 1 < line.Length && line[i + 1] == ':'))
			return 1;
		var start = i + 1;
		var end = start;
		while (end < line.Length && char.IsAsciiDigit(line[end]))
			end++;
		if (end == start || !int.TryParse(line.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1)
			return 1;
		i = end;
		return n;
	}

	// `fill:purple,stroke:gray,stroke-width:4px;` — properties without a colon are ignored.
	private static IReadOnlyDictionary<string, string> ParseDeclarations(string text)
	{
		var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var part in text.TrimEnd(';').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var colon = part.IndexOf(':');
			if (colon > 0)
				declarations[part[..colon].Trim()] = part[(colon + 1)..].Trim().TrimEnd(';');
		}

		return declarations;
	}

	private static string[] SplitIds(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static string Unquote(string value) =>
		value is ['"', .., '"'] or ['\'', .., '\''] ? value[1..^1] : value;

	private enum TokenKind { Node, Edge, Space, BlockStart }

	private sealed record Token(TokenKind Kind)
	{
		public string? Id { get; init; }
		public string? Label { get; set; }
		public BlockShape Shape { get; set; }
		public BlockArrowDirection ArrowDirections { get; set; }
		public int Span { get; init; } = 1;
		public string? ClassName { get; init; }
		public BlockEdgeLine Line { get; init; }
		public bool HasArrowEnd { get; init; } = true;
	}

	private sealed class NodeBuilder(string id, bool isComposite)
	{
		public string Id { get; } = id;
		public bool IsComposite { get; } = isComposite;
		public string? Label { get; set; }
		public BlockShape Shape { get; set; }
		public BlockArrowDirection ArrowDirections { get; set; }
		public bool IsSpace { get; init; }
		public int Span { get; set; } = 1;
		public int? Columns { get; set; }
		public bool ExplicitColumns { get; set; }
		public List<NodeBuilder> Children { get; } = [];

		public BlockNode Build() => new(Id, IsSpace || IsComposite ? Label ?? "" : Label ?? Id, Shape == BlockShape.Rounded, IsSpace, Span)
		{
			Shape = Shape,
			IsComposite = IsComposite,
			Children = Children.Select(c => c.Build()).ToList(),
			Columns = Columns,
			ArrowDirections = ArrowDirections,
		};
	}
}
