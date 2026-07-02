using System;
using System.Collections.Generic;
using System.Text;

namespace Yort.Eftpos.SmartConnect.Internal;

/// <summary>
/// Builds an <c>application/x-www-form-urlencoded</c> request body from ordered key/value fields. Keys and
/// values are percent-encoded (RFC 3986). Field order is preserved.
/// </summary>
internal static class FormUrlEncoder
{
	// .NET Framework's Uri.EscapeDataString throws UriFormatException for inputs over 32,766 chars (the
	// limit was removed in .NET Core). A large SaleData value can exceed that, and the throw would strike
	// AFTER the pre-POST sentinel is written — an uncontracted exception escaping ProcessTransactionAsync
	// with a dangling never-sent sentinel behind it. Values are escaped in chunks instead; the output is
	// identical because percent-encoding is context-free above the code-point level.
	private const int MaxEscapeChunk = 32_000;

	/// <summary>Encodes the given fields into a form-url-encoded body string. A null value is treated as empty.</summary>
	public static string Encode(IEnumerable<KeyValuePair<string, string?>> fields)
	{
		var builder = new StringBuilder();

		foreach (var field in fields)
		{
			if (builder.Length > 0)
			{
				builder.Append('&');
			}

			builder.Append(Uri.EscapeDataString(field.Key));
			builder.Append('=');
			builder.Append(EscapeDataString(field.Value ?? string.Empty, MaxEscapeChunk));
		}

		return builder.ToString();
	}

	/// <summary>Percent-encodes <paramref name="value"/> in chunks of at most <paramref name="maxChunk"/>
	/// chars, never splitting a surrogate pair. Output is identical to a single-call escape. The chunk size
	/// is a parameter so tests can exercise the boundary logic at small sizes on runtimes without the limit.</summary>
	internal static string EscapeDataString(string value, int maxChunk)
	{
		if (value.Length <= maxChunk)
		{
			return Uri.EscapeDataString(value);
		}

		var builder = new StringBuilder(value.Length * 2);
		var index = 0;
		while (index < value.Length)
		{
			var length = Math.Min(maxChunk, value.Length - index);

			// A boundary between the halves of a surrogate pair would hand EscapeDataString a lone
			// surrogate (invalid UTF-16). Stop one char short — unless that empties the chunk, in which
			// case take the whole pair (still within net48's real limit, which is far above 2).
			if (char.IsHighSurrogate(value[index + length - 1]) && index + length < value.Length)
			{
				length = length > 1 ? length - 1 : 2;
			}

			builder.Append(Uri.EscapeDataString(value.Substring(index, length)));
			index += length;
		}

		return builder.ToString();
	}
}
