using Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Models
{
	internal class PreCheckModel : BaseModel
	{
		public String Text { get; set; } = "";
		private Boolean _IsChecked;

		public Boolean IsChecked
		{
			get
			{
				return _IsChecked;
			}
			set 
			{
				_IsChecked = value;
				NotifyPropertyChanged(nameof(IsChecked));
			}
		}

	}
}
