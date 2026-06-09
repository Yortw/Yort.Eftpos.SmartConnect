using System;

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

	/// <summary>The total amount in minor units (cents). Authoritative.</summary>
	public long AmountTotalCents { get; set; }

	/// <summary>
	/// The total amount in major units (dollars). A convenience over <see cref="AmountTotalCents"/>:
	/// the getter divides by 100; the setter rounds to whole cents (away from zero). Inputs should already
	/// be cents-precise (≤2 decimal places).
	/// </summary>
	public decimal AmountTotal
	{
		get => AmountTotalCents / 100m;
		set => AmountTotalCents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
	}

	/// <summary>The cash-out amount in minor units (cents), for purchase-plus-cash. Authoritative.</summary>
	public long AmountCashCents { get; set; }

	/// <summary>The cash-out amount in major units (dollars). Convenience over <see cref="AmountCashCents"/>.</summary>
	public decimal AmountCash
	{
		get => AmountCashCents / 100m;
		set => AmountCashCents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
	}

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
