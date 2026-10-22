using Mermaider.Models;
using Mermaider.Parsing;
using Mermaider.Rendering;

namespace Mermaider;

/// <summary>
/// Parses Mermaid diagram text into its logical model for every supported diagram type, without layout or rendering.
/// Use it to draw diagrams with your own layout and graphics, or to inspect diagram source.
/// </summary>
public static class MermaidParser
{
	/// <summary>
	/// Parse Mermaid diagram text into a <see cref="MermaidDiagram"/>.
	/// </summary>
	/// <param name="text">Mermaid source text, optionally with YAML frontmatter and <c>%%{init}%%</c> directives.</param>
	/// <param name="limits">
	/// Input and element limits; <see cref="ResourceLimits.Default"/> when <c>null</c>. Only the limits that apply to
	/// parsing are enforced (<see cref="ResourceLimits.MaxInputLength"/>, <see cref="ResourceLimits.MaxLines"/>,
	/// <see cref="ResourceLimits.MaxLineLength"/>, <see cref="ResourceLimits.MaxElements"/>).
	/// </param>
	/// <param name="cancellationToken">Checked between the parse phases.</param>
	/// <returns>The diagram type, its model and the metadata read from the source.</returns>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	/// <exception cref="MermaidResourceLimitException">Thrown when a resource limit is exceeded.</exception>
	/// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
	public static MermaidDiagram Parse(string text, ResourceLimits? limits = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(text);
		var effectiveLimits = limits ?? ResourceLimits.Default;
		ResourceGuard.CheckInputLength(text, effectiveLimits);

		// Parsers read numbers the same way the render pipeline does, whatever the caller's culture
		using var culture = InvariantCultureScope.Enter();
		cancellationToken.ThrowIfCancellationRequested();

		var prepared = DiagramPreparation.Prepare(text, DiagramTypes.All, effectiveLimits, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		return new MermaidDiagram
		{
			Type = prepared.DiagramType,
			Model = DiagramModelParser.Parse(prepared, effectiveLimits),
			Metadata = prepared.Metadata,
			Accessibility = prepared.Accessibility,
		};
	}
}
