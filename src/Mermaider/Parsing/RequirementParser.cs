using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class RequirementParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^direction\s+(TB|BT|LR|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DirectionPattern();

	[GeneratedRegex(
		@"^(requirement|functionalRequirement|interfaceRequirement|performanceRequirement|physicalRequirement|designConstraint)\s+(.+?)\s*\{\s*$",
		RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex RequirementOpenPattern();

	[GeneratedRegex(@"^element\s+(.+?)\s*\{\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ElementOpenPattern();

	[GeneratedRegex(@"^(\w+)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex PropertyPattern();

	[GeneratedRegex(@"^style\s+([\w,.-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StylePattern();

	[GeneratedRegex(@"^classDef\s+([\w-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefPattern();

	[GeneratedRegex(@"^class\s+([\w,.-]+)\s+([\w-]+)\s*;?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassPattern();

	// A - type -> B
	[GeneratedRegex(
		@"^(.+?)\s+-\s+(contains|copies|derives|satisfies|verifies|refines|traces)\s+->\s+(.+)$",
		RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ForwardRelationPattern();

	// B <- type - A
	[GeneratedRegex(
		@"^(.+?)\s+<-\s+(contains|copies|derives|satisfies|verifies|refines|traces)\s+-\s+(.+)$",
		RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ReverseRelationPattern();

	internal static RequirementDiagram Parse(string[] lines)
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

	private static RequirementDiagram ParseCore(string[] lines)
	{
		string? title = null;
		var direction = Direction.TB;
		var requirements = new List<RequirementNode>();
		var elements = new List<RequirementElement>();
		var relations = new List<RequirementRelation>();
		var classDefs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
		var classAssignments = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		var nodeStyles = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var styleMatch = StylePattern().Match(line);
			if (styleMatch.Success)
			{
				var declarations = ParseDeclarations(styleMatch.Groups[2].Value);
				foreach (var id in SplitNames(styleMatch.Groups[1].Value))
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
				foreach (var id in SplitNames(classMatch.Groups[1].Value))
					AddClass(classAssignments, id, classMatch.Groups[2].Value);
				continue;
			}

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = Unquote(titleMatch.Groups[1].Value.Trim());
				continue;
			}

			var dirMatch = DirectionPattern().Match(line);
			if (dirMatch.Success)
			{
				direction = ParseDirection(dirMatch.Groups[1].Value);
				continue;
			}

			var reqMatch = RequirementOpenPattern().Match(line);
			if (reqMatch.Success)
			{
				var kind = ParseKind(reqMatch.Groups[1].Value);
				var name = TakeClass(reqMatch.Groups[2].Value.Trim(), classAssignments);
				i = ParseRequirementBody(lines, i + 1, name, kind, requirements);
				continue;
			}

			var elemMatch = ElementOpenPattern().Match(line);
			if (elemMatch.Success)
			{
				var name = TakeClass(elemMatch.Groups[1].Value.Trim(), classAssignments);
				i = ParseElementBody(lines, i + 1, name, elements);
				continue;
			}

			var fwdMatch = ForwardRelationPattern().Match(line);
			if (fwdMatch.Success)
			{
				var src = Unquote(fwdMatch.Groups[1].Value.Trim());
				var relType = ParseRelationType(fwdMatch.Groups[2].Value);
				var dst = Unquote(fwdMatch.Groups[3].Value.Trim());
				relations.Add(new RequirementRelation(src, dst, relType));
				continue;
			}

			var revMatch = ReverseRelationPattern().Match(line);
			if (revMatch.Success)
			{
				// B <- type - A  means A - type -> B
				var dst = Unquote(revMatch.Groups[1].Value.Trim());
				var relType = ParseRelationType(revMatch.Groups[2].Value);
				var src = Unquote(revMatch.Groups[3].Value.Trim());
				relations.Add(new RequirementRelation(src, dst, relType));
			}
		}

		return new RequirementDiagram
		{
			Title = title,
			Direction = direction,
			Requirements = requirements,
			Elements = elements,
			Relations = relations,
			ClassDefs = classDefs,
			ClassAssignments = classAssignments.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.Ordinal),
			NodeStyles = nodeStyles,
		};
	}

	// `name:::class` declares the class with the requirement or element; the name is what remains, unquoted.
	private static string TakeClass(string declared, Dictionary<string, List<string>> assignments)
	{
		var marker = declared.LastIndexOf(":::", StringComparison.Ordinal);
		if (marker <= 0)
			return Unquote(declared);

		var name = Unquote(declared[..marker].Trim());
		AddClass(assignments, name, declared[(marker + 3)..].Trim());
		return name;
	}

	private static void AddClass(Dictionary<string, List<string>> assignments, string name, string className)
	{
		if (className.Length == 0)
			return;
		if (!assignments.TryGetValue(name, out var classes))
			assignments[name] = classes = [];
		if (!classes.Contains(className))
			classes.Add(className);
	}

	// `fill:purple,stroke:gray, color: green` — properties without a colon are ignored.
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

	private static string[] SplitNames(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static int ParseRequirementBody(
		string[] lines, int start, string name, RequirementKind kind, List<RequirementNode> requirements)
	{
		string? id = null;
		string? text = null;
		var risk = RequirementRisk.Unspecified;
		var verify = RequirementVerifyMethod.Unspecified;

		var i = start;
		for (; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line is "}")
				break;

			var prop = PropertyPattern().Match(line);
			if (!prop.Success)
				continue;

			var key = prop.Groups[1].Value;
			var value = Unquote(prop.Groups[2].Value.Trim());

			if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
				id = value;
			else if (key.Equals("text", StringComparison.OrdinalIgnoreCase))
				text = value;
			else if (key.Equals("risk", StringComparison.OrdinalIgnoreCase))
				risk = ParseRisk(value);
			else if (key.Equals("verifymethod", StringComparison.OrdinalIgnoreCase)
				|| key.Equals("verifyMethod", StringComparison.OrdinalIgnoreCase))
				verify = ParseVerifyMethod(value);
		}

		requirements.Add(new RequirementNode(name, kind, id, text, risk, verify));
		return i;
	}

	private static int ParseElementBody(
		string[] lines, int start, string name, List<RequirementElement> elements)
	{
		string? type = null;
		string? docRef = null;

		var i = start;
		for (; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line is "}")
				break;

			var prop = PropertyPattern().Match(line);
			if (!prop.Success)
				continue;

			var key = prop.Groups[1].Value;
			var value = Unquote(prop.Groups[2].Value.Trim());

			if (key.Equals("type", StringComparison.OrdinalIgnoreCase))
				type = value;
			else if (key.Equals("docref", StringComparison.OrdinalIgnoreCase)
				|| key.Equals("docRef", StringComparison.OrdinalIgnoreCase))
				docRef = value;
		}

		elements.Add(new RequirementElement(name, type, docRef));
		return i;
	}

	private static Direction ParseDirection(string value) => value.ToUpperInvariant() switch
	{
		"TB" or "TD" => Direction.TB,
		"BT" => Direction.BT,
		"LR" => Direction.LR,
		"RL" => Direction.RL,
		_ => Direction.TB,
	};

	private static RequirementKind ParseKind(string value) => value.ToLowerInvariant() switch
	{
		"functionalrequirement" => RequirementKind.FunctionalRequirement,
		"interfacerequirement" => RequirementKind.InterfaceRequirement,
		"performancerequirement" => RequirementKind.PerformanceRequirement,
		"physicalrequirement" => RequirementKind.PhysicalRequirement,
		"designconstraint" => RequirementKind.DesignConstraint,
		_ => RequirementKind.Requirement,
	};

	private static RequirementRisk ParseRisk(string value) => value.ToLowerInvariant() switch
	{
		"low" => RequirementRisk.Low,
		"medium" => RequirementRisk.Medium,
		"high" => RequirementRisk.High,
		_ => RequirementRisk.Unspecified,
	};

	private static RequirementVerifyMethod ParseVerifyMethod(string value) => value.ToLowerInvariant() switch
	{
		"analysis" => RequirementVerifyMethod.Analysis,
		"demonstration" => RequirementVerifyMethod.Demonstration,
		"inspection" => RequirementVerifyMethod.Inspection,
		"test" => RequirementVerifyMethod.Test,
		_ => RequirementVerifyMethod.Unspecified,
	};

	private static RequirementRelationType ParseRelationType(string value) => value.ToLowerInvariant() switch
	{
		"contains" => RequirementRelationType.Contains,
		"copies" => RequirementRelationType.Copies,
		"derives" => RequirementRelationType.Derives,
		"satisfies" => RequirementRelationType.Satisfies,
		"verifies" => RequirementRelationType.Verifies,
		"refines" => RequirementRelationType.Refines,
		"traces" => RequirementRelationType.Traces,
		_ => RequirementRelationType.Traces,
	};

	private static string Unquote(string value)
	{
		if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
			return value[1..^1];
		return value;
	}
}
