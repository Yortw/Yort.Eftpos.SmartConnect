using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Yort.Eftpos.SmartConnect.WinForms;

/// <summary>Native interop. Used to enable/disable an owner window for modal-like behaviour without a blocking
/// <c>ShowDialog</c>, and to read the owner's window bounds for owner-centred placement.</summary>
internal static class NativeMethods
{
	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsIconic(IntPtr hWnd);

	/// <summary>Enables or disables mouse and keyboard input to the given window. <c>EnableWindow</c>'s return
	/// value is the window's <em>previous</em> enabled state, not a success flag, and is intentionally not
	/// surfaced — callers only care about the new state, and a disabled owner is restored on all paths regardless.
	/// <c>SetLastError</c> is enabled so a caller could inspect <see cref="System.Runtime.InteropServices.Marshal.GetLastWin32Error"/> if needed.</summary>
	public static void SetWindowEnabled(IntPtr handle, bool enabled)
	{
		EnableWindow(handle, enabled);
	}

	/// <summary>Gets the given window's bounds in screen coordinates. Returns false for a dead/invalid
	/// handle (e.g. the window was destroyed) — callers fall back to default placement.</summary>
	public static bool TryGetWindowBounds(IntPtr handle, out Rectangle bounds)
	{
		if (GetWindowRect(handle, out var rect))
		{
			bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
			return true;
		}

		bounds = Rectangle.Empty;
		return false;
	}

	/// <summary>Whether the given window is minimised. <c>GetWindowRect</c> reports the off-screen
	/// icon position (around -32000) for a minimised window, so centring on it would be meaningless.</summary>
	public static bool IsWindowMinimized(IntPtr handle)
	{
		return IsIconic(handle);
	}
}
