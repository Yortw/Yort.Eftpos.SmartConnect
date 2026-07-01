using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yort.Eftpos.SmartConnect.Internal;

/// <summary>
/// Serialises <see cref="Money"/> to/from JSON. SmartConnect encodes amounts as minor-unit integers that may
/// arrive as a JSON string (e.g. <c>"500"</c>) or a number (<c>500</c>); both are read as cents. Writes the
/// amount as a string of cents, matching the observed wire format.
/// </summary>
internal sealed class MoneyJsonConverter : JsonConverter<Money>
{
	/// <inheritdoc />
	public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			var text = reader.GetString();
			if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cents))
			{
				return Money.FromCents(cents);
			}

			throw new JsonException($"Cannot parse a money amount from string '{text}'.");
		}

		if (reader.TokenType == JsonTokenType.Number)
		{
			// Explicit TryGetInt64 (mirroring the string branch) rather than GetInt64: a fractional or out-of-range
			// number then surfaces as our own JsonException instead of relying on System.Text.Json to wrap the reader's
			// FormatException/OverflowException — which is what lets the parser's ReadMoney guard absorb it.
			if (reader.TryGetInt64(out var cents))
			{
				return Money.FromCents(cents);
			}

			throw new JsonException("Cannot parse a money amount from a non-integer or out-of-range JSON number.");
		}

		throw new JsonException($"Unexpected token '{reader.TokenType}' when reading a money amount.");
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToCents().ToString(CultureInfo.InvariantCulture));
	}
}
