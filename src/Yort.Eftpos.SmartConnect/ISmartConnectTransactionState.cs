using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The mandatory transaction-state persistence contract. The client calls these members at the correct
/// lifecycle points so a transaction can be recovered after a crash. Persistence is a dependency, not an
/// optional event — supply an implementation via <see cref="SmartConnectClientConfiguration.StateStore"/>.
/// </summary>
/// <remarks>
/// <para>The lifecycle is: <see cref="SaveTransactionAttemptAsync"/> (before the POST) →
/// <see cref="UpdatePollingDetailsAsync"/> (once the polling URL is received) →
/// <see cref="UpdateCompletedAsync"/> (terminal state). "Pending" is defined by the absence of completion;
/// the consumer calls <see cref="RemoveAsync"/> after it has finished with a completed record.</para>
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

	/// <summary>Records that the transaction reached the given terminal <paramref name="status"/>.</summary>
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
