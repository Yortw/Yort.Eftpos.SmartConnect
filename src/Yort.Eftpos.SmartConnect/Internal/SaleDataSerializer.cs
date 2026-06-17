using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Yort.Eftpos.SmartConnect.Internal;

/// <summary>
/// Serialises a <see cref="SmartConnectSaleData"/> to the SmartConnect <c>SaleData</c> wire envelope:
/// <c>{ "version": "...", "saleData": { ... } }</c>. The body is serialised by the value's RUNTIME type, so V1 —
/// or a caller's own derived type — emits its full property set; serialising a base-typed reference directly
/// would emit only the base. <c>version</c> is composed once at the envelope root and stripped from the body
/// (the base <c>[JsonIgnore]</c> does not propagate to overrides, and third-party types may not annotate it).
/// </summary>
internal static class SaleDataSerializer
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>Serialises <paramref name="saleData"/> to the version-enveloped JSON string.</summary>
	public static string Serialize(SmartConnectSaleData saleData)
	{
		// Runtime-type serialisation captures derived (V1/custom) properties; a base-typed serialise would not.
		var body = JsonSerializer.SerializeToNode(saleData, saleData.GetType(), Options)?.AsObject();
		body?.Remove("version");

		var envelope = new JsonObject
		{
			["version"] = saleData.Version,
			["saleData"] = body
		};

		return envelope.ToJsonString(Options);
	}
}
