namespace Mermaider.Models;

public sealed record QuadrantChart
{
	public string? Title { get; init; }
	public string? XAxisLeft { get; init; }
	public string? XAxisRight { get; init; }
	public string? YAxisBottom { get; init; }
	public string? YAxisTop { get; init; }
	public string? Quadrant1 { get; init; }
	public string? Quadrant2 { get; init; }
	public string? Quadrant3 { get; init; }
	public string? Quadrant4 { get; init; }
	public required IReadOnlyList<QuadrantPoint> Points { get; init; }
}

public sealed record QuadrantPoint(string Label, double X, double Y, QuadrantPointStyle? Style = null);

// A point's own styles over those of its `:::class`; every member is optional, colours and widths as written.
public sealed record QuadrantPointStyle(string? Color = null, double? Radius = null, string? StrokeColor = null, string? StrokeWidth = null);
