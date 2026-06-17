namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// A request to process a transaction. Amounts (<see cref="AmountTotal"/>, <see cref="AmountCash"/>) are
/// <see cref="Money"/> values — minor-unit (cents) authoritative; build them with
/// <see cref="Money.FromDecimal(decimal)"/> to work in dollars or <see cref="Money.FromCents(long)"/> for exact cents.
/// </summary>
public sealed class SmartConnectTransactionRequest
{
	/// <summary>The SmartConnect transaction type. See <see cref="SmartConnectTransactionType"/>.</summary>
	public string TransactionType { get; set; } = SmartConnectTransactionType.CardPurchase;

	/// <summary>
	/// The total amount. Always positive — including for refunds ("a non-zero positive number, regardless
	/// of the fact that this is a refund", per the official docs). For <c>Card.PurchasePlusCash</c> the
	/// docs state this INCLUDES the <see cref="AmountCash"/> portion ("the total amount of the transaction,
	/// including the cash-out amount") — documented 2026-06-10; dev-terminal confirmation is still a
	/// pre-release verification item (F9) because amount arithmetic errors cause real financial harm.
	/// </summary>
	public Money AmountTotal { get; set; }

	/// <summary>
	/// The cash-out amount; sent only when <see cref="TransactionType"/> is <c>Card.PurchasePlusCash</c>.
	/// Per the official docs it is the "cash portion of the AmountTotal" — i.e. a component of
	/// <see cref="AmountTotal"/>, not an addition to it. See the F9 note on <see cref="AmountTotal"/>.
	/// </summary>
	public Money AmountCash { get; set; }

	/// <summary>The globally-unique register id (UUID format).</summary>
	public string POSRegisterID { get; set; } = string.Empty;

	/// <summary>The merchant/business name — the RETAILER/STORE (docs: "Store Name"). Must match the value used at pairing.</summary>
	public string POSBusinessName { get; set; } = string.Empty;

	/// <summary>The POS software vendor name (docs: "POS Software Vendor") — the system provider, never the retailer. Must match the value used at pairing.</summary>
	public string POSVendorName { get; set; } = string.Empty;

	/// <summary>
	/// The caller-supplied reference correlating this transaction across send and crash recovery, and the key
	/// under which its state is persisted. Must be stable across a restart for the same logical transaction.
	/// </summary>
	public string ClientTransactionRef { get; set; } = string.Empty;

	/// <summary>An optional vendor transaction reference, used to pair pre-auth (<c>Card.Authorise</c>) with finalise (<c>Card.Finalise</c>).</summary>
	public string? TransactionReference { get; set; }

	/// <summary>
	/// Optional sale/line-item metadata sent in the <c>SaleData</c> field. Construct a versioned instance
	/// (e.g. <see cref="SaleData.V1.SaleData"/>). Descriptive metadata only — it is NOT echoed back and cannot
	/// be used for crash recovery. Omitted from the request when null. The library does not validate the content
	/// of amount/quantity strings (their encoding is unspecified) — pass what the terminal expects.
	/// </summary>
	public SmartConnectSaleData? SaleData { get; set; }
}
