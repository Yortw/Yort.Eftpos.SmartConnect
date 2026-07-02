using System;
using System.Drawing;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Positions a dialog centred over its owner window. WinForms' <c>CenterParent</c> only works for
/// modal <c>ShowDialog</c> with <c>Owner</c> set; these dialogs are shown non-modally with no owner (the
/// <see cref="OwnerController"/> design), so owner-centring is manual placement.</summary>
internal static class OwnerPlacement
{
	/// <summary>
	/// Centres <paramref name="form"/> over <paramref name="owner"/>'s window, clamped into that window's
	/// monitor's working area. Call at the form's <c>Load</c> (first show) so the form's FINAL size is used —
	/// the receipt and pairing forms size themselves to content before/while showing. Leaves the form's
	/// default (<c>CenterScreen</c>) placement untouched when there is no usable owner: null, minimised
	/// (<c>GetWindowRect</c> reports off-screen coords for iconic windows), disposed, or a dead handle.
	/// </summary>
	public static void TryApply(Form form, IWin32Window? owner)
	{
		if (owner == null)
		{
			return;
		}

		try
		{
			var handle = owner.Handle;
			if (NativeMethods.IsWindowMinimized(handle) || !NativeMethods.TryGetWindowBounds(handle, out var ownerBounds))
			{
				return;
			}

			var workingArea = Screen.FromRectangle(ownerBounds).WorkingArea;
			// Manual marks the form as deliberately placed, so growth-time re-centres (PairingForm's
			// logo layout) know not to snap back to screen-centre.
			form.StartPosition = FormStartPosition.Manual;
			form.Location = CentredLocation(ownerBounds, form.Size, workingArea);
		}
		catch (ObjectDisposedException)
		{
			// The owner was disposed before the dialog's first show — same tolerance as OwnerController;
			// the form's default screen-centre placement stands.
		}
	}

	/// <summary>Computes the top-left location that centres a dialog of <paramref name="dialogSize"/> over
	/// <paramref name="ownerBounds"/>, clamped fully inside <paramref name="workingArea"/> (a dialog larger
	/// than the working area pins to its top-left so the controls stay reachable).</summary>
	internal static Point CentredLocation(Rectangle ownerBounds, Size dialogSize, Rectangle workingArea)
	{
		var x = ownerBounds.Left + ((ownerBounds.Width - dialogSize.Width) / 2);
		var y = ownerBounds.Top + ((ownerBounds.Height - dialogSize.Height) / 2);

		// Min-then-Max: when the dialog exceeds the working area the upper bound falls below the lower
		// bound, and applying Max last pins to the top-left (title bar/buttons reachable) by design.
		x = Math.Max(workingArea.Left, Math.Min(x, workingArea.Right - dialogSize.Width));
		y = Math.Max(workingArea.Top, Math.Min(y, workingArea.Bottom - dialogSize.Height));

		return new Point(x, y);
	}
}
