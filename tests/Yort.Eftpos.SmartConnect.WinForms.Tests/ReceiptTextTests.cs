using Xunit;
using Yort.Eftpos.SmartConnect.WinForms;

namespace Yort.Eftpos.SmartConnect.WinForms.Tests;

/// <summary>
/// SmartConnect receipts arrive LF-delimited (<c>"LINE1\nLINE2"</c>), but the classic Win32 multiline edit
/// control behind <see cref="ReceiptForm"/>'s <c>TextBox</c> only starts a new visual line on CRLF — a lone LF
/// renders as a control glyph and collapses the receipt onto one line. These pin the normalization that fixes it.
/// </summary>
public class ReceiptTextTests
{
	[Fact]
	public void NormalizeLineEndings_LoneLf_BecomesCrLf()
	{
		Assert.Equal("LINE1\r\nLINE2", ReceiptText.NormalizeLineEndings("LINE1\nLINE2"));
	}

	// The load-bearing negative case: text that is ALREADY CRLF must pass through unchanged. A naive
	// "\n" -> "\r\n" replace would double-convert it to "\r\r\n" (a stray CR glyph before every line).
	[Fact]
	public void NormalizeLineEndings_AlreadyCrLf_IsUnchanged()
	{
		Assert.Equal("LINE1\r\nLINE2", ReceiptText.NormalizeLineEndings("LINE1\r\nLINE2"));
	}

	[Fact]
	public void NormalizeLineEndings_LoneCr_BecomesCrLf()
	{
		Assert.Equal("LINE1\r\nLINE2", ReceiptText.NormalizeLineEndings("LINE1\rLINE2"));
	}

	[Fact]
	public void NormalizeLineEndings_MixedEndings_AllBecomeCrLf()
	{
		Assert.Equal("A\r\nB\r\nC\r\nD", ReceiptText.NormalizeLineEndings("A\nB\r\nC\rD"));
	}

	[Fact]
	public void NormalizeLineEndings_BlankLines_ArePreserved()
	{
		// A blank line between sections (double LF) must survive as a blank line (double CRLF), not collapse.
		Assert.Equal("A\r\n\r\nB", ReceiptText.NormalizeLineEndings("A\n\nB"));
	}

	[Fact]
	public void NormalizeLineEndings_NoLineBreaks_IsUnchanged()
	{
		Assert.Equal("SINGLE LINE", ReceiptText.NormalizeLineEndings("SINGLE LINE"));
	}

	[Fact]
	public void NormalizeLineEndings_Null_BecomesEmpty()
	{
		Assert.Equal(string.Empty, ReceiptText.NormalizeLineEndings(null));
	}
}
