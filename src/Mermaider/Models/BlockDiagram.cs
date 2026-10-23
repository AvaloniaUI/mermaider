namespace Mermaider.Models;

/// <summary>Block diagram (grid of labeled boxes).</summary>
public sealed record BlockDiagram
{
	public string? Title { get; init; }
	/// <summary>Grid columns at the top level; without a <c>columns</c> statement, every top-level block sits in one row.</summary>
	public int Columns { get; init; } = 1;
	/// <summary>The top-level blocks; a composite block holds its own in <see cref="BlockNode.Children"/>.</summary>
	public required IReadOnlyList<BlockNode> Nodes { get; init; }
	public IReadOnlyList<BlockEdge> Edges { get; init; } = [];
	/// <summary><c>classDef</c> declarations by class name.</summary>
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ClassDefs { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();
	/// <summary>Class names given to each block by <c>class</c> statements or <c>:::name</c>, by block id.</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> ClassAssignments { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
	/// <summary><c>style</c> declarations by block id.</summary>
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NodeStyles { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();
}

/// <summary><paramref name="Span"/> is the number of grid columns the block covers (<c>A:2</c>).</summary>
public sealed record BlockNode(string Id, string Label, bool Rounded = false, bool IsSpace = false, int Span = 1)
{
	public BlockShape Shape { get; init; } = Rounded ? BlockShape.Rounded : BlockShape.Square;
	/// <summary>Whether this is a <c>block … end</c> group laid out as its own grid.</summary>
	public bool IsComposite { get; init; }
	/// <summary>A composite's blocks.</summary>
	public IReadOnlyList<BlockNode> Children { get; init; } = [];
	/// <summary>A composite's grid columns, or null when it has no <c>columns</c> statement and keeps its blocks in one row.</summary>
	public int? Columns { get; init; }
	/// <summary>Which way a block arrow (<see cref="BlockShape.BlockArrow"/>) points.</summary>
	public BlockArrowDirection ArrowDirections { get; init; }
}

public enum BlockShape
{
	Square,
	Rounded,
	Stadium,
	Subroutine,
	Cylinder,
	Circle,
	DoubleCircle,
	Asymmetric,
	Diamond,
	Hexagon,
	Parallelogram,
	ParallelogramAlt,
	Trapezoid,
	TrapezoidAlt,
	/// <summary>A block shaped as an arrow (<c>id&lt;["label"]&gt;(right)</c>).</summary>
	BlockArrow,
}

[Flags]
public enum BlockArrowDirection
{
	None = 0,
	Right = 1,
	Left = 2,
	Up = 4,
	Down = 8,
}

public sealed record BlockEdge(string From, string To, string? Label = null)
{
	public BlockEdgeLine Line { get; init; } = BlockEdgeLine.Solid;
	/// <summary>False for an open link (<c>---</c>, <c>-.-</c>, <c>===</c>).</summary>
	public bool HasArrowEnd { get; init; } = true;
}

public enum BlockEdgeLine { Solid, Dotted, Thick }
