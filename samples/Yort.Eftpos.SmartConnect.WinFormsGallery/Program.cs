using System;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinFormsGallery;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.Run(new GalleryForm());
	}
}
