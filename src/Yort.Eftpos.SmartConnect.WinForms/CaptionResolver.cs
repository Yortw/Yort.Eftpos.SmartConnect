using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Resolves the caption to display for a progress report: the library's own message when it
/// supplies one, otherwise the configured per-state default. Pure — no UI dependency.</summary>
internal static class CaptionResolver
{
	/// <summary>Returns <see cref="SmartConnectPollingStatus.Message"/> when non-blank; otherwise the
	/// caption mapped for the report's <see cref="SmartConnectPollingStatus.State"/>.</summary>
	public static string Resolve(SmartConnectPollingStatus status, IReadOnlyDictionary<SmartConnectPollingState, string> captions)
	{
		// IsNullOrWhiteSpace (F10): a whitespace-only message would otherwise render as a blank caption;
		// the pairing path trims-and-checks too, so treat blank-ish messages consistently as "absent".
		if (!string.IsNullOrWhiteSpace(status.Message))
		{
			return status.Message!;
		}

		// Defensive (F2): degrade a missing/removed state key to the enum name rather than throwing.
		return captions.TryGetValue(status.State, out var caption) ? caption : status.State.ToString();
	}
}
