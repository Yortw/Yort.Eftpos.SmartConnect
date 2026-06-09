using System.Collections.Generic;
using Xunit;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect.Tests;

public class FormUrlEncoderTests
{
	private static KeyValuePair<string, string?> Field(string key, string? value) => new KeyValuePair<string, string?>(key, value);

	[Fact]
	public void Encode_SinglePair_ProducesKeyEqualsValue()
	{
		Assert.Equal("TransactionType=Card.Purchase", FormUrlEncoder.Encode(new[] { Field("TransactionType", "Card.Purchase") }));
	}

	[Fact]
	public void Encode_MultiplePairs_JoinsWithAmpersand_PreservingOrder()
	{
		var body = FormUrlEncoder.Encode(new[]
		{
			Field("TransactionMode", "ASYNC"),
			Field("TransactionType", "Card.Purchase"),
			Field("AmountTotal", "500")
		});
		Assert.Equal("TransactionMode=ASYNC&TransactionType=Card.Purchase&AmountTotal=500", body);
	}

	[Fact]
	public void Encode_PercentEncodesSpecialCharacters()
	{
		// Space, apostrophe, ampersand must be percent-encoded so they can't break the body.
		var body = FormUrlEncoder.Encode(new[] { Field("POSBusinessName", "John Doe's & Co") });
		Assert.Equal("POSBusinessName=John%20Doe%27s%20%26%20Co", body);
	}

	[Fact]
	public void Encode_NullValue_TreatedAsEmpty()
	{
		Assert.Equal("Optional=", FormUrlEncoder.Encode(new[] { Field("Optional", null) }));
	}

	[Fact]
	public void Encode_IntegerAmountString_Unchanged()
	{
		// Amounts arrive already formatted as integer cents; no decimal point should appear.
		Assert.Equal("AmountTotal=1234", FormUrlEncoder.Encode(new[] { Field("AmountTotal", "1234") }));
	}
}
