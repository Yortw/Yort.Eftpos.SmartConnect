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
			return Money.FromCents(reader.GetInt64());
		}

		throw new JsonException($"Unexpected token '{reader.TokenType}' when reading a money amount.");
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToCents().ToString(CultureInfo.InvariantCulture));
	}
}
