using System.Collections.Generic;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Factory for the pre-populated, overridable caption maps used by the dialogs. Each call
/// returns a fresh dictionary so callers can mutate their own copy without affecting others.</summary>
internal static class DefaultCaptions
{
	/// <summary>Default progress captions per polling state (used when the library reports no message).</summary>
	public static Dictionary<SmartConnectPollingState, string> CreateStateCaptions()
	{
		return new Dictionary<SmartConnectPollingState, string>
		{
			// Operation-neutral: this dialog serves non-financial operations (status, logon, settlement) as well
			// as payments, so the default busy caption must not say "payment". Consumers override per operation
			// via the public StateCaptions dictionary (e.g. a payment path can set "Processing payment…").
			[SmartConnectPollingState.Polling] = "Processing…",
			[SmartConnectPollingState.Delayed] = "Waiting for pinpad — it may be offline…",
			[SmartConnectPollingState.BackingOff] = "Busy, retrying…",
			[SmartConnectPollingState.NetworkError] = "Network problem, retrying…"
		};
	}

	/// <summary>Default outcome captions for financial transaction statuses.</summary>
	public static Dictionary<SmartConnectTransactionStatus, string> CreateTransactionResultCaptions()
	{
		return new Dictionary<SmartConnectTransactionStatus, string>
		{
			[SmartConnectTransactionStatus.Accepted] = "Approved",
			[SmartConnectTransactionStatus.Declined] = "Declined",
			[SmartConnectTransactionStatus.Cancelled] = "Cancelled",
			[SmartConnectTransactionStatus.DeviceOffline] = "Terminal offline",
			[SmartConnectTransactionStatus.Failed] = "Failed",
			[SmartConnectTransactionStatus.Unknown] = "Outcome unknown — reconcile"
		};
	}

	/// <summary>Default outcome captions for non-financial operation statuses.</summary>
	public static Dictionary<SmartConnectOperationStatus, string> CreateOperationResultCaptions()
	{
		return new Dictionary<SmartConnectOperationStatus, string>
		{
			[SmartConnectOperationStatus.Succeeded] = "Completed",
			[SmartConnectOperationStatus.Failed] = "Failed",
			[SmartConnectOperationStatus.Unknown] = "Outcome unknown — verify"
		};
	}
}
