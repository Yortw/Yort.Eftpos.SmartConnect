using System.Text.Json.Serialization;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// Base for the optional SmartConnect <c>SaleData</c> payload attached to a transaction. Version-agnostic: the
/// concrete schema lives in a versioned namespace (e.g. <c>Yort.Eftpos.SmartConnect.SaleData.V1</c>). It is the
/// type of the sale-metadata property on <see cref="SmartConnectTransactionRequest"/>, so any schema version —
/// or a caller's own derived type — is accepted; the library serialises the runtime type, so derived properties
/// are sent.
/// </summary>
public abstract class SmartConnectSaleData
{
	/// <summary>
	/// The wire schema version, sent as the envelope's root <c>version</c> field. Excluded from the nested
	/// <c>saleData</c> body (the library composes it at the root).
	/// </summary>
	[JsonIgnore]
	public abstract string Version { get; }
}
