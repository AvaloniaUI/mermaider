using System.Globalization;
using AwesomeAssertions;
using Mermaider.Examples;
using Mermaider.Models;

namespace Mermaider.Tests.Parsing;

public class MermaidParserTests
{
	public static IEnumerable<string> ExampleSlugs() => DiagramExamples.All.Select(d => d.Slug);

	[Test]
	public void Parses_flowchart_to_graph()
	{
		var diagram = MermaidParser.Parse("graph LR\n  A[Start] --> B{Choice}\n  B -->|yes| C");

		diagram.Type.Should().Be(DiagramType.Flowchart);
		var graph = diagram.Model.Should().BeOfType<MermaidGraph>().Subject;
		graph.Direction.Should().Be(Direction.LR);
		graph.Nodes.Should().HaveCount(3);
		graph.Edges.Should().HaveCount(2);
	}

	[Test]
	public void Front_matter_with_a_nested_config_map_is_stripped_and_exposed()
	{
		var diagram = MermaidParser.Parse("""
			---
			title: Releases
			config:
			  logLevel: 'debug'
			  theme: 'base'
			  gitGraph:
			    showBranches: false
			    mainBranchName: "trunk"
			---
			      gitGraph
			        commit
			        branch hotfix
			""");

		diagram.Type.Should().Be(DiagramType.GitGraph);
		diagram.Metadata.Title.Should().Be("Releases");
		diagram.Metadata.Config.Should().NotBeNull();
		diagram.Metadata.Config!["theme"].Should().Be("base");
		diagram.Metadata.Config["gitGraph.showBranches"].Should().Be("false");
		diagram.Metadata.Config["gitGraph.mainBranchName"].Should().Be("trunk");
	}

	[Test]
	public void Parses_state_to_graph()
	{
		var diagram = MermaidParser.Parse("stateDiagram-v2\n  [*] --> Idle\n  Idle --> Busy");

		diagram.Type.Should().Be(DiagramType.State);
		diagram.Model.Should().BeOfType<MermaidGraph>();
	}

	[Test]
	public void Parses_sequence_diagram()
	{
		var diagram = MermaidParser.Parse("sequenceDiagram\n  Alice->>John: Hello\n  John-->>Alice: Hi");

		diagram.Type.Should().Be(DiagramType.Sequence);
		var sequence = diagram.Model.Should().BeOfType<SequenceDiagram>().Subject;
		sequence.Actors.Should().HaveCount(2);
		sequence.Messages.Should().HaveCount(2);
	}

	[Test]
	public void Parses_class_diagram()
	{
		var diagram = MermaidParser.Parse("classDiagram\n  Animal <|-- Duck\n  class Duck {\n    +swim()\n  }");

		diagram.Type.Should().Be(DiagramType.Class);
		diagram.Model.Should().BeOfType<ClassDiagram>().Subject.Relationships.Should().HaveCount(1);
	}

	[Test]
	public void Parses_er_diagram()
	{
		var diagram = MermaidParser.Parse("erDiagram\n  CUSTOMER ||--o{ ORDER : places");

		diagram.Type.Should().Be(DiagramType.Er);
		diagram.Model.Should().BeOfType<ErDiagram>().Subject.Entities.Should().HaveCount(2);
	}

	[Test]
	public void Parses_pie_chart()
	{
		var diagram = MermaidParser.Parse("pie title Pets\n  \"Dogs\" : 386\n  \"Cats\" : 85");

		diagram.Type.Should().Be(DiagramType.Pie);
		var pie = diagram.Model.Should().BeOfType<PieChart>().Subject;
		pie.Slices.Should().HaveCount(2);
		pie.Slices[0].Value.Should().Be(386);
	}

	[Test]
	public void Parses_xychart()
	{
		var diagram = MermaidParser.Parse("""
			xychart-beta
			  title "Sales"
			  x-axis [jan, feb, mar]
			  y-axis "Revenue" 0 --> 100
			  bar [10, 50, 90]
			  line [10, 50, 90]
			""");

		diagram.Type.Should().Be(DiagramType.XyChart);
		diagram.Model.Should().BeOfType<XyChart>().Subject.Series.Should().HaveCount(2);
	}

	[Test]
	public void Parses_indentation_structured_types_from_indented_lines()
	{
		var diagram = MermaidParser.Parse("treemap-beta\n\"Root\"\n    \"A\": 10\n    \"B\": 20");

		diagram.Type.Should().Be(DiagramType.Treemap);
		var treemap = diagram.Model.Should().BeOfType<TreemapDiagram>().Subject;
		treemap.Roots.Should().ContainSingle().Which.Children.Should().HaveCount(2);
	}

	[Test]
	public void Returns_frontmatter_title_as_metadata()
	{
		var diagram = MermaidParser.Parse("---\ntitle: Checkout\n---\ngraph TD\n  A --> B");

		diagram.Metadata.Title.Should().Be("Checkout");
		diagram.Model.Should().BeOfType<MermaidGraph>().Subject.Nodes.Should().HaveCount(2);
	}

	[Test]
	public void Returns_accessibility_and_keeps_it_out_of_the_model()
	{
		var diagram = MermaidParser.Parse("graph TD\n  accTitle: Login flow\n  accDescr: How a user signs in\n  A --> B");

		diagram.Accessibility.Title.Should().Be("Login flow");
		diagram.Accessibility.Description.Should().Be("How a user signs in");
		diagram.Model.Should().BeOfType<MermaidGraph>().Subject.Nodes.Should().HaveCount(2);
	}

	[Test]
	public void Throws_on_empty_input()
	{
		var act = () => MermaidParser.Parse("  \n%% only a comment\n");

		act.Should().Throw<MermaidParseException>();
	}

	[Test]
	public void Enforces_input_length_limit()
	{
		var limits = new ResourceLimits { MaxInputLength = 10 };

		var act = () => MermaidParser.Parse("graph TD\n  A --> B", limits);

		act.Should().Throw<MermaidResourceLimitException>();
	}

	[Test]
	public void Enforces_element_limit()
	{
		var limits = new ResourceLimits { MaxElements = 2 };

		var act = () => MermaidParser.Parse("graph TD\n  A --> B\n  B --> C", limits);

		act.Should().Throw<MermaidResourceLimitException>();
	}

	[Test]
	public void Honours_cancellation()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		var act = () => MermaidParser.Parse("graph TD\n  A --> B", cancellationToken: cts.Token);

		act.Should().Throw<OperationCanceledException>();
	}

	[Test]
	public void Parses_numbers_independently_of_current_culture()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.CreateSpecificCulture("de-DE");
		try
		{
			var diagram = MermaidParser.Parse("pie\n  \"A\" : 1.5\n  \"B\" : 2.25");

			diagram.Model.Should().BeOfType<PieChart>().Subject.Slices.Select(s => s.Value).Should().Equal(1.5, 2.25);
			CultureInfo.CurrentCulture.Name.Should().Be("de-DE");
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[Test]
	[MethodDataSource(nameof(ExampleSlugs))]
	public void Parses_every_example_that_renders(string slug)
	{
		var source = DiagramExamples.All.First(d => d.Slug == slug).Source;
		_ = MermaidRenderer.RenderSvg(source);

		var diagram = MermaidParser.Parse(source);

		diagram.Model.Should().NotBeNull();
		diagram.Model.GetType().Name.Should().Be(ExpectedModelTypeName(diagram.Type));
	}

	private static string ExpectedModelTypeName(DiagramType type) => type switch
	{
		DiagramType.Flowchart or DiagramType.State => nameof(MermaidGraph),
		DiagramType.Pie => nameof(PieChart),
		DiagramType.Quadrant => nameof(QuadrantChart),
		DiagramType.Radar => nameof(RadarChart),
		DiagramType.XyChart => nameof(XyChart),
		DiagramType.GitGraph => nameof(GitGraph),
		DiagramType.Er => nameof(ErDiagram),
		DiagramType.C4 => nameof(C4Diagram),
		_ => type + "Diagram",
	};
}
