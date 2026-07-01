using System.Text.Json;
using Xunit;

namespace Yort.Eftpos.SmartConnect.Tests;

public class MoneyJsonConverterTests
{
	// The [JsonConverter] attribute on Money means these round-trip without extra options wiring.

	[Fact]
	public void Read_StringOfCents_ParsesAsCents()
	{
		Assert.Equal(500, JsonSerializer.Deserialize<Money>("\"500\"").ToCents());
	}

	[Fact]
	public void Read_NumberOfCents_ParsesAsCents()
	{
		Assert.Equal(500, JsonSerializer.Deserialize<Money>("500").ToCents());
	}

	[Fact]
	public void Read_NegativeStringOfCents_ParsesAsCents()
	{
		Assert.Equal(-50, JsonSerializer.Deserialize<Money>("\"-50\"").ToCents());
	}

	[Fact]
	public void Read_NonNumericString_Throws()
	{
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("\"abc\""));
	}

	[Fact]
	public void Read_FractionalNumber_ThrowsJsonException()
	{
		// A JSON number with a fraction cannot be whole cents. It must surface as JsonException (symmetric with the
		// string branch) so the parser's ReadMoney guard absorbs it and defaults the amount rather than crashing the poll.
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("5.50"));
	}

	[Fact]
	public void Read_NumberExceedingInt64_ThrowsJsonException()
	{
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("99999999999999999999"));
	}

	[Fact]
	public void Write_EmitsCentsAsString()
	{
		Assert.Equal("\"1234\"", JsonSerializer.Serialize(Money.FromCents(1234)));
	}

	[Fact]
	public void RoundTrip_PreservesAmount()
	{
		var original = Money.FromCents(98765);
		var json = JsonSerializer.Serialize(original);
		Assert.Equal(original, JsonSerializer.Deserialize<Money>(json));
	}
}
