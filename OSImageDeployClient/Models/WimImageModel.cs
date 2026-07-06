#nullable disable

namespace Models
{
	using System;

	public class WimImageModel : BaseModel
	{
		private Int32 _index;
		private String _name;
		private String _description;
		private String _architecture;
		private UInt64 _sizeBytes;
		//private string _filePath;

		public Int32 Index
		{
			get
			{
				return _index;
			}
			set
			{
				if (_index != value)
				{
					_index = value;
					NotifyPropertyChanged();
				}
			}
		}

		public String Name
		{
			get
			{
				return _name;
			}
			set
			{
				if (_name != value)
				{
					_name = value;
					NotifyPropertyChanged();
				}
			}
		}

		//public String FilePath
		//{
		//	get
		//	{
		//		return _filePath;
		//	}
		//	set
		//	{
		//		if (_filePath != value)
		//		{
		//			_filePath = value;
		//			NotifyPropertyChanged();
		//		}
		//	}
		//}

		public String Description
		{
			get
			{
				return _description;
			}
			set
			{
				if (_description != value)
				{
					_description = value;
					NotifyPropertyChanged();
				}
			}
		}

		public String Architecture
		{
			get
			{
				return _architecture;
			}
			set
			{
				if (_architecture != value)
				{
					_architecture = value;
					NotifyPropertyChanged();
				}
			}
		}

		public UInt64 SizeBytes
		{
			get
			{
				return _sizeBytes;
			}
			set
			{
				if (_sizeBytes != value)
				{
					_sizeBytes = value;
					NotifyPropertyChanged();
					NotifyPropertyChanged(nameof(DisplaySize));
				}
			}
		}

		public String DisplaySize
		{
			get
			{
				Double gb = SizeBytes / 1024d / 1024d / 1024d;
				return gb.ToString("0.00") + " GB";
			}
		}
	}
}