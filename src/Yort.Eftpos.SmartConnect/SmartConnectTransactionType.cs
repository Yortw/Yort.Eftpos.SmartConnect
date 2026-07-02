namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The SmartConnect <c>TransactionType</c> wire values. These are the exact strings sent in the
/// <c>TransactionType</c> form field of a <c>POST /Transaction</c> request.
/// </summary>
public static class SmartConnectTransactionType
{
	/// <summary>Card purchase. Requires <c>AmountTotal</c>.</summary>
	public const string CardPurchase = "Card.Purchase";

	/// <summary>Card refund. Requires <c>AmountTotal</c> (a positive amount).</summary>
	public const string CardRefund = "Card.Refund";

	/// <summary>Purchase with cash-out. Requires <c>AmountTotal</c> and <c>AmountCash</c>.</summary>
	public const string CardPurchasePlusCash = "Card.PurchasePlusCash";

	/// <summary>Cash advance / cash-out only. Requires <c>AmountTotal</c>.</summary>
	public const string CardCashAdvance = "Card.CashAdvance";

	/// <summary>Pre-authorisation. Requires <c>AmountAuth</c>. NOT YET FULLY SUPPORTED — <see cref="SmartConnectTransactionRequest"/> does not yet carry an <c>AmountAuth</c> field (deferred; see the design notes).</summary>
	public const string CardAuthorise = "Card.Authorise";

	/// <summary>Finalise a prior pre-authorisation. Requires <c>AmountFinal</c> and <c>TransactionReference</c>. NOT YET FULLY SUPPORTED — <see cref="SmartConnectTransactionRequest"/> does not yet carry an <c>AmountFinal</c> field (deferred).</summary>
	public const string CardFinalise = "Card.Finalise";

	/// <summary>QR purchase where the terminal displays the code (preferred QR mode). Requires <c>AmountTotal</c>.</summary>
	public const string QrMerchantPurchase = "QR.Merchant.Purchase";

	/// <summary>QR purchase where the customer displays the code. Requires <c>AmountTotal</c>.</summary>
	public const string QrConsumerPurchase = "QR.Consumer.Purchase";

	/// <summary>QR refund. Requires <c>AmountTotal</c> (a positive amount).</summary>
	public const string QrRefund = "QR.Refund";

	/// <summary>Acquirer logon.</summary>
	public const string AcquirerLogon = "Acquirer.Logon";

	/// <summary>Settlement inquiry. Not supported on AU/NZ Android devices.</summary>
	public const string AcquirerSettlementInquiry = "Acquirer.Settlement.Inquiry";

	/// <summary>Settlement cutover.</summary>
	public const string AcquirerSettlementCutover = "Acquirer.Settlement.Cutover";

	/// <summary>Reprint the last receipt.</summary>
	public const string JournalReprintReceipt = "Journal.ReprintReceipt";

	/// <summary>Retrieve the result of the terminal's last transaction. Deprecated by the vendor for async mode; a diagnostic only — device-scoped, and its result cannot be reliably matched to a specific sale.</summary>
	public const string JournalGetTransResult = "Journal.GetTransResult";

	/// <summary>Query terminal status.</summary>
	public const string TerminalGetStatus = "Terminal.GetStatus";

	// (J2) The ONE list of financial type names, adjacent to the constants so the two cannot drift. Used by
	// ExecuteNonFinancialAsync's bypass guard; ProcessTransactionAsync is the financial path for these.
	private static readonly string[] KnownFinancialTypes =
	{
		CardPurchase, CardRefund, CardPurchasePlusCash, CardCashAdvance, CardAuthorise, CardFinalise,
		QrMerchantPurchase, QrConsumerPurchase, QrRefund
	};

	/// <summary>
	/// Returns whether <paramref name="transactionType"/> is one of the financial (money-moving) types this
	/// library knows. Financial types must go through <c>ProcessTransactionAsync</c> — the crash-recovery
	/// sentinel is mandatory for money — and are rejected by <c>ExecuteNonFinancialAsync</c>.
	/// </summary>
	public static bool IsKnownFinancial(string transactionType)
	{
		foreach (var financialType in KnownFinancialTypes)
		{
			if (string.Equals(financialType, transactionType, System.StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
