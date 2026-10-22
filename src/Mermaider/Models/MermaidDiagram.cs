namespace Mermaider.Models;

/// <summary>
/// A parsed Mermaid diagram: its type, its logical model and the metadata read alongside it.
/// Produced by <see cref="MermaidParser.Parse"/> without running layout or rendering.
/// </summary>
public sealed record MermaidDiagram
{
	/// <summary>The detected diagram type.</summary>
	public required DiagramType Type { get; init; }

	/// <summary>
	/// The parsed model. Its runtime type follows <see cref="Type"/>:
	/// <see cref="MermaidGraph"/> for <see cref="DiagramType.Flowchart"/> and <see cref="DiagramType.State"/>,
	/// <see cref="SequenceDiagram"/>, <see cref="ClassDiagram"/>, <see cref="ErDiagram"/>, <see cref="PieChart"/>,
	/// <see cref="QuadrantChart"/>, <see cref="TimelineDiagram"/>, <see cref="GitGraph"/>, <see cref="RadarChart"/>,
	/// <see cref="TreemapDiagram"/>, <see cref="VennDiagram"/>, <see cref="MindmapDiagram"/>, <see cref="GanttDiagram"/>,
	/// <see cref="JourneyDiagram"/>, <see cref="C4Diagram"/>, <see cref="SankeyDiagram"/>, <see cref="XyChart"/>,
	/// <see cref="RequirementDiagram"/>, <see cref="PacketDiagram"/>, <see cref="KanbanDiagram"/>,
	/// <see cref="ArchitectureDiagram"/>, <see cref="BlockDiagram"/> and <see cref="TreeViewDiagram"/>
	/// for the matching types.
	/// </summary>
	public required object Model { get; init; }

	/// <summary>Title, theme and theme variables from YAML frontmatter or a <c>%%{init}%%</c> directive.</summary>
	public DiagramMetadata Metadata { get; init; } = DiagramMetadata.Empty;

	/// <summary>The <c>accTitle</c> and <c>accDescr</c> directives, removed from the lines the model was parsed from.</summary>
	public AccessibilityInfo Accessibility { get; init; } = new();
}
