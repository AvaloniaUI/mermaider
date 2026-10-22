namespace Mermaider.Models;

/// <summary>Parsed sequence diagram — logical structure from mermaid text.</summary>
/// <remarks>
/// Every statement carries an Order: its position among the diagram's statements, increasing down the source. Notes,
/// activations and block boundaries are also placed by message index, which cannot tell apart statements that fall
/// between the same two messages (a note before or after a block's end); Order can.
/// </remarks>
public sealed record SequenceDiagram
{
	public required IReadOnlyList<SequenceActor> Actors { get; init; }
	public required IReadOnlyList<SequenceMessage> Messages { get; init; }
	public required IReadOnlyList<SequenceBlock> Blocks { get; init; }
	public required IReadOnlyList<SequenceNote> Notes { get; init; }
	public IReadOnlyList<SequenceBox> Boxes { get; init; } = [];
	public IReadOnlyList<SequenceCreate> Creates { get; init; } = [];
	public IReadOnlyList<SequenceDestroy> Destroys { get; init; } = [];
	public IReadOnlyList<SequenceActivation> Activations { get; init; } = [];
}

public readonly record struct SequenceActor(string Id, string Label, SequenceActorType Type);

public enum SequenceActorType { Participant, Actor }

public sealed record SequenceMessage(
	string From,
	string To,
	string Label,
	SequenceLineStyle LineStyle,
	SequenceArrowHead ArrowHead,
	bool Activate = false,
	bool Deactivate = false,
	bool Bidirectional = false,
	int Order = -1
);

public enum SequenceLineStyle { Solid, Dashed }
// Filled: ->> and -->>; Open: -) and --) (async); None: -> and --> (no head); Cross: -x and --x
public enum SequenceArrowHead { Filled, Open, None, Cross }

public sealed record SequenceBlock
{
	public required SequenceBlockType Type { get; init; }
	public required string Label { get; init; }
	public required int StartIndex { get; init; }
	public required int EndIndex { get; init; }
	public required IReadOnlyList<SequenceBlockDivider> Dividers { get; init; }
	public int StartOrder { get; init; } = -1;
	public int EndOrder { get; init; } = -1;
}

public enum SequenceBlockType { Loop, Alt, Opt, Par, Critical, Break, Rect }

public readonly record struct SequenceBlockDivider(int Index, string Label, int Order = -1);

public sealed record SequenceNote(
	IReadOnlyList<string> ActorIds,
	string Text,
	SequenceNotePosition Position,
	int AfterIndex,
	int Order = -1
);

public enum SequenceNotePosition { Left, Right, Over }

public sealed record SequenceBox(string? Color, string Title, IReadOnlyList<string> ActorIds);

public sealed record SequenceCreate(string ActorId, int AtMessageIndex);

public sealed record SequenceDestroy(string ActorId, int AtMessageIndex);

// An `activate` or `deactivate` statement, placed after the message at AfterIndex (-1 before the first) like a note
public sealed record SequenceActivation(string ActorId, bool Activate, int AfterIndex, int Order = -1);
