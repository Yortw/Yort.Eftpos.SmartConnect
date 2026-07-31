using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Disables an owner window while the dialog is busy and restores it afterwards, giving
/// modal-like behaviour without a thread-blocking <c>ShowDialog</c>. A null owner is a no-op (the
/// dialog simply centres on screen). Owner enable/disable is reference-counted per owner, so two dialogs
/// sharing one owner disable it once and re-enable it only when the last one releases — otherwise, since
/// <c>EnableWindow</c> is absolute (no nesting), the first dialog to finish would re-enable the owner while
/// the second is still busy. The count relies on every <see cref="Disable"/> being matched by a
/// <see cref="Restore"/>, which the dialogs guarantee via their finally/dispose paths; a missed
/// <see cref="Restore"/> only affects that same owner and is cleaned up when the owner is collected.</summary>
internal sealed class OwnerController
{
	// Disable depth keyed on the owner OBJECT (not its HWND). Keying on the object — via a weak table so the
	// entry dies with the owner — matters for two reasons: Windows reuses HWND values, so a leaked count keyed
	// on a handle could silently alias onto an unrelated future window and skip disabling it mid-transaction;
	// and the object survives a handle recreation. Each owner's depth box doubles as its own lock, so distinct
	// owners never contend (only dialogs sharing one owner do). Shared across all controllers for that owner.
	private static readonly ConditionalWeakTable<IWin32Window, StrongBox<int>> _disableDepthByOwner
		= new ConditionalWeakTable<IWin32Window, StrongBox<int>>();

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

		if (_owner == null || !_disableWhileBusy())
		{
			return;
		}

		IntPtr handle;
		try
		{
			handle = _owner.Handle;
		}
		catch (ObjectDisposedException)
		{
			// The owner was disposed before the dialog's first show; Handle on a disposed control throws.
			// Disable() runs inside a Progress<T>-posted callback, so letting this escape is an unhandled
			// UI-thread exception mid-transaction. Nothing was disabled (_disabled stays false), so a later
			// Restore() correctly no-ops.
			return;
		}

		var depth = _disableDepthByOwner.GetValue(_owner, _ => new StrongBox<int>(0));
		lock (depth)
		{
			// First disabler for this owner does the actual disable; inner disablers just hold a reference.
			// The handle was read above (raw IntPtr), so the native call cannot throw ObjectDisposedException.
			if (depth.Value == 0)
			{
				_setWindowEnabled(handle, false);
			}

			depth.Value++;
		}

		_disabled = true;
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

		if (_owner == null || !_disableDepthByOwner.TryGetValue(_owner, out var depth))
		{
			return;
		}

		lock (depth)
		{
			if (depth.Value > 1)
			{
				// Another dialog still has the owner disabled — just release our reference; do NOT re-enable.
				depth.Value--;
				return;
			}

			// Last disabler releases — re-enable the owner through its LIVE Handle so a disposed owner is
			// handled as before: a disposed window is already re-enabled by the OS, so the throw is swallowed
			// and no re-enable is issued. The box is left at zero (a future Disable re-uses it); it is collected
			// with the owner. The native call runs inside the per-owner lock deliberately — moving it out risks
			// a racing enable/disable applying in the wrong order and leaving the owner mis-stated (a concern
			// only for a dialog created on a different thread from its owner, which is atypical).
			depth.Value = 0;
			try
			{
				_setWindowEnabled(_owner.Handle, true);
			}
			catch (ObjectDisposedException)
			{
				// The owner was disposed while the dialog was busy (owner closed / app shutdown mid-transaction),
				// so accessing its Handle throws. Nothing to restore; escaping would mask the result or bubble
				// out of Dispose().
			}
		}
	}
}
