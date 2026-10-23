namespace Mermaider.Models;

/// <summary>SysML-style requirement diagram (requirements, elements, relationships).</summary>
public sealed record RequirementDiagram
{
	public string? Title { get; init; }
	public Direction Direction { get; init; } = Direction.TB;
	public required IReadOnlyList<RequirementNode> Requirements { get; init; }
	public required IReadOnlyList<RequirementElement> Elements { get; init; }
	public required IReadOnlyList<RequirementRelation> Relations { get; init; }
	/// <summary><c>classDef</c> declarations by class name.</summary>
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ClassDefs { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();
	/// <summary>Class names given to each requirement or element by <c>class</c> statements or <c>:::name</c>, by name.</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> ClassAssignments { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
	/// <summary><c>style</c> declarations by requirement or element name.</summary>
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NodeStyles { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();
}

public sealed record RequirementNode(
	string Name,
	RequirementKind Kind,
	string? Id = null,
	string? Text = null,
	RequirementRisk Risk = RequirementRisk.Unspecified,
	RequirementVerifyMethod VerifyMethod = RequirementVerifyMethod.Unspecified);

public sealed record RequirementElement(
	string Name,
	string? Type = null,
	string? DocRef = null);

public sealed record RequirementRelation(
	string Source,
	string Target,
	RequirementRelationType Type);

public enum RequirementKind
{
	Requirement,
	FunctionalRequirement,
	InterfaceRequirement,
	PerformanceRequirement,
	PhysicalRequirement,
	DesignConstraint,
}

public enum RequirementRisk
{
	Unspecified,
	Low,
	Medium,
	High,
}

public enum RequirementVerifyMethod
{
	Unspecified,
	Analysis,
	Demonstration,
	Inspection,
	Test,
}

public enum RequirementRelationType
{
	Contains,
	Copies,
	Derives,
	Satisfies,
	Verifies,
	Refines,
	Traces,
}
