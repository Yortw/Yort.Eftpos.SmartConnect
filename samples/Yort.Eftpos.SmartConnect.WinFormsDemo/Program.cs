using System;
using System.Windows.Forms;

namespace Yort.Eftpos.SmartConnect.WinFormsDemo;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.Run(new MainForm());
	}
}
