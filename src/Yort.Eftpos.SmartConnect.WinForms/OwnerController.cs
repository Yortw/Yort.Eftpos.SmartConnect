using System;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Disables an owner window while the dialog is busy and restores it afterwards, giving
/// modal-like behaviour without a thread-blocking <c>ShowDialog</c>. A null owner is a no-op (the
/// dialog simply centres on screen).</summary>
internal sealed class OwnerController
{
	private readonly IWin32Window? _owner;
	private readonly bool _disableWhileBusy;
	private readonly Action<IntPtr, bool> _setWindowEnabled;
	private bool _disabled;

	/// <summary>Creates a controller for the given owner.</summary>
	/// <param name="owner">The owner window, or null when there is none.</param>
	/// <param name="disableWhileBusy">Whether to disable the owner while busy.</param>
	/// <param name="setWindowEnabled">Action that enables/disables a window by handle.</param>
	public OwnerController(IWin32Window? owner, bool disableWhileBusy, Action<IntPtr, bool> setWindowEnabled)
	{
		_owner = owner;
		_disableWhileBusy = disableWhileBusy;
		_setWindowEnabled = setWindowEnabled;
	}

	/// <summary>Disables the owner if there is one and disabling is enabled. Idempotent.</summary>
	public void Disable()
	{
		if (_disabled)
		{
			return;
		}

		if (_owner != null && _disableWhileBusy)
		{
			_setWindowEnabled(_owner.Handle, false);
			_disabled = true;
		}
	}

	/// <summary>Re-enables the owner if (and only if) this controller disabled it. Idempotent.</summary>
	public void Restore()
	{
		if (!_disabled)
		{
			return;
		}

		_setWindowEnabled(_owner!.Handle, true);
		_disabled = false;
	}
}
