using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Maps a core result status to a <see cref="ResultVisual"/>.</summary>
internal static class ResultVisuals
{
	/// <summary>Resolves the visual for a financial transaction status.</summary>
	public static ResultVisual ForTransaction(SmartConnectTransactionStatus status, IReadOnlyDictionary<SmartConnectTransactionStatus, string> captions)
	{
		ResultSeverity severity;
		if (status == SmartConnectTransactionStatus.Accepted)
		{
			severity = ResultSeverity.Success;
		}
		else if (status == SmartConnectTransactionStatus.Unknown)
		{
			severity = ResultSeverity.Ambiguous;
		}
		else
		{
			// Declined, Cancelled, DeviceOffline, Failed — all non-success.
			severity = ResultSeverity.Negative;
		}

		// Defensive (F2): the caption maps are consumer-mutable. A removed key must degrade to the enum
		// name, never throw KeyNotFoundException on the UI thread while showing an outcome.
		var caption = captions.TryGetValue(status, out var mapped) ? mapped : status.ToString();
		return new ResultVisual(caption, severity, detail: null);
	}

	/// <summary>Resolves the visual for a non-financial operation status. The error message becomes the
	/// detail only for a <see cref="SmartConnectOperationStatus.Failed"/> outcome.</summary>
	public static ResultVisual ForOperation(SmartConnectOperationStatus status, string? errorMessage, IReadOnlyDictionary<SmartConnectOperationStatus, string> captions)
	{
		ResultSeverity severity;
		string? detail = null;
		if (status == SmartConnectOperationStatus.Succeeded)
		{
			severity = ResultSeverity.Success;
		}
		else if (status == SmartConnectOperationStatus.Unknown)
		{
			severity = ResultSeverity.Ambiguous;
		}
		else
		{
			severity = ResultSeverity.Negative;
			detail = errorMessage;
		}

		// Defensive (F2): degrade a missing key to the enum name rather than throwing.
		var caption = captions.TryGetValue(status, out var mapped) ? mapped : status.ToString();
		return new ResultVisual(caption, severity, detail);
	}
}
