#nullable disable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using ViewModels;

namespace Models
{
    class LogItemModel : BaseViewModel
    {
		private Visibility _visibility;

		public DateTime Timestamp { get; set; }

		public String Level { get; set; }

		public String Message { get; set; }

		public Brush LevelBrush
		{
			get
			{
				if (Level == "ERROR")
				{
					return Brushes.IndianRed;
				}

				if (Level == "WARN")
				{
					return Brushes.Gold;
				}

				if (Level == "SUCCESS")
				{
					return Brushes.LightGreen;
				}

				return Brushes.LightSkyBlue;
			}
		}

		public Visibility Visibility
		{
			get
			{
				return _visibility;
			}
			set
			{
				if (_visibility != value)
				{
					_visibility = value;
					NotifyPropertyChanged(nameof(Visibility));
				}
			}
		}

		public LogItemModel()
		{
			Timestamp = DateTime.Now;
			Visibility = Visibility.Visible;
		}
	}
}
