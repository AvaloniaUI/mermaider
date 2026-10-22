using System.Globalization;

namespace Mermaider.Rendering;

/// <summary>
/// Switches the current culture to the invariant culture until disposed, so number parsing and formatting
/// do not depend on the caller's locale.
/// </summary>
internal readonly struct InvariantCultureScope : IDisposable
{
	private readonly CultureInfo _previousCulture;

	private InvariantCultureScope(CultureInfo previousCulture) => _previousCulture = previousCulture;

	internal static InvariantCultureScope Enter()
	{
		var previousCulture = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		return new InvariantCultureScope(previousCulture);
	}

	public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;
}
