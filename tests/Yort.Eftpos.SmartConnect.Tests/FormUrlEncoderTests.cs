using System;
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

	// --- Chunked escaping (net48's Uri.EscapeDataString throws for inputs > 32,766 chars; a large
	// SaleData value can exceed that, so values are escaped in chunks — output must be identical) ---

	// These run on net8, where Uri.EscapeDataString accepts any length: the unchunked call is the
	// oracle, and tiny chunk sizes exercise every boundary case the real 32K chunk would ever meet.
	[Theory]
	[InlineData("plain ascii text with spaces & symbols!", 3)]
	[InlineData("plain ascii text with spaces & symbols!", 7)]
	[InlineData("café costs €5 — déjà vu", 2)]
	[InlineData("café costs €5 — déjà vu", 5)]
	public void EscapeDataString_Chunked_MatchesUnchunkedOracle(string value, int chunkSize)
	{
		Assert.Equal(Uri.EscapeDataString(value), FormUrlEncoder.EscapeDataString(value, chunkSize));
	}

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	public void EscapeDataString_Chunked_NeverSplitsSurrogatePairs(int chunkSize)
	{
		// Emoji are surrogate PAIRS: a chunk boundary landing between the halves would hand
		// Uri.EscapeDataString a lone surrogate (invalid UTF-16), corrupting the encoding. Mixed
		// 1-char/2-char content makes pairs straddle every possible boundary offset.
		var value = "a\U0001F600b\U0001F680\U0001F600cd\U0001F680";
		Assert.Equal(Uri.EscapeDataString(value), FormUrlEncoder.EscapeDataString(value, chunkSize));
	}

	[Fact]
	public void Encode_ValueLongerThanNet48EscapeLimit_EncodesCompletely()
	{
		// 40,000 chars > net48's 32,766 limit. On this (net8) runner the limit does not exist, so this
		// pins completeness/equivalence; the net48 throw itself is covered by routing through the
		// chunked path, whose boundary logic the tests above pin at small sizes.
		var value = new string('x', 39_000) + "tail é\U0001F600";
		var body = FormUrlEncoder.Encode(new[] { Field("SaleData", value) });
		Assert.Equal("SaleData=" + Uri.EscapeDataString(value), body);
	}
}
