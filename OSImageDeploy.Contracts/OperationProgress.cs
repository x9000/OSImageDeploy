namespace OSImageDeploy.Contracts
{
	public sealed class OperationProgress
	{
		public String Stage { get; init; } = String.Empty;

		public String Message { get; init; } = String.Empty;

		public Int32? OverallPercent { get; init; }

		public Int32? StagePercent { get; init; }

		public OperationLogLevel LogLevel { get; init; } =
			OperationLogLevel.Information;
	}

	public enum OperationLogLevel
	{
		Debug,
		Information,
		Warning,
		Error
	}
}
