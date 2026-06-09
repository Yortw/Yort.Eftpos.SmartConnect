namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The details supplied when pairing a register to a terminal (<c>PUT /Pairing/{code}</c>). The
/// <c>POSRegisterID</c>/<c>POSBusinessName</c>/<c>POSVendorName</c> triple must match across pairing and all
/// subsequent transactions.
/// </summary>
public sealed class SmartConnectPairingRequest
{
	/// <summary>The globally-unique register id (UUID format). See <see cref="SmartConnectRegisterId"/>.</summary>
	public string POSRegisterID { get; set; } = string.Empty;

	/// <summary>A human-readable name for this register.</summary>
	public string? POSRegisterName { get; set; }

	/// <summary>The merchant/business name. Must match on every subsequent transaction.</summary>
	public string POSBusinessName { get; set; } = string.Empty;

	/// <summary>The POS vendor name. Must match on every subsequent transaction.</summary>
	public string POSVendorName { get; set; } = string.Empty;
}
