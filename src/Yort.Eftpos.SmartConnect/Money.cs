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
	/// Creates a <see cref="Money"/> from a dollar amount, rounding to whole cents (half away from zero).
	/// Inputs should already be cents-precise (≤2 decimal places).
	/// </summary>
	public static Money FromDecimal(decimal dollars) => new Money((long)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero));

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
