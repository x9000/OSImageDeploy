#nullable disable

namespace Models
{
	using System;

	public class WimSelectionDialogResult
	{
		public Boolean Accepted { get; set; }
		public String WimFilePath { get; set; }
		public Int32 ImageIndex { get; set; }
		public String ImageName { get; set; }
	}
}
