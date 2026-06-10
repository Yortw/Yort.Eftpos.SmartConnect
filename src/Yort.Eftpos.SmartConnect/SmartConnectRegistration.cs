namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// The register's identity triple, as established at pairing — used by every non-financial operation
/// (terminal status, logon, settlement, the Layer-2 recovery query, and the unknown-type escape hatch).
/// The values must match those used at pairing and on financial transactions.
/// </summary>
public sealed class SmartConnectRegistration
{
	/// <summary>The globally-unique register id (UUID format), as used at pairing.</summary>
	public string POSRegisterID { get; set; } = string.Empty;

	/// <summary>The merchant/business name, as used at pairing.</summary>
	public string POSBusinessName { get; set; } = string.Empty;

	/// <summary>The POS vendor name, as used at pairing.</summary>
	public string POSVendorName { get; set; } = string.Empty;
}
