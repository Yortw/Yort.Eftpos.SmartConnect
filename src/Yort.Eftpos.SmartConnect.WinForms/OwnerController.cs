using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Disables an owner window while the dialog is busy and restores it afterwards, giving
/// modal-like behaviour without a thread-blocking <c>ShowDialog</c>. A null owner is a no-op (the
/// dialog simply centres on screen). Owner enable/disable is reference-counted per owner handle across all
/// controllers, so two dialogs sharing one owner disable it once and re-enable it only when the last one
/// releases — otherwise, since <c>EnableWindow</c> is absolute (no nesting), the first dialog to finish would
/// re-enable the owner while the second is still busy. The count relies on every <see cref="Disable"/> being
/// matched by a <see cref="Restore"/>, which the dialogs guarantee via their finally/dispose paths.</summary>
internal sealed class OwnerController
{
	// Per-owner-handle disable depth, shared across all controllers. Guarded by its own lock because separate
	// owners may live on separate UI threads; entries are removed when the depth returns to zero.
	private static readonly Dictionary<IntPtr, int> _disableDepthByHandle = new Dictionary<IntPtr, int>();
	private static readonly object _depthLock = new object();

	private readonly IWin32Window? _owner;
	private readonly Func<bool> _disableWhileBusy;
	private readonly Action<IntPtr, bool> _setWindowEnabled;
	private bool _disabled;
	// The handle this controller counted against, captured at Disable — used as the count key at Restore even
	// if the owner is disposed by then (its live Handle would throw).
	private IntPtr _disabledHandle;

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

		lock (_depthLock)
		{
			_disableDepthByHandle.TryGetValue(handle, out var depth);
			if (depth == 0)
			{
				// First disabler for this owner does the actual disable. If the native call throws (owner
				// disposed between reading Handle and here), leave the depth at zero and _disabled false so
				// Restore() no-ops.
				try
				{
					_setWindowEnabled(handle, false);
				}
				catch (ObjectDisposedException)
				{
					return;
				}
			}

			_disableDepthByHandle[handle] = depth + 1;
		}

		_disabled = true;
		_disabledHandle = handle;
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

		lock (_depthLock)
		{
			if (!_disableDepthByHandle.TryGetValue(_disabledHandle, out var depth))
			{
				return;
			}

			if (depth > 1)
			{
				// Another dialog still has the owner disabled — just release our reference; do NOT re-enable.
				_disableDepthByHandle[_disabledHandle] = depth - 1;
				return;
			}

			// Last disabler releases — re-enable the owner. Go through the live owner Handle (not the captured
			// one) so a disposed owner is handled as before: a disposed window is already re-enabled by the OS,
			// so the throw is swallowed and no re-enable is issued.
			_disableDepthByHandle.Remove(_disabledHandle);
			try
			{
				_setWindowEnabled(_owner!.Handle, true);
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
