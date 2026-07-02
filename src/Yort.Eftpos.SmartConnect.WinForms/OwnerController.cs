using System;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Disables an owner window while the dialog is busy and restores it afterwards, giving
/// modal-like behaviour without a thread-blocking <c>ShowDialog</c>. A null owner is a no-op (the
/// dialog simply centres on screen).</summary>
internal sealed class OwnerController
{
	private readonly IWin32Window? _owner;
	private readonly Func<bool> _disableWhileBusy;
	private readonly Action<IntPtr, bool> _setWindowEnabled;
	private bool _disabled;

	/// <summary>Creates a controller for the given owner.</summary>
	/// <param name="owner">The owner window, or null when there is none.</param>
	/// <param name="disableWhileBusy">Whether to disable the owner while busy — a delegate, not a captured
	/// value, because the dialogs expose this as a settable property: it must be read when the disable
	/// actually happens, or setting it after construction is silently ignored.</param>
	/// <param name="setWindowEnabled">Action that enables/disables a window by handle.</param>
	public OwnerController(IWin32Window? owner, Func<bool> disableWhileBusy, Action<IntPtr, bool> setWindowEnabled)
	{
		_owner = owner;
		_disableWhileBusy = disableWhileBusy;
		_setWindowEnabled = setWindowEnabled;
	}

	/// <summary>Disables the owner if there is one and disabling is enabled. Idempotent, and safe to call
	/// after the owner window has been disposed (owner closed just as a transaction starts).</summary>
	public void Disable()
	{
		if (_disabled)
		{
			return;
		}

		if (_owner != null && _disableWhileBusy())
		{
			try
			{
				_setWindowEnabled(_owner.Handle, false);
				_disabled = true;
			}
			catch (ObjectDisposedException)
			{
				// The owner was disposed before the dialog's first show; Handle on a disposed control
				// throws. Disable() runs inside a Progress<T>-posted callback, so letting this escape is
				// an unhandled UI-thread exception mid-transaction. Nothing was disabled (_disabled stays
				// false), so a later Restore() correctly no-ops. Restore() has the matching guard.
			}
		}
	}

	/// <summary>Re-enables the owner if (and only if) this controller disabled it. Idempotent, and safe to call
	/// after the owner window has been disposed (e.g. host shutdown while a dialog was busy).</summary>
	public void Restore()
	{
		if (!_disabled)
		{
			return;
		}

		// Clear first: whether or not the native re-enable succeeds, this controller has done its part, and a
		// second Restore()/Dispose() must neither retry nor re-throw.
		_disabled = false;

		try
		{
			_setWindowEnabled(_owner!.Handle, true);
		}
		catch (ObjectDisposedException)
		{
			// The owner was disposed while the dialog was busy (owner closed / app shutdown mid-transaction), so
			// accessing its Handle throws. A disposed window is already re-enabled by the OS — there is nothing to
			// restore, and letting this escape would mask the real result or bubble out of Dispose().
		}
	}
}
