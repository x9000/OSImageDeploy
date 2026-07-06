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

namespace OSImageDeployClient.Views
{
	/// <summary>
	/// Interaction logic for WimSelectionWindow.xaml
	/// </summary>
	public partial class WimSelectionWindow : Window
	{
		public WimSelectionWindow()
		{
			InitializeComponent();
			WimSelectionVM.RequestClose += (s, e) =>
			{
				this.DialogResult = e.Accepted;
				this.Close();
			};
		}
	}
}
