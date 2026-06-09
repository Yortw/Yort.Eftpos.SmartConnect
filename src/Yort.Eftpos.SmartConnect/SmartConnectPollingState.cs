namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// Informational progress states reported while a transaction is being polled. Intended for UI feedback
/// (via <see cref="System.IProgress{T}"/>); these carry no persistence or outcome responsibility.
/// </summary>
public enum SmartConnectPollingState
{
	/// <summary>A normal poll — the terminal is working through the transaction.</summary>
	Polling = 0,

	/// <summary>The cloud reported the transaction as delayed; the terminal may be temporarily offline.</summary>
	Delayed,

	/// <summary>The poll was rate-limited (HTTP 429); the client is waiting before retrying.</summary>
	BackingOff,

	/// <summary>A transient transport error occurred during a poll (DNS, connection, timeout); the client will retry.</summary>
	NetworkError
}
