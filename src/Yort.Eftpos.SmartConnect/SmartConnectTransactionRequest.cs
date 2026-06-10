namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// A request to process a transaction. Amounts are authoritative in minor units (cents) via the
/// <c>*Cents</c> properties; the <see cref="decimal"/> convenience properties read/write those cents so
/// callers can work in dollars without exposing rounding decisions.
/// </summary>
public sealed class SmartConnectTransactionRequest
{
	/// <summary>The SmartConnect transaction type. See <see cref="SmartConnectTransactionType"/>.</summary>
	public string TransactionType { get; set; } = SmartConnectTransactionType.CardPurchase;

	/// <summary>
	/// The total amount. <b>UNVERIFIED:</b> for <c>Card.PurchasePlusCash</c>, whether this includes the
	/// <see cref="AmountCash"/> portion or is purchase-only is not yet established — do not assume either
	/// interpretation until it is confirmed against the dev environment (verification item F9; amount
	/// arithmetic errors cause real financial harm).
	/// </summary>
	public Money AmountTotal { get; set; }

	/// <summary>
	/// The cash-out amount; sent only when <see cref="TransactionType"/> is <c>Card.PurchasePlusCash</c>.
	/// See the <see cref="AmountTotal"/> caveat about the unverified relationship between the two amounts.
	/// </summary>
	public Money AmountCash { get; set; }

	/// <summary>The globally-unique register id (UUID format).</summary>
	public string POSRegisterID { get; set; } = string.Empty;

	/// <summary>The merchant/business name. Must match the value used at pairing.</summary>
	public string POSBusinessName { get; set; } = string.Empty;

	/// <summary>The POS vendor name. Must match the value used at pairing.</summary>
	public string POSVendorName { get; set; } = string.Empty;

	/// <summary>
	/// The caller-supplied reference correlating this transaction across send and crash recovery, and the key
	/// under which its state is persisted. Must be stable across a restart for the same logical transaction.
	/// </summary>
	public string ClientTransactionRef { get; set; } = string.Empty;

	/// <summary>An optional vendor transaction reference, used to pair pre-auth (<c>Card.Authorise</c>) with finalise (<c>Card.Finalise</c>).</summary>
	public string? TransactionReference { get; set; }
}
