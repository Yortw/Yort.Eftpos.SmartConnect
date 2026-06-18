using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The internal form implementing <see cref="IPairingView"/>. Hand-coded layout: a logo, a
/// prompt label, a code textbox with Pair/Cancel, a busy spinner, and a failure panel with a coloured
/// message and Try-again/Cancel. Success is shown briefly with an OK button.</summary>
internal sealed class PairingForm : Form, IPairingView
{
	private readonly PictureBox _logo;
	private readonly Label _prompt;
	private readonly TextBox _code;
	private readonly Button _pair;
	private readonly Button _cancel;
	private readonly ProgressBar _busy;
	private readonly Label _message;
	private readonly Button _retry;
	private readonly Button _cancel2;
	private readonly Button _ok;

	private TaskCompletionSource<string?>? _codeResult;
	private TaskCompletionSource<bool>? _failureResult;
	private TaskCompletionSource<bool>? _successAck;

	public PairingForm()
	{
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(380, 200);

		_logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(12, 12, 56, 56), Visible = false };
		_prompt = new Label { Bounds = new Rectangle(12, 76, 356, 24), Text = "Enter the pairing code shown on the terminal:" };
		_code = new TextBox { Bounds = new Rectangle(12, 104, 356, 24) };
		_code.TextChanged += (_, _) => _pair!.Enabled = _code.Text.Trim().Length > 0;
		_pair = new Button { Text = "Pair", Bounds = new Rectangle(206, 140, 76, 30), Enabled = false };
		_cancel = new Button { Text = "Cancel", Bounds = new Rectangle(292, 140, 76, 30) };
		_busy = new ProgressBar { Style = ProgressBarStyle.Marquee, Bounds = new Rectangle(12, 140, 180, 16), Visible = false };

		_message = new Label { Bounds = new Rectangle(12, 76, 356, 52), Visible = false, Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
		_retry = new Button { Text = "Try again", Bounds = new Rectangle(206, 140, 76, 30), Visible = false };
		_cancel2 = new Button { Text = "Cancel", Bounds = new Rectangle(292, 140, 76, 30), Visible = false };
		_ok = new Button { Text = "OK", Bounds = new Rectangle(292, 140, 76, 30), Visible = false };

		_pair.Click += (_, _) => _codeResult?.TrySetResult(_code.Text);
		_cancel.Click += (_, _) => _codeResult?.TrySetResult(null);
		_retry.Click += (_, _) => _failureResult?.TrySetResult(true);
		_cancel2.Click += (_, _) => _failureResult?.TrySetResult(false);
		_ok.Click += (_, _) => _successAck?.TrySetResult(true);

		Controls.AddRange(new Control[] { _logo, _prompt, _code, _pair, _cancel, _busy, _message, _retry, _cancel2, _ok });
	}

	public PictureBox LogoBox => _logo;

	public string PromptText
	{
		get => _prompt.Text;
		set => _prompt.Text = value;
	}

	public Task<string?> GetCodeAsync()
	{
		ShowPromptControls();
		if (!Visible)
		{
			Show();
		}

		BringToFront();
		Activate();
		_code.Focus();
		_codeResult = new TaskCompletionSource<string?>();
		return _codeResult.Task;
	}

	public void ShowBusy()
	{
		_pair.Enabled = false;
		_cancel.Enabled = false;
		_busy.Visible = true;
	}

	public void HideBusy()
	{
		_busy.Visible = false;
		_cancel.Enabled = true;
	}

	public Task<bool> ShowFailureAsync(string message, ResultSeverity severity)
	{
		HidePromptControls();
		_message.Text = message;
		_message.ForeColor = severity == ResultSeverity.Ambiguous ? Color.DarkGoldenrod : Color.Firebrick;
		_message.Visible = true;
		_retry.Visible = true;
		_cancel2.Visible = true;
		_ok.Visible = false;
		_retry.Focus();
		_failureResult = new TaskCompletionSource<bool>();
		return _failureResult.Task;
	}

	public Task ShowSuccessAsync(SmartConnectPairingResult result)
	{
		HidePromptControls();
		_message.Text = "Paired";
		_message.ForeColor = Color.ForestGreen;
		_message.Visible = true;
		_retry.Visible = false;
		_cancel2.Visible = false;
		_ok.Visible = true;
		_ok.Focus();
		_successAck = new TaskCompletionSource<bool>();
		return _successAck.Task;
	}

	private void ShowPromptControls()
	{
		_prompt.Visible = true;
		_code.Visible = true;
		_pair.Visible = true;
		_cancel.Visible = true;
		_pair.Enabled = _code.Text.Trim().Length > 0;
		_cancel.Enabled = true;
		_message.Visible = false;
		_retry.Visible = false;
		_cancel2.Visible = false;
		_ok.Visible = false;
		_busy.Visible = false;
	}

	private void HidePromptControls()
	{
		_prompt.Visible = false;
		_code.Visible = false;
		_pair.Visible = false;
		_cancel.Visible = false;
		_busy.Visible = false;
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		// F7: closing via the window controls (X / Alt+F4) must resolve any pending interaction as a
		// cancel, so an awaiting ShowAsync returns null instead of hanging waiting on a TCS no button
		// will ever complete.
		_codeResult?.TrySetResult(null);
		_failureResult?.TrySetResult(false);
		_successAck?.TrySetResult(true);
		base.OnFormClosing(e);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_codeResult?.TrySetResult(null);
			_failureResult?.TrySetResult(false);
			_successAck?.TrySetResult(true);
		}

		base.Dispose(disposing);
	}
}
