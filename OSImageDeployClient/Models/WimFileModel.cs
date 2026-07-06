#nullable disable

namespace Models
{
	using System;
	using System.Collections.ObjectModel;
	using System.IO;

	public class WimFileModel : BaseModel
	{
		private String _filePath;
		private ObservableCollection<WimImageModel> _images;

		public String FilePath
		{
			get
			{
				return _filePath;
			}
			set
			{
				if (_filePath != value)
				{
					_filePath = value;
					NotifyPropertyChanged();
					NotifyPropertyChanged(nameof(FileName));
					NotifyPropertyChanged(nameof(Exists));
				}
			}
		}

		public String FileName
		{
			get
			{
				if (String.IsNullOrWhiteSpace(FilePath))
				{
					return String.Empty;
				}

				return Path.GetFileName(FilePath);
			}
		}

		public Boolean Exists
		{
			get
			{
				return !String.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
			}
		}

		public ObservableCollection<WimImageModel> Images
		{
			get
			{
				return _images;
			}
			set
			{
				if (_images != value)
				{
					_images = value;
					NotifyPropertyChanged();
				}
			}
		}

		public WimFileModel()
		{
			Images = new ObservableCollection<WimImageModel>();
		}
	}
}