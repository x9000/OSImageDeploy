using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Navigation;
using System.Diagnostics;
using OSImageDeploy.Contracts;
using ViewModels;

namespace OSImageDeploy.Views
{
	/// <summary>
	/// Interaction logic for MainUSBCreatorWindow.xaml
	/// </summary>
	public partial class MainUSBCreatorWindow : Window
	{
		#region "Constructor"
		public MainUSBCreatorWindow()
		{
			InitializeComponent();
		}
		#endregion

		private void OpenExternalLink(
			object sender,
			RequestNavigateEventArgs e)
		{
			if (e.Uri == null ||
				!e.Uri.IsAbsoluteUri ||
				e.Uri.Scheme != Uri.UriSchemeHttps)
			{
				return;
			}

			Process.Start(
				new ProcessStartInfo(e.Uri.AbsoluteUri)
				{
					UseShellExecute = true
				});
			e.Handled = true;
		}

		private async void PreparePackageClick(
			object sender,
			RoutedEventArgs e)
		{
			if (sender is not Button button ||
				button.Tag is not WinPeDriverPackageDescriptor package)
			{
				return;
			}

			WinPeDriverPackagePreparationWindow dialog = new(package)
			{
				Owner = this
			};

			if (dialog.ShowDialog() == true &&
				DataContext is MainUSBCreatorWindowViewModel viewModel)
			{
				await viewModel.RefreshWinPeDriverPackagesAsync(
					package.PackageId);
			}
		}

		private void OpenDriverSupportClick(
			object sender,
			RoutedEventArgs e)
		{
			WinPeDriverSupportWindow dialog = new()
			{
				Owner = this
			};
			dialog.ShowDialog();
		}
    }
}
