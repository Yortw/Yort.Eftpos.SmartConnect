using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>A reusable WinForms dialog that onboards a terminal: it prompts for the pairing code, runs
/// the pairing attempt via a caller-supplied callback, presents the result, and lets the operator retry
/// a bad code or cancel. Construct it on the UI thread. The dialog depends on no client type — only on
/// the callback that turns an entered code into a <see cref="SmartConnectPairingResult"/>.</summary>
public sealed class SmartConnectPairingDialog : IDisposable
{
	private readonly PairingForm _form;
	private readonly DialogChrome _chrome = new DialogChrome { WindowTitle = "Pair Terminal" };
	private readonly OwnerController _owner;
	private readonly PairingController _controller = new PairingController();
	private bool _appearanceApplied;

	/// <summary>Creates an owner-less dialog (centres on screen).</summary>
	public SmartConnectPairingDialog()
		: this(null)
	{
	}

	/// <summary>Creates a dialog owned by <paramref name="owner"/> (centres on it; disables it while busy).</summary>
	public SmartConnectPairingDialog(IWin32Window? owner)
	{
		_form = new PairingForm();
		// Delegate, not value: DisableOwnerWhileBusy is settable post-construction (see progress dialog).
		_owner = new OwnerController(owner, () => _chrome.DisableOwnerWhileBusy, NativeMethods.SetWindowEnabled);
		// Owner-centring at Load (first show), when the form's size is final (see progress dialog).
		_form.Load += (_, _) => OwnerPlacement.TryApply(_form, owner);
	}

	/// <summary>The window title (default "Pair Terminal").</summary>
	public string WindowTitle { get => _chrome.WindowTitle; set => _chrome.WindowTitle = value; }

	/// <summary>An optional logo image.</summary>
	public Image? Logo { get => _chrome.Logo; set => _chrome.Logo = value; }

	/// <summary>The background colour.</summary>
	public Color BackgroundColour { get => _chrome.BackgroundColour; set => _chrome.BackgroundColour = value; }

	/// <summary>The foreground (text) colour.</summary>
	public Color ForegroundColour { get => _chrome.ForegroundColour; set => _chrome.ForegroundColour = value; }

	/// <summary>The dialog font.</summary>
	public Font? Font { get => _chrome.Font; set => _chrome.Font = value; }

	/// <summary>Whether to disable the owner window while busy (default true).</summary>
	public bool DisableOwnerWhileBusy { get => _chrome.DisableOwnerWhileBusy; set => _chrome.DisableOwnerWhileBusy = value; }

	/// <summary>The prompt text shown above the code field.</summary>
	public string Prompt { get => _form.PromptText; set => _form.PromptText = value; }

	/// <summary>Runs the pairing flow. Returns the successful result, or null if the operator cancelled.
	/// The callback is invoked with the (non-blank, trimmed) entered code for each attempt.</summary>
	public async Task<SmartConnectPairingResult?> ShowAsync(Func<string, Task<SmartConnectPairingResult>> pairWithCode)
	{
		if (pairWithCode == null)
		{
			throw new ArgumentNullException(nameof(pairWithCode));
		}

		EnsureAppearanceAndOwner();
		try
		{
			return await _controller.RunAsync(_form, pairWithCode).ConfigureAwait(true);
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
		_chrome.ApplyTo(_form, _form.LogoBox);
		_owner.Disable();
	}

	/// <summary>Closes the dialog and re-enables the owner window.</summary>
	public void Dispose()
	{
		_owner.Restore();
		_form.Dispose();
	}
}
