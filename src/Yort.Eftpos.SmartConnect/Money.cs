using System;
using System.Globalization;
using System.Text.Json.Serialization;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// An immutable monetary amount stored as a whole number of minor units (cents). The same amount can be read
/// in cents (<see cref="ToCents"/>) or dollars (<see cref="ToDecimal"/>) — these are two representations of one
/// value, not separate parts of it. Construct with <see cref="FromCents"/> or <see cref="FromDecimal"/>.
/// </summary>
/// <remarks>
/// Currency-agnostic by design (SmartConnect is single-currency). On the wire, amounts are minor-unit integers
/// encoded as JSON strings; <see cref="MoneyJsonConverter"/> centralises that string/number handling so it
/// lives in one place.
/// </remarks>
[JsonConverter(typeof(MoneyJsonConverter))]
public readonly struct Money : IEquatable<Money>
{
	private readonly long _cents;

	private Money(long cents)
	{
		_cents = cents;
	}

	/// <summary>Creates a <see cref="Money"/> from a whole number of minor units (cents).</summary>
	public static Money FromCents(long cents) => new Money(cents);

	/// <summary>
	/// Creates a <see cref="Money"/> from a dollar amount. Sub-cent precision (3+ decimal places) is rounded to
	/// whole cents, half away from zero — it is NOT rejected, so pass a cents-precise value (≤2 decimal places)
	/// if you need exactness; use <see cref="FromCents(long)"/> when you already have cents.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="dollars"/> is too large to represent as a whole
	/// number of cents in an <see cref="long"/> (roughly ±9.2e16 dollars).</exception>
	public static Money FromDecimal(decimal dollars)
	{
		decimal cents;
		try
		{
			cents = Math.Round(dollars * 100m, MidpointRounding.AwayFromZero);
		}
		catch (OverflowException)
		{
			// The dollars * 100m multiply overflowed the decimal type itself (pathologically large input).
			// Surface it as the caller's bad argument, never a raw arithmetic fault.
			throw new ArgumentOutOfRangeException(nameof(dollars), dollars, AmountTooLargeMessage);
		}

		// decimal holds values far beyond long, so range-check the cents before the narrowing cast — otherwise
		// the (long) cast throws a bare OverflowException the caller can't distinguish from an internal bug.
		if (cents < long.MinValue || cents > long.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(dollars), dollars, AmountTooLargeMessage);
		}

		return new Money((long)cents);
	}

	private const string AmountTooLargeMessage = "The amount is too large to represent as a monetary value.";

	/// <summary>Returns the whole amount expressed in minor units (cents).</summary>
	public long ToCents() => _cents;

	/// <summary>Returns the whole amount expressed in dollars.</summary>
	public decimal ToDecimal() => _cents / 100m;

	/// <inheritdoc />
	public bool Equals(Money other) => _cents == other._cents;

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is Money other && Equals(other);

	/// <inheritdoc />
	public override int GetHashCode() => _cents.GetHashCode();

	/// <summary>Determines whether two amounts are equal.</summary>
	public static bool operator ==(Money left, Money right) => left.Equals(right);

	/// <summary>Determines whether two amounts are unequal.</summary>
	public static bool operator !=(Money left, Money right) => !left.Equals(right);

	/// <summary>Returns the dollar amount formatted with two decimal places (invariant culture).</summary>
	public override string ToString() => ToDecimal().ToString("0.00", CultureInfo.InvariantCulture);
}
