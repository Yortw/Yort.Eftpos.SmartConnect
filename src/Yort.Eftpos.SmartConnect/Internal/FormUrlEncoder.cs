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
			builder.Append(Uri.EscapeDataString(field.Value ?? string.Empty));
		}

		return builder.ToString();
	}
}
