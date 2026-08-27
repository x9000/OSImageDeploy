using Microsoft.Win32;
using OSImageDeploy.Client;
using OSImageDeploy.Contracts;
using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;

namespace OSImageDeploy.Views
{
	public partial class WinPeDriverPackagePreparationWindow : Window
	{
		private readonly WinPeDriverPackageDescriptor _package;
		private CancellationTokenSource? _cancellationTokenSource;
		private Boolean _isPreparing;

		public WinPeDriverPackagePreparationWindow(
			WinPeDriverPackageDescriptor package)
		{
			_package = package ??
				throw new ArgumentNullException(nameof(package));

			InitializeComponent();

			PackageNameTextBlock.Text = package.DisplayName;
			ExplanationTextBlock.Text =
				$"Select the {package.PreparationFileExtension.ToUpperInvariant()} " +
				"file downloaded from the manufacturer's official site. " +
				"The desktop application remains non-elevated; the local Windows " +
				"service performs the protected package-store work.";

			ServiceActionTextBlock.Text = package.PackageId == "hp-winpe"
				? "The service copies the SoftPaq into protected staging, verifies a " +
					"valid HP Authenticode signature, runs the supported HP extractor, " +
					"checks for WinPE INF files, and installs an atomic package."
				: "The service copies the CAB into protected staging, extracts it with " +
					"the Windows CAB tool, checks for WinPE INF files, and installs an " +
					"atomic package.";
		}

		private void BrowseButtonClick(
			object sender,
			RoutedEventArgs e)
		{
			String extension = _package.PreparationFileExtension;
			OpenFileDialog dialog = new()
			{
				Title = $"Select {_package.DisplayName} download",
				CheckFileExists = true,
				Multiselect = false,
				DefaultExt = extension,
				Filter = extension.Equals(
					".cab",
					StringComparison.OrdinalIgnoreCase)
					? "CAB files (*.cab)|*.cab"
					: "Executable files (*.exe)|*.exe"
			};

			if (dialog.ShowDialog(this) == true)
			{
				SourcePathTextBox.Text = dialog.FileName;
			}
		}

		private async void PrepareButtonClick(
			object sender,
			RoutedEventArgs e)
		{
			if (String.IsNullOrWhiteSpace(SourcePathTextBox.Text))
			{
				MessageBox.Show(
					this,
					"Select the manufacturer download first.",
					"Prepare WinPE Drivers",
					MessageBoxButton.OK,
					MessageBoxImage.Information);
				return;
			}

			Boolean replaceConfirmed = false;

			if (_package.IsAvailable)
			{
				MessageBoxResult result = MessageBox.Show(
					this,
					"This manufacturer package is already prepared. " +
					"Replace it with the selected download? The next USB build will " +
					"rebuild WinPE if the package contents changed.",
					"Replace WinPE Driver Package",
					MessageBoxButton.YesNo,
					MessageBoxImage.Question);

				if (result != MessageBoxResult.Yes)
				{
					return;
				}

				replaceConfirmed = true;
			}

			SetPreparingState(isPreparing: true);
			StatusTextBlock.Text =
				"Preparing the driver package. Large downloads can take several minutes.";
			_cancellationTokenSource = new CancellationTokenSource();

			try
			{
				using OsImageDeployServiceClient client = new();
				WinPeDriverPackageDescriptor preparedPackage =
					await client.PrepareWinPeDriverPackageAsync(
						_package.PackageId,
						SourcePathTextBox.Text,
						String.Empty,
						replaceConfirmed,
						_cancellationTokenSource.Token);

				MessageBox.Show(
					this,
					$"{preparedPackage.DisplayName} is ready. " +
					$"{preparedPackage.DriverCount} INF files were validated.",
					"WinPE Drivers Ready",
					MessageBoxButton.OK,
					MessageBoxImage.Information);

				SetPreparingState(isPreparing: false);
				DialogResult = true;
			}
			catch (OperationCanceledException)
			{
				StatusTextBlock.Text = "Package preparation was cancelled.";
			}
			catch (Exception exception)
			{
				StatusTextBlock.Text = "Package preparation did not complete.";
				MessageBox.Show(
					this,
					exception.Message,
					"Prepare WinPE Drivers",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
			finally
			{
				_cancellationTokenSource?.Dispose();
				_cancellationTokenSource = null;
				SetPreparingState(isPreparing: false);
			}
		}

		private void CancelButtonClick(
			object sender,
			RoutedEventArgs e)
		{
			if (_isPreparing)
			{
				StatusTextBlock.Text = "Cancelling package preparation...";
				_cancellationTokenSource?.Cancel();
				return;
			}

			DialogResult = false;
		}

		private void SetPreparingState(Boolean isPreparing)
		{
			_isPreparing = isPreparing;
			BrowseButton.IsEnabled = !isPreparing;
			PrepareButton.IsEnabled = !isPreparing;
			PreparationProgressBar.Visibility = isPreparing
				? Visibility.Visible
				: Visibility.Collapsed;
			CancelButton.Content = isPreparing ? "Cancel preparation" : "Cancel";
		}

		private void WindowClosing(
			object? sender,
			CancelEventArgs e)
		{
			if (!_isPreparing)
			{
				return;
			}

			e.Cancel = true;
			StatusTextBlock.Text = "Cancelling package preparation...";
			_cancellationTokenSource?.Cancel();
		}
	}
}
