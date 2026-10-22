using Mermaider.Models;
using Mermaider.Rendering;

namespace Mermaider.Parsing;

/// <summary>
/// Dispatches a prepared diagram to the parser for its type. Feeds each parser the same line set the render
/// pipeline does: indentation-preserving lines for the indentation-structured types, filtered lines otherwise.
/// </summary>
internal static class DiagramModelParser
{
	internal static object Parse(PreparedDiagram diagram, ResourceLimits limits)
	{
		var lines = diagram.FilteredLines;
		switch (diagram.DiagramType)
		{
			case DiagramType.Sequence:
				{
					var parsed = SequenceParser.Parse(lines);
					ResourceGuard.CheckElements(parsed.Actors.Count + parsed.Messages.Count + parsed.Notes.Count, limits);
					return parsed;
				}
			case DiagramType.Class:
				{
					var parsed = ClassParser.Parse(lines);
					ResourceGuard.CheckElements(parsed.Classes.Count + parsed.Relationships.Count, limits);
					return parsed;
				}
			case DiagramType.Er:
				{
					var parsed = ErParser.Parse(lines);
					ResourceGuard.CheckElements(parsed.Entities.Count + parsed.Relationships.Count, limits);
					return parsed;
				}
			case DiagramType.Venn:
				{
					var parsed = VennParser.Parse(lines);
					ResourceGuard.CheckElements(parsed.Sets.Count + parsed.Unions.Count, limits);
					return parsed;
				}
			case DiagramType.Architecture:
				{
					var parsed = ArchitectureParser.Parse(lines);
					ResourceGuard.CheckElements(
						parsed.Services.Count + parsed.Groups.Count + parsed.Edges.Count + parsed.Junctions.Count,
						limits);
					return parsed;
				}
			case DiagramType.Flowchart or DiagramType.State:
				{
					var parsed = diagram.DiagramType == DiagramType.Flowchart ? FlowchartParser.Parse(lines) : StateParser.Parse(lines);
					ResourceGuard.CheckElements(parsed.Nodes.Count + parsed.Edges.Count, limits);
					return parsed;
				}
			case DiagramType.Pie:
				return PieParser.Parse(lines);
			case DiagramType.Quadrant:
				return QuadrantParser.Parse(lines);
			case DiagramType.Timeline:
				return TimelineParser.Parse(lines);
			case DiagramType.GitGraph:
				return GitGraphParser.Parse(lines);
			case DiagramType.Radar:
				return RadarParser.Parse(lines);
			case DiagramType.Treemap:
				return TreemapParser.Parse(DiagramPreprocessor.PreprocessLinesPreserveIndent(diagram.CleanedSource));
			case DiagramType.Mindmap:
				return MindmapParser.Parse(DiagramPreprocessor.PreprocessLinesPreserveIndent(diagram.CleanedSource));
			case DiagramType.Gantt:
				return GanttParser.Parse(lines);
			case DiagramType.Journey:
				return JourneyParser.Parse(lines);
			case DiagramType.C4:
				return C4Parser.Parse(lines);
			case DiagramType.Sankey:
				return SankeyParser.Parse(lines);
			case DiagramType.XyChart:
				return XyChartParser.Parse(lines);
			case DiagramType.Requirement:
				return RequirementParser.Parse(lines);
			case DiagramType.Packet:
				return PacketParser.Parse(lines);
			case DiagramType.Kanban:
				return KanbanParser.Parse(DiagramPreprocessor.PreprocessLinesPreserveIndent(diagram.CleanedSource, diagram.Accessibility));
			case DiagramType.Block:
				return BlockParser.Parse(lines);
			case DiagramType.TreeView:
				return TreeViewParser.Parse(DiagramPreprocessor.PreprocessLinesPreserveIndent(diagram.CleanedSource, diagram.Accessibility));
			default:
				throw new MermaidParseException($"Diagram type '{diagram.DiagramType}' is not yet supported.");
		}
	}
}
