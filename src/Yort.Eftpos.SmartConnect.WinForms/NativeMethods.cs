using System;
using System.Runtime.InteropServices;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Native interop. Used to enable/disable an owner window for modal-like behaviour without a blocking <c>ShowDialog</c>.</summary>
internal static class NativeMethods
{
	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

	/// <summary>Enables or disables mouse and keyboard input to the given window. <c>EnableWindow</c>'s return
	/// value is the window's <em>previous</em> enabled state, not a success flag, and is intentionally not
	/// surfaced — callers only care about the new state, and a disabled owner is restored on all paths regardless.
	/// <c>SetLastError</c> is enabled so a caller could inspect <see cref="System.Runtime.InteropServices.Marshal.GetLastWin32Error"/> if needed.</summary>
	public static void SetWindowEnabled(IntPtr handle, bool enabled)
	{
		EnableWindow(handle, enabled);
	}
}
