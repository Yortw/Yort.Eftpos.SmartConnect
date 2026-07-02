namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// Why a transaction did not complete normally — the driver's switch for "retry freely" vs "recovery flow"
/// vs "service said no" (ADR Decision 9). See individual values for retry guidance.
/// </summary>
public enum SmartConnectFailureCause
{
	/// <summary>No failure-cause detail — the transaction has a normal outcome (including Declined).</summary>
	None = 0,

	/// <summary>SmartConnect answered and rejected the request (e.g. HTTP 400). Fix the request/configuration; blind retry will fail again.</summary>
	ServiceError = 1,

	/// <summary>
	/// The transaction POST provably never reached SmartConnect (DNS, TCP connect, TLS handshake). Nothing
	/// happened service-side — safe to retry once connectivity returns.
	/// </summary>
	TransportNotSent = 2,

	/// <summary>
	/// The exchange failed (or the response was unusable) after the POST may have reached SmartConnect — the
	/// outcome is unknown and the transaction may have been processed. Never blind-retry; use the recovery flow.
	/// </summary>
	TransportUnknown = 3,

	/// <summary>
	/// The state store refused the pre-POST sentinel write, so the transaction was never sent (the gate is
	/// absolute — ADR Decision 10). Safe to retry once the store is healthy.
	/// </summary>
	StateStoreFailure = 4,

	/// <summary>
	/// SmartConnect answered the poll with a verdict that the polling URL itself is no good
	/// (401/403/404/410) — the transaction's outcome cannot be learned by polling. Never blind-retry; the
	/// outcome must be resolved by manual reconciliation (the sentinel stays pending until then).
	/// </summary>
	PollingUrlInvalid = 5
}
