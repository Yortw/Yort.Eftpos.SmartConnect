namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The terminal outcome of a SmartConnect transaction, as surfaced to the caller.
/// </summary>
/// <remarks>
/// <para>Declines and cancellations are normal, final outcomes reported here as data; exceptions are
/// reserved for failures to obtain an answer at all.</para>
/// <para><see cref="Unknown"/> is value zero deliberately: a default-initialised or never-populated
/// status reads as "outcome not determined", never as <see cref="Accepted"/>. Always handle
/// <see cref="Unknown"/> explicitly — it requires reconciliation and is never safe to treat as approved.</para>
/// </remarks>
public enum SmartConnectTransactionStatus
{
	/// <summary>
	/// The financial outcome is ambiguous — polling was exhausted or interrupted without a terminal result,
	/// or the status was never set. The caller MUST handle this explicitly (e.g. flag for reconciliation);
	/// it is never safe to treat as approved. This is value zero so an unset status can never read as <see cref="Accepted"/>.
	/// </summary>
	Unknown = 0,

	/// <summary>The transaction was approved by the terminal/issuer.</summary>
	Accepted,

	/// <summary>
	/// The transaction was declined by the terminal/issuer (a normal, final outcome — not an error).
	/// Typical EFTPOS decline reasons include insufficient funds, an incorrect PIN, an invalid/unsupported
	/// account type, or an expired card — but SmartConnect surfaces no machine-readable decline reason to
	/// branch on. Probed live (2026-07-28), a declined response carries no reason field at all; the reason
	/// appears only as free text inside <see cref="SmartConnectTransactionResult.Receipt"/> (observed:
	/// "UNABLE TO PROCESS"). Render the receipt to tell an operator why — do not expect a reason code.
	/// </summary>
	Declined,

	/// <summary>The customer or operator cancelled the transaction at the terminal.</summary>
	Cancelled,

	/// <summary>The terminal could not be reached (a connectivity/interface failure reported by the cloud).</summary>
	DeviceOffline,

	/// <summary>The transaction failed for a reason that is neither an approval, a decline, nor a cancellation.</summary>
	Failed
}
