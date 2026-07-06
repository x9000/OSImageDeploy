using OSImageDeploy.Views;
using System.Configuration;
using System.Data;
using System.Windows;

namespace OSImageDeploy
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			if(Environment.SystemDirectory.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
			{
				//Launch Restore page
			}
			else
			{
				//Launch USB Creator Window
				Window mainUSBCreatorWindow = new MainUSBCreatorWindow();
				mainUSBCreatorWindow.Show();
			}
		}
	}
}
