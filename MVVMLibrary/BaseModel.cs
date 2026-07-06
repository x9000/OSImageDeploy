using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Models
{
	public class BaseModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		//private object sender;

		//private PropertyChangedEventArgs e;
		// This method is called by the Set accessor of each property.  
		// The CallerMemberName attribute that is applied to the optional propertyName  
		// parameter causes the property name of the caller to be substituted as an argument.  
		public void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
