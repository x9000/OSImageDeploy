#nullable disable

namespace ViewModels
{
	using Imaging;
	using Microsoft.Win32;
	using Models;
	using System;
	using System.Collections.ObjectModel;
	using System.IO;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Windows.Input;
	using System.Xml.Linq;

	public class WimSelectionWindowViewModel : BaseViewModel
	{
		private readonly WimImageService _wimImageService;
		private CancellationTokenSource _cancellationTokenSource;
		private String _selectedWimFilePath;
		private WimImageModel _selectedImage;
		private String _statusMessage;
		private Boolean _isBusy;

		public event EventHandler<WimSelectionDialogResult> RequestClose;

		public ObservableCollection<WimImageModel> Images { get; }
		public ObservableCollection<String> AvailableWimFiles { get; }

		public ICommand BrowseCommand { get; }
		public ICommand LoadImagesCommand { get; }
		public ICommand OkCommand { get; }
		public ICommand CancelCommand { get; }
		public RelayCommand WindowLoadedCommand { get; }

		//public String SelectedWimFilePath
		//{
		//	get
		//	{
		//		return _selectedWimFilePath;
		//	}
		//	set
		//	{
		//		if (_selectedWimFilePath != value)
		//		{
		//			_selectedWimFilePath = value;
		//			NotifyPropertyChanged();
		//			NotifyPropertyChanged(nameof(CanUseSelectedImage));

		//			((RelayCommand)LoadImagesCommand).RaiseCanExecuteChanged();
		//			((RelayCommand)OkCommand).RaiseCanExecuteChanged();
		//		}
		//	}
		//}
		public String SelectedWimFilePath
		{
			get
			{
				return _selectedWimFilePath;
			}
			set
			{
				if (_selectedWimFilePath != value)
				{
					_selectedWimFilePath = value;
					NotifyPropertyChanged();
					NotifyPropertyChanged(nameof(CanUseSelectedImage));

					((RelayCommand)LoadImagesCommand).RaiseCanExecuteChanged();
					((RelayCommand)OkCommand).RaiseCanExecuteChanged();
					_ = LoadImagesAsync();
				}
			}
		}

		public WimImageModel SelectedImage
		{
			get
			{
				return _selectedImage;
			}
			set
			{
				if (_selectedImage != value)
				{
					_selectedImage = value;
					NotifyPropertyChanged();
					NotifyPropertyChanged(nameof(CanUseSelectedImage));
					NotifyPropertyChanged(nameof(SelectedImageSummary));

					((RelayCommand)OkCommand).RaiseCanExecuteChanged();
				}
			}
		}

		public String StatusMessage
		{
			get
			{
				return _statusMessage;
			}
			set
			{
				if (_statusMessage != value)
				{
					_statusMessage = value;
					NotifyPropertyChanged();
				}
			}
		}

		public Boolean IsBusy
		{
			get
			{
				return _isBusy;
			}
			set
			{
				if (_isBusy != value)
				{
					_isBusy = value;
					NotifyPropertyChanged();

					((RelayCommand)BrowseCommand).RaiseCanExecuteChanged();
					((RelayCommand)LoadImagesCommand).RaiseCanExecuteChanged();
					((RelayCommand)OkCommand).RaiseCanExecuteChanged();
				}
			}
		}

		public String ImageCountText
		{
			get
			{
				if (Images.Count == 0)
				{
					return "No images loaded";
				}

				if (Images.Count == 1)
				{
					return "1 image found";
				}

				return Images.Count + " images found";
			}
		}

		public String SelectedImageSummary
		{
			get
			{
				if (SelectedImage == null)
				{
					return "No image selected.";
				}

				return "Selected: Index " + SelectedImage.Index + " - " + SelectedImage.Name;
			}
		}

		public Boolean CanUseSelectedImage
		{
			get
			{
				return !IsBusy &&
					   !String.IsNullOrWhiteSpace(SelectedWimFilePath) &&
					   File.Exists(SelectedWimFilePath) &&
					   SelectedImage != null &&
					   SelectedImage.Index > 0;
			}
		}

		public WimSelectionWindowViewModel() : this(null)
		{
		}

		public WimSelectionWindowViewModel(String initialWimFilePath)
		{
			_wimImageService = new WimImageService();
			_cancellationTokenSource = new CancellationTokenSource();

			Images = new ObservableCollection<WimImageModel>();

			BrowseCommand = new RelayCommand(Browse, CanBrowse);
			LoadImagesCommand = new RelayCommand(async () => await LoadImagesAsync(), CanLoadImages);
			OkCommand = new RelayCommand(Ok, CanOk);
			CancelCommand = new RelayCommand(Cancel);
			WindowLoadedCommand = new RelayCommand(execute: WindowLoadedCommandHandler);
			AvailableWimFiles = new ObservableCollection<string>();
			StatusMessage = "Choose a WIM file to continue.";
		}

		private void WindowLoadedCommandHandler()
		{
			//if (!String.IsNullOrWhiteSpace(SelectedWimFilePath))
			//{
			//	_ = LoadImagesAsync();
			//}
		}

		private Boolean CanBrowse()
		{
			return !IsBusy;
		}

		private Boolean CanLoadImages()
		{
			return !IsBusy &&
				   !String.IsNullOrWhiteSpace(SelectedWimFilePath) &&
				   File.Exists(SelectedWimFilePath);
		}

		private Boolean CanOk()
		{
			return CanUseSelectedImage;
		}

		private void Browse()
		{
			OpenFileDialog dialog = new OpenFileDialog
			{
				Title = "Select Windows Image",
				Filter = "Windows Image Files (*.wim;*.esd)|*.wim;*.esd|WIM Files (*.wim)|*.wim|ESD Files (*.esd)|*.esd|All Files (*.*)|*.*",
				CheckFileExists = true,
				CheckPathExists = true,
				Multiselect = false
			};

			Boolean? result = dialog.ShowDialog();

			if (result == true)
			{
				SelectedWimFilePath = dialog.FileName;
				Images.Clear();
				SelectedImage = null;
				NotifyPropertyChanged(nameof(ImageCountText));
				NotifyPropertyChanged(nameof(SelectedImageSummary));

				StatusMessage = "WIM file selected. Click Load Images to read available images.";
			}
		}

		private async Task LoadImagesAsync()
		{
			if (String.IsNullOrWhiteSpace(SelectedWimFilePath))
			{
				StatusMessage = "No WIM file has been selected.";
				return;
			}

			if (!File.Exists(SelectedWimFilePath))
			{
				StatusMessage = "The selected WIM file does not exist.";
				return;
			}

			try
			{
				IsBusy = true;
				StatusMessage = "Reading WIM metadata...";

				Images.Clear();
				SelectedImage = null;
				NotifyPropertyChanged(nameof(ImageCountText));
				NotifyPropertyChanged(nameof(SelectedImageSummary));

				XDocument document = await _wimImageService.GetWimInfoAsync(
					SelectedWimFilePath,
					_cancellationTokenSource.Token);

				Collection<WimImageModel> images = ParseImages(document);

				foreach (WimImageModel image in images)
				{
					Images.Add(image);
				}

				if (Images.Count > 0)
				{
					SelectedImage = Images[0];
					StatusMessage = "Images loaded successfully.";
				}
				else
				{
					StatusMessage = "No images were found in the selected WIM file.";
				}

				NotifyPropertyChanged(nameof(ImageCountText));
				NotifyPropertyChanged(nameof(SelectedImageSummary));
			}
			catch (OperationCanceledException)
			{
				StatusMessage = "Reading WIM metadata was cancelled.";
			}
			catch (Exception ex)
			{
				StatusMessage = "Failed to read WIM metadata: " + ex.Message;
			}
			finally
			{
				IsBusy = false;
			}
		}

		private Collection<WimImageModel> ParseImages(XDocument document)
		{
			Collection<WimImageModel> images = new Collection<WimImageModel>();

			if (document == null || document.Root == null)
			{
				return images;
			}

			foreach (XElement imageElement in document.Descendants("IMAGE"))
			{
				WimImageModel image = new WimImageModel
				{
					Index = ReadIntAttribute(imageElement, "INDEX"),
					Name = ReadStringElement(imageElement, "NAME"),
					Description = ReadStringElement(imageElement, "DESCRIPTION"),
					Architecture = ConvertArchitecture(ReadStringElement(imageElement, "WINDOWS/ARCH")),
					SizeBytes = ReadUInt64Element(imageElement, "TOTALBYTES")
				};

				if (String.IsNullOrWhiteSpace(image.Name))
				{
					image.Name = "Image " + image.Index;
				}

				images.Add(image);
			}

			return images;
		}

		private Int32 ReadIntAttribute(XElement element, String attributeName)
		{
			if (element == null)
			{
				return 0;
			}

			XAttribute attribute = element.Attribute(attributeName);

			if (attribute == null)
			{
				return 0;
			}

			Int32 value;

			if (Int32.TryParse(attribute.Value, out value))
			{
				return value;
			}

			return 0;
		}

		private UInt64 ReadUInt64Element(XElement element, String elementPath)
		{
			String valueText = ReadStringElement(element, elementPath);
			UInt64 value;

			if (UInt64.TryParse(valueText, out value))
			{
				return value;
			}

			return 0;
		}

		private String ReadStringElement(XElement element, String elementPath)
		{
			if (element == null || String.IsNullOrWhiteSpace(elementPath))
			{
				return String.Empty;
			}

			String[] parts = elementPath.Split('/');
			XElement current = element;

			foreach (String part in parts)
			{
				current = current.Element(part);

				if (current == null)
				{
					return String.Empty;
				}
			}

			return current.Value ?? String.Empty;
		}

		private String ConvertArchitecture(String architectureValue)
		{
			if (String.IsNullOrWhiteSpace(architectureValue))
			{
				return String.Empty;
			}

			if (architectureValue == "0")
			{
				return "x86";
			}

			if (architectureValue == "9")
			{
				return "x64";
			}

			if (architectureValue == "12")
			{
				return "ARM64";
			}

			return architectureValue;
		}

		private void Ok()
		{
			if (!CanUseSelectedImage)
			{
				return;
			}
			RequestClose?.Invoke(this, new WimSelectionDialogResult
			{
				Accepted = true,
				WimFilePath = SelectedWimFilePath,
				ImageIndex = SelectedImage.Index,
				ImageName = SelectedImage.Name
			});
		}

		private void Cancel()
		{
			_cancellationTokenSource.Cancel();

			RequestClose?.Invoke(this, new WimSelectionDialogResult
			{
				Accepted = false
			});
		}
	}
}