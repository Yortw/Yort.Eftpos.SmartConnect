namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Line-ending normalization for receipt text bound to a WinForms multiline <c>TextBox</c>.</summary>
internal static class ReceiptText
{
	/// <summary>
	/// Rewrites every line ending in <paramref name="receipt"/> — lone LF, lone CR, or CRLF — to CRLF, so the
	/// text renders as multiple lines in a WinForms multiline <c>TextBox</c>.
	/// </summary>
	/// <remarks>
	/// SmartConnect returns receipts delimited with bare LF (<c>"LINE1\nLINE2"</c>), but the classic Win32
	/// multiline edit control that <c>TextBox</c> wraps only breaks a line on CRLF — a lone LF renders as a
	/// control-character glyph and the whole receipt collapses onto one line. Collapsing to LF first, then
	/// expanding to CRLF, is deliberate: a naive <c>"\n"</c>-&gt;<c>"\r\n"</c> replace would double-convert
	/// text that is already CRLF into <c>"\r\r\n"</c> (a stray CR glyph before every line).
	/// </remarks>
	/// <param name="receipt">The receipt text, or null.</param>
	/// <returns>The receipt with CRLF line endings; <see cref="string.Empty"/> when <paramref name="receipt"/> is null.</returns>
	public static string NormalizeLineEndings(string? receipt)
	{
		if (string.IsNullOrEmpty(receipt))
		{
			return string.Empty;
		}

		return receipt!
			.Replace("\r\n", "\n")
			.Replace("\r", "\n")
			.Replace("\n", "\r\n");
	}
}
