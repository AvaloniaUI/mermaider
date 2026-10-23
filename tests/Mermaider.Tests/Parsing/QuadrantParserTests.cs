using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class QuadrantParserTests
{
	[Test]
	public void Parses_title()
	{
		var lines = new[]
		{
			"quadrantChart",
			"title Priority Matrix",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Title.Should().Be("Priority Matrix");
	}

	[Test]
	public void Parses_title_on_header_line()
	{
		var lines = new[]
		{
			"quadrantChart title Features by effort (compact)",
			"x-axis Low effort --> High effort",
			"y-axis Low glory --> High glory",
			"Mermaid: [0.8, 0.9]",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Title.Should().Be("Features by effort (compact)");
		chart.XAxisLeft.Should().Be("Low effort");
		chart.XAxisRight.Should().Be("High effort");
		chart.Points.Should().HaveCount(1);
		chart.Points[0].Label.Should().Be("Mermaid");
	}

	[Test]
	public void Body_title_overrides_header_title()
	{
		var lines = new[]
		{
			"quadrantChart title Header Title",
			"title Body Title",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Title.Should().Be("Body Title");
	}

	[Test]
	public void Parses_x_axis_with_both_labels()
	{
		var lines = new[]
		{
			"quadrantChart",
			"x-axis Low Effort --> High Effort",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.XAxisLeft.Should().Be("Low Effort");
		chart.XAxisRight.Should().Be("High Effort");
	}

	[Test]
	public void Parses_x_axis_with_left_only()
	{
		var lines = new[]
		{
			"quadrantChart",
			"x-axis Effort",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.XAxisLeft.Should().Be("Effort");
		chart.XAxisRight.Should().BeNull();
	}

	[Test]
	public void Parses_y_axis()
	{
		var lines = new[]
		{
			"quadrantChart",
			"y-axis Low Impact --> High Impact",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.YAxisBottom.Should().Be("Low Impact");
		chart.YAxisTop.Should().Be("High Impact");
	}

	[Test]
	public void Parses_quadrant_labels()
	{
		var lines = new[]
		{
			"quadrantChart",
			"quadrant-1 Do First",
			"quadrant-2 Schedule",
			"quadrant-3 Delegate",
			"quadrant-4 Eliminate",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Quadrant1.Should().Be("Do First");
		chart.Quadrant2.Should().Be("Schedule");
		chart.Quadrant3.Should().Be("Delegate");
		chart.Quadrant4.Should().Be("Eliminate");
	}

	[Test]
	public void Parses_points()
	{
		var lines = new[]
		{
			"quadrantChart",
			"Feature A: [0.8, 0.9]",
			"Feature B: [0.3, 0.2]",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Points.Should().HaveCount(2);
		chart.Points[0].Label.Should().Be("Feature A");
		chart.Points[0].X.Should().BeApproximately(0.8, 0.001);
		chart.Points[0].Y.Should().BeApproximately(0.9, 0.001);
		chart.Points[1].Label.Should().Be("Feature B");
		chart.Points[1].X.Should().BeApproximately(0.3, 0.001);
	}

	[Test]
	public void Parses_point_styles_and_classes()
	{
		var lines = new[]
		{
			"quadrantChart",
			"Campaign A: [0.9, 0.0] radius: 12",
			"Campaign B:::class1: [0.8, 0.1] color: #ff3300, radius: 10",
			"Campaign D: [0.6, 0.3] radius: 15, stroke-color: #00ff0f, stroke-width: 5px ,color: #ff33f0",
			"Campaign E:::class2: [0.5, 0.4]",
			"classDef class1 color: #109060",
			"classDef class2 color: #908342, radius : 10, stroke-color: #310085, stroke-width: 10px",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Points.Select(p => p.Label).Should().Equal("Campaign A", "Campaign B", "Campaign D", "Campaign E");
		chart.Points[0].Style.Should().Be(new QuadrantPointStyle(Radius: 12));
		chart.Points[1].Style.Should().Be(new QuadrantPointStyle("#ff3300", 10));
		chart.Points[2].Style.Should().Be(new QuadrantPointStyle("#ff33f0", 15, "#00ff0f", "5px"));
		chart.Points[3].Style.Should().Be(new QuadrantPointStyle("#908342", 10, "#310085", "10px"));
		chart.Points[3].X.Should().BeApproximately(0.5, 0.001);
	}

	[Test]
	public void Quoted_labels_lose_their_quotes()
	{
		var lines = new[]
		{
			"quadrantChart",
			"title \"Effort and impact\"",
			"x-axis Urgent --> \"Not Urgent\"",
			"y-axis Not Important --> \"Important ❤\"",
			"quadrant-1 \"Plan\"",
			"\"Point, quoted\": [0.2, 0.3]",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Title.Should().Be("Effort and impact");
		chart.XAxisRight.Should().Be("Not Urgent");
		chart.YAxisTop.Should().Be("Important ❤");
		chart.Quadrant1.Should().Be("Plan");
		chart.Points[0].Label.Should().Be("Point, quoted");
	}

	[Test]
	public void Clamps_point_values_to_0_1()
	{
		var lines = new[]
		{
			"quadrantChart",
			"Over: [1.5, -0.2]",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Points[0].X.Should().Be(1.0);
		chart.Points[0].Y.Should().Be(0.0);
	}

	[Test]
	public void Handles_empty_chart()
	{
		var lines = new[]
		{
			"quadrantChart",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Points.Should().HaveCount(0);
		chart.Title.Should().BeNull();
	}
}
