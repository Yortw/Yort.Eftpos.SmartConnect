using System;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// A transaction that has not yet reached a terminal state, as recorded by an
/// <see cref="ISmartConnectTransactionState"/> store. Returned during crash recovery so the caller can
/// resume polling.
/// </summary>
/// <remarks>
/// <see cref="PollingUrl"/> contains an embedded bearer token (<c>merchantAccessToken</c>) and is therefore
/// sensitive — it must be persisted to enable recovery, but never logged and stored only with restricted access.
/// </remarks>
public sealed class PendingTransaction
{
	/// <summary>The caller-supplied reference that correlates this record across send and recovery.</summary>
	public string ClientTransactionRef { get; set; } = string.Empty;

	/// <summary>
	/// The polling URL returned by the initial POST, used to resume polling. <see langword="null"/> when only a
	/// pre-POST sentinel exists (the crash window the design calls out). Treat as a credential — never log it.
	/// </summary>
	public string? PollingUrl { get; set; }

	/// <summary>The server-issued transaction id, if the initial POST response was received.</summary>
	public string? TransactionId { get; set; }

	/// <summary>When the attempt was first recorded (sentinel write), in UTC.</summary>
	public DateTimeOffset CreatedAt { get; set; }
}
