using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class BlockParserTests
{
	[Test]
	public void Parses_grid_nodes_and_columns()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 3",
			"A[\"A\"] B[\"B\"] C[\"C\"]",
			"D[\"D\"] E[\"E\"] F[\"F\"]",
		]);

		diagram.Columns.Should().Be(3);
		diagram.Nodes.Should().HaveCount(6);
		diagram.Nodes[0].Id.Should().Be("A");
		diagram.Nodes[0].Label.Should().Be("A");
		diagram.Nodes[5].Id.Should().Be("F");
		diagram.Nodes[5].Label.Should().Be("F");
	}

	[Test]
	public void Parses_block_header_without_beta()
	{
		var diagram = BlockParser.Parse(["block", "A[\"One\"]"]);

		diagram.Nodes.Should().HaveCount(1);
		diagram.Nodes[0].Label.Should().Be("One");
	}

	[Test]
	public void Without_columns_every_block_sits_in_one_row()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"A[\"First\"]",
			"B[\"Second\"]:2",
		]);

		diagram.Columns.Should().Be(3);
		diagram.Nodes.Should().HaveCount(2);
	}

	[Test]
	public void Parses_id_only_and_unquoted_labels()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 2",
			"A B[Label]",
		]);

		diagram.Nodes.Should().HaveCount(2);
		diagram.Nodes[0].Id.Should().Be("A");
		diagram.Nodes[0].Label.Should().Be("A");
		diagram.Nodes[1].Id.Should().Be("B");
		diagram.Nodes[1].Label.Should().Be("Label");
	}

	[Test]
	public void Parses_rounded_paren_form()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"R(\"Rounded\")",
		]);

		diagram.Nodes.Should().HaveCount(1);
		diagram.Nodes[0].Rounded.Should().BeTrue();
		diagram.Nodes[0].Label.Should().Be("Rounded");
	}

	[Test]
	public void Parses_title_line_and_compact_header()
	{
		var withLine = BlockParser.Parse(
		[
			"block-beta",
			"title Grid Layout",
			"A[\"A\"]",
		]);
		withLine.Title.Should().Be("Grid Layout");

		var compact = BlockParser.Parse(
		[
			"block-beta title Compact",
			"A[\"A\"]",
		]);
		compact.Title.Should().Be("Compact");
	}

	[Test]
	public void Parses_simple_edges()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 2",
			"A[\"A\"] B[\"B\"]",
			"A --> B",
		]);

		diagram.Edges.Should().HaveCount(1);
		diagram.Edges[0].From.Should().Be("A");
		diagram.Edges[0].To.Should().Be("B");
	}

	[Test]
	public void Naming_a_block_again_keeps_one_cell_and_updates_its_label()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"A[\"First\"] A[\"Second\"] A",
		]);

		diagram.Nodes.Should().HaveCount(1);
		diagram.Nodes[0].Label.Should().Be("Second");
	}

	[Test]
	public void Parses_space_spacers()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 3",
			"A[\"A\"] space B[\"B\"]",
		]);

		diagram.Nodes.Should().HaveCount(3);
		diagram.Nodes[1].IsSpace.Should().BeTrue();
		diagram.Nodes[1].Label.Should().BeEmpty();
		diagram.Nodes[0].IsSpace.Should().BeFalse();
		diagram.Nodes[2].IsSpace.Should().BeFalse();
	}

	[Test]
	public void Literal_space_prefixed_ids_are_not_spacers()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 2",
			"__space_0[\"Slot\"] space",
		]);

		diagram.Nodes.Should().HaveCount(2);
		diagram.Nodes[0].Id.Should().Be("__space_0");
		diagram.Nodes[0].Label.Should().Be("Slot");
		diagram.Nodes[0].IsSpace.Should().BeFalse();
		diagram.Nodes[1].IsSpace.Should().BeTrue();
	}

	[Test]
	public void Ignores_invalid_columns()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 0",
			"A[\"A\"]",
		]);

		diagram.Columns.Should().Be(1);
	}

	[Test]
	public void Handles_empty_block()
	{
		var diagram = BlockParser.Parse(["block-beta"]);

		diagram.Nodes.Should().BeEmpty();
		diagram.Edges.Should().BeEmpty();
		diagram.Title.Should().BeNull();
	}

	[Test]
	public void Parses_column_spans()
	{
		var diagram = BlockParser.Parse(["block-beta", "columns 3", "A[\"A\"]:2 B space:1 C:3"]);

		diagram.Nodes.Select(n => n.Span).Should().Equal(2, 1, 1, 3);
	}

	[Test]
	public void Parses_edge_labels()
	{
		var diagram = BlockParser.Parse(["block-beta", "A B C", "A -- \"quoted\" --> B", "B -- bare --> C", "A -->|piped| C", "A --> C"]);

		diagram.Edges.Select(e => e.Label).Should().Equal("quoted", "bare", "piped", null);
	}

	[Test]
	public void Parses_every_node_shape()
	{
		var diagram = BlockParser.Parse(
		[
			"block",
			"a(\"r\") b([\"s\"]) c[[\"sub\"]] d[(\"db\")] e((\"c\")) f>\"asym\"] g{\"d\"} h{{\"hex\"}}",
			"i[/\"p\"/] j[\\\"pa\"\\] k[/\"t\"\\] l[\\\"ta\"/] m(((\"dc\"))) n[\"sq\"]",
		]);

		diagram.Nodes.Select(n => n.Shape).Should().Equal(
			BlockShape.Rounded, BlockShape.Stadium, BlockShape.Subroutine, BlockShape.Cylinder, BlockShape.Circle,
			BlockShape.Asymmetric, BlockShape.Diamond, BlockShape.Hexagon, BlockShape.Parallelogram,
			BlockShape.ParallelogramAlt, BlockShape.Trapezoid, BlockShape.TrapezoidAlt, BlockShape.DoubleCircle,
			BlockShape.Square);
		diagram.Nodes.Select(n => n.Label).Should().Equal("r", "s", "sub", "db", "c", "asym", "d", "hex", "p", "pa", "t", "ta", "dc", "sq");
		diagram.Nodes[0].Rounded.Should().BeTrue();
	}

	[Test]
	public void Parses_block_arrows()
	{
		var diagram = BlockParser.Parse(
		[
			"block",
			"r<[\"Label\"]>(right) x<[\"Both\"]>(x) d<[\"Two\"]>(x, down)",
		]);

		diagram.Nodes.Should().AllSatisfy(n => n.Shape.Should().Be(BlockShape.BlockArrow));
		diagram.Nodes[0].ArrowDirections.Should().Be(BlockArrowDirection.Right);
		diagram.Nodes[1].ArrowDirections.Should().Be(BlockArrowDirection.Left | BlockArrowDirection.Right);
		diagram.Nodes[2].ArrowDirections.Should().Be(BlockArrowDirection.Left | BlockArrowDirection.Right | BlockArrowDirection.Down);
		diagram.Nodes[2].Label.Should().Be("Two");
	}

	[Test]
	public void Composite_blocks_keep_their_children_columns_and_span()
	{
		var diagram = BlockParser.Parse(
		[
			"block",
			"columns 3",
			"a:3",
			"block:group1:2",
			"columns 2",
			"h i j k",
			"end",
			"g",
			"block:group2:3",
			"l m n o p q r",
			"end",
		]);

		diagram.Columns.Should().Be(3);
		diagram.Nodes.Select(n => n.Id).Should().Equal("a", "group1", "g", "group2");
		var group1 = diagram.Nodes[1];
		group1.IsComposite.Should().BeTrue();
		group1.Span.Should().Be(2);
		group1.Columns.Should().Be(2);
		group1.Children.Select(n => n.Id).Should().Equal("h", "i", "j", "k");
		diagram.Nodes[3].Columns.Should().BeNull();
		diagram.Nodes[3].Children.Should().HaveCount(7);
	}

	[Test]
	public void Anonymous_composite_gets_an_id()
	{
		var diagram = BlockParser.Parse(["block", "block", "D", "end", "A[\"A: I am a wide one\"]"]);

		diagram.Nodes[0].IsComposite.Should().BeTrue();
		diagram.Nodes[0].Children.Single().Id.Should().Be("D");
		diagram.Nodes[1].Label.Should().Be("A: I am a wide one");
	}

	[Test]
	public void Edges_take_kinds_labels_and_inline_definitions()
	{
		var diagram = BlockParser.Parse(
		[
			"block",
			"id1 space id2 c d",
			"id1(\"Start\")-->id2(\"Stop\")",
			"id2-- \"X\" -->c",
			"c-.->d",
			"d --- id1",
			"c ==> id1",
			"d -->|pipe| id2",
		]);

		diagram.Nodes.Should().HaveCount(5);
		diagram.Nodes[0].Label.Should().Be("Start");
		diagram.Nodes[0].Shape.Should().Be(BlockShape.Rounded);
		diagram.Edges.Select(e => (e.From, e.To, e.Label, e.Line, e.HasArrowEnd)).Should().Equal(
			("id1", "id2", (string?)null, BlockEdgeLine.Solid, true),
			("id2", "c", "X", BlockEdgeLine.Solid, true),
			("c", "d", null, BlockEdgeLine.Dotted, true),
			("d", "id1", null, BlockEdgeLine.Solid, false),
			("c", "id1", null, BlockEdgeLine.Thick, true),
			("d", "id2", "pipe", BlockEdgeLine.Solid, true));
	}

	[Test]
	public void Parses_styles_classes_and_class_shorthand()
	{
		var diagram = BlockParser.Parse(
		[
			"block",
			"A space B C:::warm",
			"classDef blue fill:#6e6ce6,stroke:#333,stroke-width:4px;",
			"class A blue",
			"style B fill:#bbf,stroke:#f66,stroke-width:2px,color:#fff,stroke-dasharray: 5 5",
			"style A fill#969;",
		]);

		diagram.ClassDefs["blue"]["fill"].Should().Be("#6e6ce6");
		diagram.ClassDefs["blue"]["stroke-width"].Should().Be("4px");
		diagram.ClassAssignments["A"].Should().Equal("blue");
		diagram.ClassAssignments["C"].Should().Equal("warm");
		diagram.NodeStyles["B"]["stroke-dasharray"].Should().Be("5 5");
		diagram.NodeStyles["A"].Should().BeEmpty();
	}

	[Test]
	public void Hyphenated_ids_and_wide_spaces()
	{
		var diagram = BlockParser.Parse(["block", "LINE-ITEM space:3 B", "LINE-ITEM-->B"]);

		diagram.Nodes.Select(n => (n.Id, n.IsSpace, n.Span)).Should().Equal(("LINE-ITEM", false, 1), ("space-1", true, 3), ("B", false, 1));
		diagram.Edges.Single().From.Should().Be("LINE-ITEM");
	}
}
