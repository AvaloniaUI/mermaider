namespace Mermaider.Models;

/// <summary>Parsed Mermaid gantt chart.</summary>
public sealed record GanttDiagram
{
	public string? Title { get; init; }
	/// <summary>Mermaid dateFormat string (e.g. <c>YYYY-MM-DD</c>).</summary>
	public string DateFormat { get; init; } = "YYYY-MM-DD";
	/// <summary>The axis label format (d3 time format, e.g. <c>%Y-%m-%d</c>), or null for the renderer's choice.</summary>
	public string? AxisFormat { get; init; }
	/// <summary>The axis tick spacing as written (e.g. <c>1week</c>), or null.</summary>
	public string? TickInterval { get; init; }
	/// <summary>The day weekly ticks start on (<c>weekday monday</c>), or null.</summary>
	public string? Weekday { get; init; }
	/// <summary>The today marker setting as written: <c>off</c>, a CSS style, or null for the default marker.</summary>
	public string? TodayMarker { get; init; }
	public required IReadOnlyList<GanttSection> Sections { get; init; }
}

public sealed record GanttSection(string? Name, IReadOnlyList<GanttTask> Tasks);

public sealed record GanttTask(
	string Name,
	string? Id,
	DateTime Start,
	DateTime End,
	GanttTaskTags Tags);

[Flags]
public enum GanttTaskTags
{
	None = 0,
	Done = 1,
	Active = 2,
	Crit = 4,
	Milestone = 8,
	/// <summary>A vertical marker line at the task's start rather than a bar.</summary>
	Vert = 16,
}
