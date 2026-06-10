namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The registration details for a Layer-2 recovery query (<c>Journal.GetTransResult</c>). The triple must
/// match the values used at pairing and on the original transaction.
/// </summary>
/// <remarks>
/// <c>Journal.GetTransResult</c> is deprecated by the vendor and undocumented for async mode (design Known
/// Limitation #4) — its behaviour must be confirmed against the dev environment before relying on it.
/// </remarks>
public sealed class SmartConnectRecoveryRequest
{
	/// <summary>The globally-unique register id (UUID format), as used at pairing.</summary>
	public string POSRegisterID { get; set; } = string.Empty;

	/// <summary>The merchant/business name, as used at pairing.</summary>
	public string POSBusinessName { get; set; } = string.Empty;

	/// <summary>The POS vendor name, as used at pairing.</summary>
	public string POSVendorName { get; set; } = string.Empty;
}
