using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>The internal form implementing <see cref="IProgressView"/>. Hand-coded layout (no designer
/// file): a logo, a caption label, an indeterminate marquee progress bar (busy), and an outcome panel
/// with a coloured caption, optional detail, and an OK button.</summary>
internal sealed class ProgressForm : Form, IProgressView
{
	private readonly PictureBox _logo;
	private readonly Label _caption;
	private readonly ProgressBar _busy;
	private readonly Panel _resultPanel;
	private readonly Label _resultCaption;
	private readonly Label _resultDetail;
	private readonly Button _ok;
	private System.Windows.Forms.Timer? _autoClose;
	private TaskCompletionSource<bool>? _resultAck;

	/// <summary>Creates the form with a fixed hand-coded layout.</summary>
	public ProgressForm()
	{
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ControlBox = false;
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(360, 160);

		_logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(12, 12, 64, 64), Visible = false };
		_caption = new Label { Bounds = new Rectangle(12, 84, 336, 28), TextAlign = ContentAlignment.MiddleCenter };
		_busy = new ProgressBar { Style = ProgressBarStyle.Marquee, Bounds = new Rectangle(12, 120, 336, 16) };

		_resultCaption = new Label { Bounds = new Rectangle(12, 24, 336, 40), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 16, FontStyle.Bold) };
		_resultDetail = new Label { Bounds = new Rectangle(12, 68, 336, 36), TextAlign = ContentAlignment.MiddleCenter };
		_ok = new Button { Text = "OK", Bounds = new Rectangle(140, 112, 80, 30), DialogResult = DialogResult.OK };
		_ok.Click += (_, _) => CompleteResult();
		_resultPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
		_resultPanel.Controls.Add(_resultCaption);
		_resultPanel.Controls.Add(_resultDetail);
		_resultPanel.Controls.Add(_ok);

		Controls.Add(_logo);
		Controls.Add(_caption);
		Controls.Add(_busy);
		Controls.Add(_resultPanel);
	}

	/// <summary>The logo picture box; exposed so the outer dialog can apply chrome (logo image) to it.</summary>
	public PictureBox LogoBox => _logo;

	/// <summary>Shows the busy state with the given caption. Auto-shows the form on first call.</summary>
	public void ShowBusy(string caption)
	{
		_resultPanel.Visible = false;
		_logo.Visible = _logo.Image != null;
		_caption.Visible = true;
		_busy.Visible = true;
		_caption.Text = caption;
		if (!Visible)
		{
			Show();
		}

		BringToFront();
		Activate();
	}

	/// <summary>Updates the caption text while the busy state is already showing.</summary>
	public void UpdateCaption(string caption)
	{
		_caption.Text = caption;
	}

	/// <summary>Switches to the outcome panel. Returns when the operator clicks OK or the optional
	/// auto-close timer elapses.</summary>
	public Task ShowResultAsync(ResultVisual visual, TimeSpan? autoCloseAfter)
	{
		if (IsDisposed)
		{
			// F7: disposed mid-flight (host shutdown / early using-scope exit) — nothing to show.
			return Task.CompletedTask;
		}

		// F4 re-entrancy: complete and clear any prior outcome wait/timer before starting a new one, so a
		// second ShowResultAsync can never strand the first awaiter or orphan its timer.
		_autoClose?.Stop();
		_autoClose?.Dispose();
		_autoClose = null;
		_resultAck?.TrySetResult(false);

		if (!Visible)
		{
			Show();
		}

		_caption.Visible = false;
		_busy.Visible = false;
		_resultCaption.Text = visual.Caption;
		_resultCaption.ForeColor = SeverityColour(visual.Severity);
		_resultDetail.Text = visual.Detail ?? string.Empty;
		_resultPanel.Visible = true;
		_resultPanel.BringToFront();
		_ok.Focus();

		_resultAck = new TaskCompletionSource<bool>();
		if (autoCloseAfter.HasValue)
		{
			// F3: DialogTimeouts guards against <= 0 (Timer.Interval throws) and int overflow.
			_autoClose = new System.Windows.Forms.Timer { Interval = DialogTimeouts.ToIntervalMs(autoCloseAfter.Value) };
			_autoClose.Tick += (_, _) => CompleteResult();
			_autoClose.Start();
		}

		return _resultAck.Task;
	}

	/// <summary>Completes the current result acknowledgement and stops/disposes the auto-close timer.
	/// Called by OK click and by the auto-close tick; safe to call from either path (TrySetResult is
	/// race-safe).</summary>
	private void CompleteResult()
	{
		_autoClose?.Stop();
		_autoClose?.Dispose();
		_autoClose = null;
		_resultAck?.TrySetResult(true);
	}

	/// <summary>Maps a severity bucket to a display colour.</summary>
	private static Color SeverityColour(ResultSeverity severity)
	{
		if (severity == ResultSeverity.Success)
		{
			return Color.ForestGreen;
		}

		if (severity == ResultSeverity.Ambiguous)
		{
			return Color.DarkGoldenrod;
		}

		return Color.Firebrick;
	}

	/// <inheritdoc/>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_autoClose?.Dispose();
			_resultAck?.TrySetResult(false);
		}

		base.Dispose(disposing);
	}
}
