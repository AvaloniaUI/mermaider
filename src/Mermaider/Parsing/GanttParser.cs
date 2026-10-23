using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class GanttParser
{
	private const int TimeoutMs = 2000;
	/// <summary>Guard against hostile durations that overflow TimeSpan/DateTime (~200 years).</summary>
	private const double MaxDurationDays = 365.0 * 200;
	/// <summary>Mermaid-style cap when walking excluded days.</summary>
	private const int MaxExcludeIterations = 10_000;

	/// <summary>Stable synthetic origin when a chart has no absolute dates (never wall-clock).</summary>
	private static readonly DateTime SyntheticOrigin = new(2020, 1, 1);

	/// <summary>Start of Unix time for <c>dateFormat X</c> (seconds) and <c>x</c> (milliseconds), kind unspecified.</summary>
	private static readonly DateTime UnixEpoch = new(1970, 1, 1);

	// Sentinel .NET formats for the Unix time dateFormats, which no custom format string expresses.
	private const string UnixSecondsFormat = "\0X";
	private const string UnixMillisecondsFormat = "\0x";

	// Supported mermaid dateFormat tokens (dayjs-style) we map to .NET custom format:
	// YYYY/YY, MM/M, DD/D, HH/H, mm, ss, and the whole-format Unix times X and x. Unsupported tokens
	// (Do, DDD, Q, …) are left as-is and typically fail TryParseExact → treated as non-dates.

	[GeneratedRegex(@"^gantt(?:\s+title\s+(.+))?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^dateFormat\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DateFormatPattern();

	[GeneratedRegex(@"^section\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SectionPattern();

	[GeneratedRegex(@"^(.+?)\s*:\s*(.*)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TaskPattern();

	/// <summary>
	/// Mermaid duration units are case-sensitive: m=minutes, M=months, y=years, ms=milliseconds.
	/// Do not use IgnoreCase — that would map <c>1M</c> to minutes.
	/// </summary>
	[GeneratedRegex(@"^\d+(?:\.\d+)?(?:ms|[smhdwyM])$", RegexOptions.None, TimeoutMs)]
	private static partial Regex DurationPattern();

	[GeneratedRegex(@"^after\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex AfterPattern();

	[GeneratedRegex(@"^until\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex UntilPattern();

	[GeneratedRegex(@"^excludes\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ExcludesPattern();

	[GeneratedRegex(@"^(axisFormat|tickInterval|todayMarker|weekday|weekend)\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SettingPattern();

	internal static GanttDiagram Parse(string[] lines)
	{
		try
		{
			return ParseCore(lines);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException(
				$"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.",
				ex);
		}
		catch (OverflowException ex)
		{
			throw new MermaidParseException("Gantt duration or date arithmetic overflowed.", ex);
		}
		catch (ArgumentOutOfRangeException ex)
		{
			throw new MermaidParseException("Gantt duration or date is out of range.", ex);
		}
		catch (FormatException ex)
		{
			throw new MermaidParseException($"Invalid gantt dateFormat or date value: {ex.Message}", ex);
		}
	}

	// Every statement is read before any task is resolved: `after` and `until` may name a task defined further down,
	// and excludes declared after a task still apply to it, as in mermaid.js.
	private static GanttDiagram ParseCore(string[] lines)
	{
		string? title = null;
		var dateFormat = "YYYY-MM-DD";
		var netDateFormat = ToNetDateFormat(dateFormat);
		string? axisFormat = null, tickInterval = null, weekday = null, todayMarker = null;
		var excludes = new Excludes();
		var sections = new List<(string? Name, List<int> Tasks)>();
		var raws = new List<RawTask>();

		var headerMatch = HeaderPattern().Match(lines[0]);
		if (headerMatch.Success && headerMatch.Groups[1].Success)
			title = headerMatch.Groups[1].Value.Trim();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			// Gantt click/interaction directives can execute arbitrary JavaScript callbacks.
			// They are disabled in mermaid.js strict mode for the same reason — reject early.
			if (line.TrimStart().StartsWith("click ", StringComparison.OrdinalIgnoreCase))
				throw new MermaidParseException(
					"Gantt 'click' interactivity is not supported: it can execute arbitrary JavaScript " +
					"callbacks. Remove click directives to render the diagram.");

			var excludesMatch = ExcludesPattern().Match(line);
			if (excludesMatch.Success)
			{
				foreach (var token in excludesMatch.Groups[1].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
					excludes.Add(token, netDateFormat);
				continue;
			}

			var settingMatch = SettingPattern().Match(line);
			if (settingMatch.Success)
			{
				var value = settingMatch.Groups[2].Value.Trim();
				switch (settingMatch.Groups[1].Value.ToLowerInvariant())
				{
					case "axisformat":
						axisFormat = value;
						break;
					case "tickinterval":
						tickInterval = value;
						break;
					case "todaymarker":
						todayMarker = value;
						break;
					case "weekday":
						weekday = value;
						break;
					case "weekend":
						excludes.SetWeekend(value);
						break;
				}

				continue;
			}

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var dfMatch = DateFormatPattern().Match(line);
			if (dfMatch.Success)
			{
				dateFormat = dfMatch.Groups[1].Value.Trim();
				netDateFormat = dateFormat switch
				{
					"X" => UnixSecondsFormat,
					"x" => UnixMillisecondsFormat,
					_ => ToNetDateFormat(dateFormat),
				};

				// Fail early on format strings that .NET rejects (wrap as MermaidParseException via Parse).
				if (netDateFormat is not (UnixSecondsFormat or UnixMillisecondsFormat))
					ValidateNetDateFormat(netDateFormat);
				continue;
			}

			var sectionMatch = SectionPattern().Match(line);
			if (sectionMatch.Success)
			{
				sections.Add((sectionMatch.Groups[1].Value.Trim(), []));
				continue;
			}

			var taskMatch = TaskPattern().Match(line);
			if (!taskMatch.Success)
				continue;

			raws.Add(ParseRawTask(taskMatch.Groups[1].Value.Trim(), taskMatch.Groups[2].Value.Trim(), netDateFormat));
			if (sections.Count == 0)
				sections.Add((null, []));
			sections[^1].Tasks.Add(raws.Count - 1);
		}

		var resolver = new TaskResolver(raws, excludes);
		var result = new List<GanttSection>(sections.Count);
		foreach (var (name, indices) in sections)
		{
			if (indices.Count == 0)
				continue;

			var tasks = new List<GanttTask>(indices.Count);
			foreach (var index in indices)
				tasks.Add(resolver.Resolve(index));
			result.Add(new GanttSection(name, tasks));
		}

		if (result.Count == 0)
			result.Add(new GanttSection(null, []));

		return new GanttDiagram
		{
			Title = title,
			DateFormat = dateFormat,
			AxisFormat = axisFormat,
			TickInterval = tickInterval,
			Weekday = weekday,
			TodayMarker = todayMarker,
			Sections = result,
		};
	}

	/// <summary>
	/// Parsed duration with case-sensitive unit. Months/years use calendar arithmetic at apply time.
	/// </summary>
	private readonly record struct ParsedDuration(double Number, string Unit)
	{
		public static ParsedDuration Days(double n) => new(n, "d");
		public static ParsedDuration Zero => new(0, "d");

		public DateTime AddTo(DateTime start)
		{
			// Bound extreme magnitudes before arithmetic.
			if (double.IsNaN(Number) || double.IsInfinity(Number) || Number < 0)
				throw new MermaidParseException($"Invalid gantt duration value: {Number}{Unit}");

			try
			{
				return Unit switch
				{
					"ms" => start.AddMilliseconds(Number),
					"s" => start.AddSeconds(Number),
					"m" => start.AddMinutes(Number),
					"h" => start.AddHours(Number),
					"d" => start.AddDays(Number),
					"w" => start.AddDays(Number * 7),
					"M" => AddCalendarMonths(start, Number),
					"y" => AddCalendarYears(start, Number),
					_ => start.AddDays(Number),
				};
			}
			catch (ArgumentOutOfRangeException ex)
			{
				throw new MermaidParseException($"Gantt duration {Number}{Unit} is out of range.", ex);
			}
			catch (OverflowException ex)
			{
				throw new MermaidParseException($"Gantt duration {Number}{Unit} overflowed.", ex);
			}
		}

		/// <summary>Approximate span for overflow pre-checks (months ≈ 30d, years ≈ 365d).</summary>
		public double ApproximateDays =>
			Unit switch
			{
				"ms" => Number / 86_400_000d,
				"s" => Number / 86_400d,
				"m" => Number / 1_440d,
				"h" => Number / 24d,
				"d" => Number,
				"w" => Number * 7,
				"M" => Number * 30,
				"y" => Number * 365,
				_ => Number,
			};
	}

	private static DateTime AddCalendarMonths(DateTime start, double number)
	{
		var whole = (int)Math.Truncate(number);
		var frac = number - whole;
		var result = start.AddMonths(whole);
		if (frac != 0)
			result = result.AddDays(frac * 30);
		return result;
	}

	private static DateTime AddCalendarYears(DateTime start, double number)
	{
		var whole = (int)Math.Truncate(number);
		var frac = number - whole;
		var result = start.AddYears(whole);
		if (frac != 0)
			result = result.AddDays(frac * 365);
		return result;
	}

	private sealed class RawTask
	{
		public required string Name { get; init; }
		public string? Id { get; init; }
		public GanttTaskTags Tags { get; init; }
		public DateTime? StartDate { get; init; }
		public string[]? AfterIds { get; init; }
		/// <summary>Tasks whose earliest start ends this task (<c>until a b</c>).</summary>
		public string[]? UntilIds { get; init; }
		public DateTime? EndDate { get; init; }
		public ParsedDuration? Duration { get; init; }
		/// <summary>True when end was an explicit calendar date (excludes do not extend end).</summary>
		public bool ManualEndTime { get; init; }
	}

	/// <summary>
	/// Mermaid metadata after tags (see ganttDb compileData):
	/// 1 token → end (date or duration), start = previous end;
	/// 2 tokens → start + end (no id);
	/// 3 tokens → id + start + end.
	/// Dialect extension: a leading non-date/non-duration/non-after token may be consumed as id
	/// even when fewer than three slots remain (common charts use <c>id, end</c>).
	/// When only one slot remains after optional id, a date is always END (never start).
	/// </summary>
	private static RawTask ParseRawTask(string name, string meta, string netDateFormat)
	{
		var tags = GanttTaskTags.None;
		string? id = null;
		DateTime? startDate = null;
		string[]? afterIds = null;
		string[]? untilIds = null;
		DateTime? endDate = null;
		ParsedDuration? duration = null;
		var manualEndTime = false;

		if (meta.Length == 0)
		{
			return new RawTask
			{
				Name = name,
				Duration = ParsedDuration.Days(1),
			};
		}

		var tokens = SplitMeta(meta);
		var i = 0;

		while (i < tokens.Count && TryParseTag(tokens[i], out var tag))
		{
			tags |= tag;
			i++;
		}

		// Optional id: first remaining token that is not after / duration / date
		// (greedy id — locked dialect for charts like "a1, 2026-07-07, 1d" and "id, endDate").
		if (i < tokens.Count &&
			!IsAfter(tokens[i]) &&
			!IsUntil(tokens[i]) &&
			!IsDuration(tokens[i]) &&
			ParseDate(tokens[i], netDateFormat) is null)
		{
			id = tokens[i];
			i++;
		}

		var remaining = tokens.Count - i;

		if (remaining == 1)
		{
			// Single slot: end date | duration | (after alone → start, default end)
			var t = tokens[i];
			if (IsAfter(t))
			{
				afterIds = ParseAfterIds(t);
			}
			else if (IsUntil(t))
			{
				untilIds = ParseIds(UntilPattern(), t);
			}
			else if (IsDuration(t))
			{
				duration = ParseDuration(t);
			}
			else if (ParseDate(t, netDateFormat) is { } ed)
			{
				endDate = ed;
				manualEndTime = true;
			}
		}
		else if (remaining >= 2)
		{
			// Start: after … | date  (unknown tokens left for end path / ignored)
			var t0 = tokens[i];
			if (IsAfter(t0))
			{
				afterIds = ParseAfterIds(t0);
				i++;
			}
			else if (ParseDate(t0, netDateFormat) is { } sd)
			{
				startDate = sd;
				i++;
			}
			else if (!IsDuration(t0))
			{
				// Not a recognizable start — skip so a following duration can still bind as end
				// only if something else provides start via previous/after. Avoid swallowing duration.
				// If it's an id-like leftover after we already took id, ignore.
				i++;
			}

			// End: duration | date
			if (i < tokens.Count)
			{
				var t1 = tokens[i];
				if (IsUntil(t1))
				{
					untilIds = ParseIds(UntilPattern(), t1);
				}
				else if (IsDuration(t1))
				{
					duration = ParseDuration(t1);
				}
				else if (ParseDate(t1, netDateFormat) is { } ed)
				{
					endDate = ed;
					manualEndTime = true;
				}
			}
		}

		// Defaults when end is unspecified
		if (duration is null && endDate is null && untilIds is null)
		{
			duration = (tags & GanttTaskTags.Milestone) != 0
				? ParsedDuration.Zero
				: ParsedDuration.Days(1);
		}

		return new RawTask
		{
			Name = name,
			Id = id,
			Tags = tags,
			StartDate = startDate,
			AfterIds = afterIds,
			UntilIds = untilIds,
			EndDate = endDate,
			Duration = duration,
			ManualEndTime = manualEndTime,
		};
	}

	// Resolves tasks on demand, so a task may depend on one defined later; a dependency cycle is a parse error.
	private sealed class TaskResolver
	{
		private readonly List<RawTask> _raws;
		private readonly Excludes _excludes;
		private readonly GanttTask?[] _resolved;
		private readonly bool[] _visiting;
		private readonly Dictionary<string, int> _byId = new(StringComparer.OrdinalIgnoreCase);
		private readonly DateTime _origin;

		public TaskResolver(List<RawTask> raws, Excludes excludes)
		{
			_raws = raws;
			_excludes = excludes;
			_resolved = new GanttTask?[raws.Count];
			_visiting = new bool[raws.Count];
			DateTime? origin = null;
			for (var i = 0; i < raws.Count; i++)
			{
				if (raws[i].Id is { Length: > 0 } id)
					_byId[id] = i;
				origin ??= raws[i].StartDate ?? raws[i].EndDate;
			}

			_origin = origin ?? SyntheticOrigin;
		}

		public GanttTask Resolve(int index)
		{
			if (_resolved[index] is { } done)
				return done;
			if (_visiting[index])
				throw new MermaidParseException($"Gantt task '{_raws[index].Name}' depends on itself through after or until.");

			_visiting[index] = true;
			var raw = _raws[index];
			var start = ResolveStart(raw, index);
			var (end, manual) = ResolveEnd(raw, start);
			if (!manual && _excludes.Any && end > start)
				end = ApplyExcludes(start, end, _excludes);
			if (end < start)
				end = start;

			var task = new GanttTask(raw.Name, raw.Id, start, end, raw.Tags);
			_resolved[index] = task;
			_visiting[index] = false;
			return task;
		}

		// A start that is not given follows the previous task's end, resolved only then: a task with its own start must
		// not depend on its predecessor, which may itself end `until` this task.
		private DateTime ResolveStart(RawTask raw, int index)
		{
			if (raw.AfterIds is { Length: > 0 })
			{
				var maxEnd = DateTime.MinValue;
				var found = false;
				foreach (var refId in raw.AfterIds)
				{
					if (_byId.TryGetValue(refId, out var dep) && Resolve(dep).End is var depEnd && depEnd > maxEnd)
					{
						maxEnd = depEnd;
						found = true;
					}
				}

				return found ? maxEnd : PreviousEnd(index);
			}

			if (raw.StartDate is { } sd)
				return sd;

			return PreviousEnd(index);
		}

		private DateTime PreviousEnd(int index) => index > 0 ? Resolve(index - 1).End : _origin;

		// The end, and whether it was given outright (a date or `until`), which excludes do not stretch.
		private (DateTime End, bool Manual) ResolveEnd(RawTask raw, DateTime start)
		{
			if (raw.UntilIds is { Length: > 0 })
			{
				var minStart = DateTime.MaxValue;
				var found = false;
				foreach (var refId in raw.UntilIds)
				{
					if (_byId.TryGetValue(refId, out var dep) && Resolve(dep).Start is var depStart && depStart < minStart)
					{
						minStart = depStart;
						found = true;
					}
				}

				return found ? (minStart, true) : (start.AddDays(1), false);
			}

			if (raw.Duration is { } dur)
				return (dur.AddTo(start), false);
			if (raw.EndDate is { } ed)
				return (ed, raw.ManualEndTime);
			if ((raw.Tags & GanttTaskTags.Milestone) != 0)
				return (start, true);
			return (start.AddDays(1), false);
		}
	}

	// What `excludes` and `weekend` take out of the calendar: weekends (Saturday and Sunday, or Friday and Saturday
	// after `weekend friday`), named weekdays, and dates in the chart's dateFormat.
	private sealed class Excludes
	{
		private readonly HashSet<DayOfWeek> _days = [];
		private readonly HashSet<DateTime> _dates = [];
		private bool _weekends;
		private DayOfWeek _weekendStart = DayOfWeek.Saturday;

		public bool Any => _weekends || _days.Count > 0 || _dates.Count > 0;

		public void Add(string token, string netDateFormat)
		{
			if (token.Equals("weekends", StringComparison.OrdinalIgnoreCase))
				_weekends = true;
			else if (Enum.TryParse<DayOfWeek>(token, ignoreCase: true, out var day) && !int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
				_ = _days.Add(day);
			else if (ParseDate(token, netDateFormat) is { } date)
				_ = _dates.Add(date.Date);
		}

		public void SetWeekend(string value)
		{
			if (value.Equals("friday", StringComparison.OrdinalIgnoreCase))
				_weekendStart = DayOfWeek.Friday;
			else if (value.Equals("saturday", StringComparison.OrdinalIgnoreCase))
				_weekendStart = DayOfWeek.Saturday;
		}

		public bool IsExcluded(DateTime d)
			=> (_weekends && (d.DayOfWeek == _weekendStart || d.DayOfWeek == (DayOfWeek)(((int)_weekendStart + 1) % 7)))
				|| _days.Contains(d.DayOfWeek)
				|| _dates.Contains(d.Date);
	}

	/// <summary>
	/// Mermaid fixTaskDates: for each day in (start, end], if it is excluded, push end +1 day.
	/// </summary>
	private static DateTime ApplyExcludes(DateTime start, DateTime end, Excludes excludes)
	{
		var cursor = start.Date.AddDays(1);
		var endCursor = end;
		var iterations = 0;
		while (cursor <= endCursor)
		{
			if (iterations++ > MaxExcludeIterations)
				throw new MermaidParseException(
					"Failed to resolve gantt task end after excludes (iteration cap).");

			if (excludes.IsExcluded(cursor))
				endCursor = endCursor.AddDays(1);

			cursor = cursor.AddDays(1);
		}
		// Preserve time-of-day from original end when both are date-aligned; otherwise keep computed end.
		if (end.TimeOfDay != TimeSpan.Zero && endCursor.TimeOfDay == TimeSpan.Zero)
			endCursor = endCursor.Date + end.TimeOfDay;
		return endCursor;
	}

	private static List<string> SplitMeta(string meta)
	{
		var parts = meta.Split(',');
		var tokens = new List<string>(parts.Length);
		foreach (var p in parts)
		{
			var t = p.Trim();
			if (t.Length > 0)
				tokens.Add(t);
		}
		return tokens;
	}

	private static bool TryParseTag(string token, out GanttTaskTags tag)
	{
		if (token.Equals("done", StringComparison.OrdinalIgnoreCase))
		{
			tag = GanttTaskTags.Done;
			return true;
		}
		if (token.Equals("active", StringComparison.OrdinalIgnoreCase))
		{
			tag = GanttTaskTags.Active;
			return true;
		}
		if (token.Equals("crit", StringComparison.OrdinalIgnoreCase))
		{
			tag = GanttTaskTags.Crit;
			return true;
		}
		if (token.Equals("milestone", StringComparison.OrdinalIgnoreCase))
		{
			tag = GanttTaskTags.Milestone;
			return true;
		}
		if (token.Equals("vert", StringComparison.OrdinalIgnoreCase))
		{
			tag = GanttTaskTags.Vert;
			return true;
		}
		tag = GanttTaskTags.None;
		return false;
	}

	private static bool IsAfter(string token) =>
		token.StartsWith("after", StringComparison.OrdinalIgnoreCase) &&
		(token.Length == 5 || char.IsWhiteSpace(token[5]));

	private static bool IsUntil(string token) =>
		token.StartsWith("until", StringComparison.OrdinalIgnoreCase) &&
		token.Length > 5 && char.IsWhiteSpace(token[5]);

	private static string[] ParseAfterIds(string token) => ParseIds(AfterPattern(), token);

	private static string[] ParseIds(Regex pattern, string token)
	{
		var m = pattern.Match(token);
		if (!m.Success)
			return [];
		return m.Groups[1].Value
			.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	private static bool IsDuration(string token) => DurationPattern().IsMatch(token);

	private static ParsedDuration ParseDuration(string token)
	{
		string unit;
		int numberLen;
		if (token.EndsWith("ms", StringComparison.Ordinal))
		{
			unit = "ms";
			numberLen = token.Length - 2;
		}
		else
		{
			// Preserve original case: 'M' months vs 'm' minutes.
			unit = token[^1].ToString();
			numberLen = token.Length - 1;
		}

		if (!double.TryParse(token.AsSpan(0, numberLen), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
			throw new MermaidParseException($"Invalid gantt duration: {token}");

		var dur = new ParsedDuration(number, unit);
		if (dur.ApproximateDays > MaxDurationDays)
			throw new MermaidParseException($"Gantt duration too large: {token}");

		return dur;
	}

	/// <summary>
	/// Map common dayjs/Mermaid tokens to .NET custom date format.
	/// Order matters: longer tokens first (YYYY before YY, DD before D, HH before H, mm before m).
	/// Known support: YYYY-MM-DD, YY-MM-DD, DD-MM-YYYY, YYYY/MM/DD, and datetime with HH:mm:ss.
	/// </summary>
	private static string ToNetDateFormat(string mermaid)
	{
		// Use placeholders so shorter replacements cannot corrupt longer ones.
		const string y4 = "\u0001";
		const string y2 = "\u0002";
		const string d2 = "\u0003";
		const string d1 = "\u0004";
		const string h2 = "\u0005";
		const string h1 = "\u0006";
		const string min2 = "\u0007";
		const string sec2 = "\u0008";

		var s = mermaid
			.Replace("YYYY", y4, StringComparison.Ordinal)
			.Replace("YY", y2, StringComparison.Ordinal)
			.Replace("DD", d2, StringComparison.Ordinal)
			.Replace("D", d1, StringComparison.Ordinal)
			.Replace("HH", h2, StringComparison.Ordinal)
			.Replace("H", h1, StringComparison.Ordinal)
			.Replace("mm", min2, StringComparison.Ordinal)
			.Replace("ss", sec2, StringComparison.Ordinal);

		// Month tokens MM/M are already .NET-compatible (case-sensitive).
		return s
			.Replace(y4, "yyyy", StringComparison.Ordinal)
			.Replace(y2, "yy", StringComparison.Ordinal)
			.Replace(d2, "dd", StringComparison.Ordinal)
			.Replace(d1, "d", StringComparison.Ordinal)
			.Replace(h2, "HH", StringComparison.Ordinal)
			.Replace(h1, "H", StringComparison.Ordinal)
			.Replace(min2, "mm", StringComparison.Ordinal)
			.Replace(sec2, "ss", StringComparison.Ordinal);
	}

	private static void ValidateNetDateFormat(string netFormat)
	{
		// Probe with a fixed instant — invalid custom format strings throw FormatException.
		_ = new DateTime(2020, 1, 2, 3, 4, 5).ToString(netFormat, CultureInfo.InvariantCulture);
	}

	private static DateTime? ParseDate(string text, string netFormat)
	{
		if (netFormat is UnixSecondsFormat or UnixMillisecondsFormat)
		{
			if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
				return null;
			return netFormat == UnixSecondsFormat ? UnixEpoch.AddSeconds(value) : UnixEpoch.AddMilliseconds(value);
		}

		// Unspecified kind throughout — never wall-clock, never forced UTC.
		// Strict parse under active dateFormat only (no culture-loose TryParse fallback —
		// that mis-tokenized ids/metadata under non-ISO formats).
		try
		{
			if (DateTime.TryParseExact(
					text, netFormat, CultureInfo.InvariantCulture,
					DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault,
					out var dt))
			{
				return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
			}
		}
		catch (FormatException ex)
		{
			throw new MermaidParseException($"Invalid gantt dateFormat '{netFormat}'.", ex);
		}

		return null;
	}
}
