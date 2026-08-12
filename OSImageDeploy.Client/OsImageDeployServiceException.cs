namespace OSImageDeploy.Client
{
	public sealed class OsImageDeployServiceException : Exception
	{
		public OsImageDeployServiceException(
			String message,
			Exception innerException)
			: base(message, innerException)
		{
		}
	}
}
