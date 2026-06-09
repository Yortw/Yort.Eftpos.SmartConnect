using Xunit;

namespace Yort.Eftpos.SmartConnect.Tests;

public class MoneyTests
{
	[Fact]
	public void FromCents_RoundTripsViaToCents()
	{
		Assert.Equal(1234, Money.FromCents(1234).ToCents());
	}

	[Fact]
	public void FromCents_ToDecimal_ExpressesSameAmountInDollars()
	{
		Assert.Equal(12.34m, Money.FromCents(1234).ToDecimal());
	}

	[Fact]
	public void FromDecimal_StoresWholeCents()
	{
		Assert.Equal(1234, Money.FromDecimal(12.34m).ToCents());
	}

	[Fact]
	public void FromDecimal_RoundsHalfAwayFromZero()
	{
		// 2.005 * 100 = 200.5 -> away-from-zero -> 201 (banker's rounding would give 200).
		Assert.Equal(201, Money.FromDecimal(2.005m).ToCents());
	}

	[Fact]
	public void Default_IsZeroCents()
	{
		Assert.Equal(0, default(Money).ToCents());
	}

	[Fact]
	public void Equality_SameCents_AreEqual()
	{
		Assert.True(Money.FromCents(500) == Money.FromCents(500));
		Assert.True(Money.FromCents(500).Equals(Money.FromCents(500)));
		Assert.Equal(Money.FromCents(500).GetHashCode(), Money.FromCents(500).GetHashCode());
	}

	[Fact]
	public void Equality_DifferentCents_AreNotEqual()
	{
		Assert.True(Money.FromCents(500) != Money.FromCents(501));
		Assert.False(Money.FromCents(500) == Money.FromCents(501));
	}

	[Fact]
	public void ToString_FormatsDollarsWithTwoPlaces()
	{
		Assert.Equal("12.34", Money.FromCents(1234).ToString());
		Assert.Equal("5.00", Money.FromCents(500).ToString());
	}
}
