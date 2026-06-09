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

	/// <summary>Pre-authorisation. Requires <c>AmountAuth</c>.</summary>
	public const string CardAuthorise = "Card.Authorise";

	/// <summary>Finalise a prior pre-authorisation. Requires <c>AmountFinal</c> and <c>TransactionReference</c>.</summary>
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

	/// <summary>Retrieve the result of a prior transaction. Deprecated by the vendor for async mode; used only as a crash-recovery fallback.</summary>
	public const string JournalGetTransResult = "Journal.GetTransResult";

	/// <summary>Query terminal status.</summary>
	public const string TerminalGetStatus = "Terminal.GetStatus";
}
