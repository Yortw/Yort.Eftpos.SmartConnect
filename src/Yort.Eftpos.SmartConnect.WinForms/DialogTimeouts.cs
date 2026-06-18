using System;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Converts an auto-close <see cref="TimeSpan"/> to a valid <c>Timer.Interval</c> in
/// milliseconds. The WinForms timer rejects intervals &lt;= 0 and an int cast overflows for very large
/// spans, so this is the single guarded conversion the dialog uses.</summary>
internal static class DialogTimeouts
{
	/// <summary>Returns the millisecond interval for the given auto-close duration.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="autoCloseAfter"/> is not positive.</exception>
	public static int ToIntervalMs(TimeSpan autoCloseAfter)
	{
		if (autoCloseAfter <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(autoCloseAfter), "The auto-close duration must be positive.");
		}

		var ms = autoCloseAfter.TotalMilliseconds;
		if (ms >= int.MaxValue)
		{
			return int.MaxValue;
		}

		return (int)ms;
	}
}
