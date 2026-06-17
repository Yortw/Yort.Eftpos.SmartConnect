using System;
using System.Globalization;
using Yort.Eftpos.SmartConnect.Internal;

namespace Yort.Eftpos.SmartConnect;

/// <summary>
/// Generates a deterministic <c>POSRegisterID</c> (a version-5 UUID), unique within the caller's own
/// namespace, from a merchant identifier and a register identifier.
/// </summary>
/// <remarks>
/// <para>The same inputs always produce the same id, so a register that supplies a <em>stable</em> identifier
/// reuses its <c>POSRegisterID</c> — and therefore its existing pairing — across reinstalls. A hardware id
/// would change and force re-pairing; prefer a logical, reinstall-stable identifier.</para>
/// <para>This is a convenience; callers may use any UUID-format id strategy they prefer.</para>
/// </remarks>
public static class SmartConnectRegisterId
{
	// Fixed private namespace for register ids. Arbitrary but permanent — never change it, or every
	// generated id would change and break existing pairings.
	private static readonly Guid RegisterNamespace = new Guid("8b617c24-6e4c-4766-addf-3d02b28a97b9");

	/// <summary>
	/// Generates the deterministic register id.
	/// </summary>
	/// <param name="merchantIdentifier">
	/// Identifies the merchant/tenant, so ids do not collide across merchants (the caller's namespace).
	/// </param>
	/// <param name="registerIdentifier">
	/// Identifies the specific terminal/register. Prefer a stable logical id over a hardware id (see remarks).
	/// </param>
	/// <returns>
	/// A deterministic id in canonical UUID string form (lowercase, hyphenated — `Guid` "D" format), ready to
	/// assign to <c>POSRegisterID</c>. Returns a string (not a <see cref="Guid"/>) so callers needn't convert and
	/// format consistently, and so the id-generation algorithm could change without a breaking signature change.
	/// </returns>
	/// <exception cref="ArgumentException">Either argument is null, empty, or whitespace.</exception>
	public static string Generate(string merchantIdentifier, string registerIdentifier)
	{
		if (string.IsNullOrWhiteSpace(merchantIdentifier))
		{
			throw new ArgumentException("Merchant identifier must not be null, empty, or whitespace.", nameof(merchantIdentifier));
		}

		if (string.IsNullOrWhiteSpace(registerIdentifier))
		{
			throw new ArgumentException("Register identifier must not be null, empty, or whitespace.", nameof(registerIdentifier));
		}

		return UuidV5.Create(RegisterNamespace, Combine(merchantIdentifier, registerIdentifier)).ToString("D");
	}

	// Length-prefixed join so ("ab","c") and ("a","bc") never collide, without relying on a delimiter
	// character that might appear in an identifier.
	private static string Combine(string merchantIdentifier, string registerIdentifier)
	{
		return merchantIdentifier.Length.ToString(CultureInfo.InvariantCulture) + ":" + merchantIdentifier + registerIdentifier;
	}
}
