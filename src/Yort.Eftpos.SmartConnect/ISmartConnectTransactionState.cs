using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The mandatory transaction-state persistence contract. The client persists at the pre-POST and
/// polling-URL lifecycle points; the consumer records completion after it has durably stored the outcome.
/// Persistence is a dependency, not an optional event — supply an implementation via
/// <see cref="SmartConnectClientConfiguration.StateStore"/>.
/// </summary>
/// <remarks>
/// <para>The lifecycle is: <see cref="SaveTransactionAttemptAsync"/> (before the POST) →
/// <see cref="UpdatePollingDetailsAsync"/> (once the polling URL is received). For a <b>completed</b> outcome
/// delivered by polling (Accepted/Declined/Cancelled and the like) the client does NOT mark completion — it
/// returns the result with the record left pending, and the <b>consumer</b> calls
/// <see cref="UpdateCompletedAsync"/> only AFTER it has durably recorded the outcome (persist-before-complete),
/// then <see cref="RemoveAsync"/> once done. (The client still closes the record itself only on a rejected or
/// never-sent POST, which completes it <c>Failed</c>; a store-refused pre-POST attempt persisted no record at
/// all. Poll exhaustion no longer finalizes — it leaves the record pending, see Decision 13.) "Pending" is the absence of
/// completion. Because recovery can re-poll and re-deliver a still-pending completed transaction, the
/// consumer's outcome persistence MUST be idempotent by <c>clientTransactionRef</c>. A consumer that never
/// calls <see cref="UpdateCompletedAsync"/> leaves the record pending indefinitely, so every recovery pass
/// re-polls it — but re-delivery of the outcome only works while the polling URL's access token is valid
/// (measured at ~15 min from the original POST); past that the re-poll returns
/// <see cref="SmartConnectFailureCause.PollingUrlInvalid"/> and the outcome is manual-reconciliation only, so
/// monitor pending-row age and reconcile stale records. Conversely this is self-healing: if
/// <see cref="UpdateCompletedAsync"/> fails after a successful persist, replay plus idempotent persistence
/// retries the completion.</para>
/// <para>Implementation guidance (ADR Decision 10): (a) make the attempt write reserve capacity comparable
/// to the completed record (the polling URL carries a long token), so passing the pre-POST gate predicts
/// the later update succeeding; (b) retry known-transient errors briefly before throwing (file sharing
/// violations, SQL deadlocks/timeouts) — the library treats a <see cref="SaveTransactionAttemptAsync"/>
/// throw as a gate refusal, so a throw should mean "store actually unavailable", not "one blip";
/// (c) be genuinely asynchronous — <see cref="SaveTransactionAttemptAsync"/> is the FIRST await in the
/// client's transaction flow, so any synchronous work (IO, retry sleeps) executes on the caller's thread
/// before anything yields, which on a WinForms POS is the UI thread at tender start.</para>
/// </remarks>
public interface ISmartConnectTransactionState
{
	/// <summary>
	/// Records the sentinel <em>before</em> the transaction POST is sent. If this throws, the client does not
	/// send the transaction — this is the load-bearing crash-recovery guarantee.
	/// </summary>
	/// <param name="clientTransactionRef">The caller-supplied correlation reference.</param>
	/// <param name="transactionType">The SmartConnect transaction type being attempted.</param>
	/// <param name="amountTotalCents">The total amount in minor units (cents).</param>
	Task SaveTransactionAttemptAsync(string clientTransactionRef, string transactionType, long amountTotalCents);

	/// <summary>
	/// Records the polling URL (and server transaction id) once the initial POST response is received. This
	/// closes the recovery gap. The <paramref name="pollingUrl"/> carries a bearer token; store it with
	/// restricted access and never log it.
	/// </summary>
	Task UpdatePollingDetailsAsync(string clientTransactionRef, string pollingUrl, string transactionId);

	/// <summary>Records that the transaction reached the given terminal <paramref name="status"/>. For a completed
	/// outcome delivered by polling the client does not call this — the consumer calls it after durably persisting
	/// the outcome, which moves the record out of <see cref="GetPendingTransactionsAsync"/>. (The client does call
	/// it itself only when it closes a rejected/never-sent POST as <c>Failed</c>; poll exhaustion no longer
	/// finalizes — see Decision 13.) Calling it on an already-completed record is permitted and idempotent
	/// (recovery replay and consumer-side deployment skew both reach this). A consumer completing an
	/// exhaustion-<c>Unknown</c> record may pass any resolved status, or <c>Unknown</c> to accept the ambiguity
	/// and stop tracking.</summary>
	Task UpdateCompletedAsync(string clientTransactionRef, SmartConnectTransactionStatus status);

	/// <summary>Returns all transactions that have not reached a terminal state. Used during crash recovery.</summary>
	Task<IEnumerable<PendingTransaction>> GetPendingTransactionsAsync();

	/// <summary>
	/// Removes a completed transaction record. Implementations MUST throw
	/// <see cref="InvalidOperationException"/> if the record has not reached a terminal state — pending
	/// transactions cannot be removed.
	/// </summary>
	/// <exception cref="InvalidOperationException">The record has not reached a terminal state.</exception>
	Task RemoveAsync(string clientTransactionRef);
}
