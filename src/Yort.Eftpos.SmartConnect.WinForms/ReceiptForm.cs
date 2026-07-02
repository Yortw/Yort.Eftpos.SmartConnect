using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The internal form behind <see cref="SmartConnectReceiptDialog"/>. Renders a fixed-width EFTPOS
/// receipt in a monospace font (the only way its columns line up), sizes itself to the content up to a cap
/// and scrolls beyond it, and is dismissed with OK or Escape.</summary>
internal sealed class ReceiptForm : Form
{
	// Caps so a long/wide receipt can't grow the window past something sensible — it scrolls instead.
	private const int Spacing = 12;
	private const int MaxClientWidth = 760;
	private const int MaxReceiptHeight = 560;

	private readonly TextBox _receipt;
	private readonly Button _ok;
	private readonly Font _baseFont;
	private readonly Font _receiptFont;
	private TaskCompletionSource<bool>? _ack;

	public ReceiptForm()
	{
		FormBorderStyle = FormBorderStyle.FixedSingle;
		ControlBox = false;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.CenterScreen;

		_baseFont = new Font(Font.FontFamily, 12f);
		Font = _baseFont;

		// Receipts are fixed-width text and only align in a monospace font (the bug this dialog exists to fix
		// was a receipt rendered in the proportional MessageBox font). Consolas ships on every supported Windows;
		// if it were somehow missing, GDI+ falls back to the default (proportional) UI font — so this relies on
		// Consolas's ubiquity, not on a guaranteed monospace substitution.
		_receiptFont = new Font("Consolas", 10f);
		_receipt = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			WordWrap = false,
			ScrollBars = ScrollBars.Both,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = Color.White,
			Font = _receiptFont,
			TabStop = false
		};

		_ok = new Button { Text = "OK", Size = new Size(80, 30) };
		_ok.Click += (_, _) => _ack?.TrySetResult(true);

		Controls.Add(_receipt);
		Controls.Add(_ok);

		// Only OK exists, so both Enter and Escape dismiss.
		AcceptButton = _ok;
		CancelButton = _ok;
	}

	/// <summary>Shows the receipt and returns when the operator dismisses it (OK / Escape / close).</summary>
	public Task ShowReceiptAsync(string receipt)
	{
		if (IsDisposed)
		{
			// Disposed mid-flight (host shutdown / early using-scope exit) — nothing to show.
			return Task.CompletedTask;
		}

		_receipt.Text = receipt ?? string.Empty;
		LayoutToContent();

		// F4-style pre-emption (as ProgressForm.ShowResultAsync): a second call must complete the prior
		// awaiter (as dismissed) before replacing its TCS, or that caller's await hangs forever and its
		// finally (owner Restore) never runs.
		_ack?.TrySetResult(true);
		// RunContinuationsAsynchronously: see PairingForm — keeps caller continuations out of click
		// handlers, OnFormClosing, and the pre-emption line above.
		_ack = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!Visible)
		{
			Show();
		}

		BringToFront();
		Activate();
		_ok.Focus();
		return _ack.Task;
	}

	private void LayoutToContent()
	{
		// Measure the unwrapped block in the receipt font: width = widest line, height = all lines. Measure a
		// non-empty string so an empty receipt still yields a sane line height.
		var measured = TextRenderer.MeasureText(
			_receipt.Text.Length == 0 ? " " : _receipt.Text,
			_receiptFont,
			new Size(int.MaxValue, int.MaxValue),
			TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

		// Leave room for the textbox border and a scrollbar when the content is capped.
		var scrollbar = SystemInformation.VerticalScrollBarWidth;
		var receiptWidth = Math.Min(measured.Width + scrollbar + 8, MaxClientWidth - (Spacing * 2));
		var receiptHeight = Math.Min(measured.Height + scrollbar + 8, MaxReceiptHeight);

		_receipt.Bounds = new Rectangle(Spacing, Spacing, receiptWidth, receiptHeight);

		var buttonTop = _receipt.Bottom + Spacing;
		_ok.Location = new Point(_receipt.Right - _ok.Width, buttonTop);

		ClientSize = new Size(_receipt.Right + Spacing, buttonTop + _ok.Height + Spacing);
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		// Closing via Escape/Alt+F4 must resolve the awaiter rather than leave ShowReceiptAsync hanging.
		_ack?.TrySetResult(true);
		base.OnFormClosing(e);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_ack?.TrySetResult(true);
			_receiptFont?.Dispose();
			_baseFont?.Dispose();
		}

		base.Dispose(disposing);
	}
}
