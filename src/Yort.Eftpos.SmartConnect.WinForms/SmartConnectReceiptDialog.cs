using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>A reusable WinForms dialog that displays an EFTPOS receipt (the fixed-width, newline-delimited
/// text from <c>SmartConnectTransactionResult.Receipt</c>) in a monospace font so its columns line up.
/// Construct it on the UI thread. It is a passive viewer: the caller decides <em>when</em> to show a
/// receipt (e.g. after an acquirer logon or settlement inquiry returns one) and passes the text to
/// <see cref="ShowAsync"/>.</summary>
public sealed class SmartConnectReceiptDialog : IDisposable
{
	private readonly ReceiptForm _form;
	private readonly DialogChrome _chrome = new DialogChrome { WindowTitle = "Receipt" };
	private readonly OwnerController _owner;
	private bool _appearanceApplied;

	/// <summary>Creates an owner-less dialog (centres on screen).</summary>
	public SmartConnectReceiptDialog()
		: this(null)
	{
	}

	/// <summary>Creates a dialog owned by <paramref name="owner"/> (centres on it; disables it while shown).</summary>
	public SmartConnectReceiptDialog(IWin32Window? owner)
	{
		_form = new ReceiptForm();
		// Delegate, not value: DisableOwnerWhileBusy is settable post-construction (see progress dialog).
		_owner = new OwnerController(owner, () => _chrome.DisableOwnerWhileBusy, NativeMethods.SetWindowEnabled);
		// Owner-centring at Load (first show), when the form has already sized itself to the receipt text.
		_form.Load += (_, _) => OwnerPlacement.TryApply(_form, owner);
	}

	/// <summary>The window title (default "Receipt").</summary>
	public string WindowTitle { get => _chrome.WindowTitle; set => _chrome.WindowTitle = value; }

	/// <summary>The background colour.</summary>
	public Color BackgroundColour { get => _chrome.BackgroundColour; set => _chrome.BackgroundColour = value; }

	/// <summary>The foreground (text) colour.</summary>
	public Color ForegroundColour { get => _chrome.ForegroundColour; set => _chrome.ForegroundColour = value; }

	/// <summary>The dialog font for the chrome (title, OK button). The receipt itself is always rendered in a
	/// monospace font regardless of this, since fixed-width receipts only align in one.</summary>
	public Font? Font { get => _chrome.Font; set => _chrome.Font = value; }

	/// <summary>Whether to disable the owner window while the dialog is shown (default true).</summary>
	public bool DisableOwnerWhileBusy { get => _chrome.DisableOwnerWhileBusy; set => _chrome.DisableOwnerWhileBusy = value; }

	/// <summary>Shows <paramref name="receipt"/> and returns when the operator dismisses it.</summary>
	/// <param name="receipt">The receipt text to display. Rendered as-is in a monospace font; an empty string shows an empty dialog.</param>
	/// <exception cref="ArgumentNullException"><paramref name="receipt"/> is null.</exception>
	public async Task ShowAsync(string receipt)
	{
		if (receipt == null)
		{
			throw new ArgumentNullException(nameof(receipt));
		}

		EnsureAppearanceAndOwner();
		try
		{
			await _form.ShowReceiptAsync(receipt).ConfigureAwait(true);
		}
		finally
		{
			_owner.Restore();
		}
	}

	private void EnsureAppearanceAndOwner()
	{
		if (_appearanceApplied)
		{
			return;
		}

		_appearanceApplied = true;
		_chrome.ApplyTo(_form, null);
		_owner.Disable();
	}

	/// <summary>Closes the dialog and re-enables the owner window.</summary>
	public void Dispose()
	{
		_owner.Restore();
		_form.Dispose();
	}
}
