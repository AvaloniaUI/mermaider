using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class LabelNormalizerTests
{
	[Test]
	public void Strips_surrounding_quotes()
	{
		LabelNormalizer.NormalizeBrTags("\"Hello\"").Should().Be("Hello");
	}

	[Test]
	public void Converts_br_tags_and_escaped_newlines_to_newlines()
	{
		LabelNormalizer.NormalizeBrTags("a<br>b<BR/>c<br />d\\ne").Should().Be("a\nb\nc\nd\ne");
	}

	[Test]
	public void Strips_unsupported_tags()
	{
		LabelNormalizer.NormalizeBrTags("H<sub>2</sub>O <mark>x</mark>").Should().Be("H2O x");
	}

	[Test]
	public void Converts_markdown_emphasis_to_format_tags()
	{
		LabelNormalizer.NormalizeBrTags("**bold** *italic* ~~gone~~").Should().Be("<b>bold</b> <i>italic</i> <s>gone</s>");
	}
}
