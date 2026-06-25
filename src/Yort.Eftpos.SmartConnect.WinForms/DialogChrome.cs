using System.Drawing;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Holds the shared appearance settings for the dialogs and applies them to a form. Shared by
/// composition rather than an internal base class, because a public dialog type cannot derive from an
/// internal one (CS0060).</summary>
internal sealed class DialogChrome
{
	/// <summary>The window title. Defaults to "EFTPOS".</summary>
	public string WindowTitle { get; set; } = "EFTPOS";

	/// <summary>An optional logo image.</summary>
	public Image? Logo { get; set; }

	/// <summary>The dialog background colour. Defaults to pure white (not <see cref="SystemColors.Window"/>,
	/// which a high-contrast or dark theme can repaint) for a consistent modern look; callers can override.</summary>
	public Color BackgroundColour { get; set; } = Color.White;

	/// <summary>The dialog foreground (text) colour.</summary>
	public Color ForegroundColour { get; set; } = SystemColors.ControlText;

	/// <summary>The dialog font; null leaves the form default.</summary>
	public Font? Font { get; set; }

	/// <summary>Whether to disable the owner window while the dialog is busy. Defaults to true.</summary>
	public bool DisableOwnerWhileBusy { get; set; } = true;

	/// <summary>Applies the current settings to the form (and its logo box, if any).</summary>
	public void ApplyTo(Form form, PictureBox? logoBox)
	{
		form.Text = WindowTitle;
		form.BackColor = BackgroundColour;
		form.ForeColor = ForegroundColour;
		if (Font != null)
		{
			form.Font = Font;
		}

		if (logoBox != null)
		{
			logoBox.Image = Logo;
			logoBox.Visible = Logo != null;
		}
	}
}
