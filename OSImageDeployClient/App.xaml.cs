using OSImageDeployClient.Views;
using System.Configuration;
using System.Data;
using System.Windows;

namespace OSImageDeployClient
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		public App()
		{
			this.DispatcherUnhandledException += App_DispatcherUnhandledException;
			TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
			AppDomain currentDomain = AppDomain.CurrentDomain;
			currentDomain.UnhandledException += new UnhandledExceptionEventHandler(AppUnhandledExceptionEventHandler);
		}
		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			Window deploymentWindow = new DeploymentWindow();
			deploymentWindow.ShowDialog();
		}

		private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
		{
			Console.WriteLine($"An unhandled exception occurred: {e.Exception.ToString()}");
			MessageBox.Show($"An unhandled exception occurred: {e.Exception.ToString()}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		private void AppUnhandledExceptionEventHandler(object sender, UnhandledExceptionEventArgs e)
		{
			Console.WriteLine($"An unhandled exception occurred: {e.ExceptionObject.ToString()}");
			MessageBox.Show($"An unhandled exception occurred: {e.ExceptionObject.ToString()}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
		{
			Console.WriteLine($"An unhandled exception occurred: {e.Exception.Message}");
			MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			//e.Handled = true; // Prevents the application from crashingthrow new NotImplementedException();
		}
	}

}
