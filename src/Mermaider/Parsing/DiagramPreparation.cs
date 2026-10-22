using Mermaider.Models;
using Mermaider.Rendering;

namespace Mermaider.Parsing;

/// <summary>Diagram source after preprocessing, ready for the parser of its type.</summary>
internal sealed record PreparedDiagram(
	string CleanedSource,
	DiagramMetadata Metadata,
	string[] Lines,
	string[] FilteredLines,
	DiagramType DiagramType,
	AccessibilityInfo Accessibility);

/// <summary>
/// Turns raw Mermaid text into a <see cref="PreparedDiagram"/>: frontmatter and init directives stripped, lines split,
/// line limits checked, the type detected and the accessibility directives extracted. Shared by parsing and rendering,
/// and independent of either, so the parse layer can be used on its own.
/// </summary>
internal static class DiagramPreparation
{
	internal static PreparedDiagram Prepare(string text, DiagramTypes allowedDiagrams,
		ResourceLimits limits, CancellationToken ct = default)
	{
		var (cleaned, metadata) = DiagramPreprocessor.Process(text);
		var lines = DiagramPreprocessor.PreprocessLines(cleaned);
		if (lines.Length == 0)
			throw new MermaidParseException("Empty mermaid diagram");

		// Line count + line length checks (bounds aggregate regex budget)
		ResourceGuard.CheckLines(lines, limits);
		ResourceGuard.CheckLineLength(lines, limits);
		ct.ThrowIfCancellationRequested();

		var diagramType = DiagramDetector.Detect(cleaned.AsSpan());
		if ((allowedDiagrams & diagramType.ToFlag()) == 0)
			throw new MermaidParseException(
				$"Diagram type '{diagramType}' is not in the allowed set. " +
				$"Adjust RenderOptions.AllowedDiagrams to include it.");

		var (accessibility, filteredLines) = AccessibilityParser.Extract(lines);
		return new PreparedDiagram(cleaned, metadata, lines, filteredLines, diagramType, accessibility);
	}
}
