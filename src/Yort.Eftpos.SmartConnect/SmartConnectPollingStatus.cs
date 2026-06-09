using System;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// A progress report emitted while a transaction is being polled, for UI feedback. Carries no persistence
/// or outcome responsibility — the terminal outcome is the returned <see cref="SmartConnectTransactionResult"/>.
/// </summary>
public sealed class SmartConnectPollingStatus
{
	/// <summary>The current polling state.</summary>
	public SmartConnectPollingState State { get; init; }

	/// <summary>An optional human-readable message suitable for display. May be <see langword="null"/>.</summary>
	public string? Message { get; init; }

	/// <summary>
	/// The underlying exception, populated only when <see cref="State"/> is
	/// <see cref="SmartConnectPollingState.NetworkError"/>. Detailed diagnostics belong in logs, not the UI.
	/// </summary>
	public Exception? Error { get; init; }
}
