using OSImageDeploy.Contracts;
using System.ComponentModel;

namespace Models
{
	internal sealed class WinPeDriverPackageSelectionModel :
		INotifyPropertyChanged
	{
		private Boolean _isSelected;

		public required WinPeDriverPackageDescriptor Package { get; init; }

		public Boolean IsSelected
		{
			get => _isSelected;
			set
			{
				if (_isSelected == value || !Package.IsAvailable)
				{
					return;
				}

				_isSelected = value;
				PropertyChanged?.Invoke(
					this,
					new PropertyChangedEventArgs(nameof(IsSelected)));
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;
	}
}
