using System;
using System.Diagnostics;
using System.IO;

namespace Utilities
{
	public static class AppLog
	{
		private static Boolean _initialized;

		public static void Initialize()
		{
			if (_initialized)
			{
				return;
			}

			String logFolder = Path.Combine(
				Environment.GetFolderPath(
					Environment.SpecialFolder.CommonApplicationData),
				"OSImageDeploy",
				"Logs");

			Directory.CreateDirectory(logFolder);

			String logPath = Path.Combine(
				logFolder,
				$"OSImageDeploy-{DateTime.Now:yyyyMMdd}.log");

			Trace.Listeners.Add(
				new TextWriterTraceListener(logPath));

			Trace.AutoFlush = true;

			_initialized = true;

			Information("Application logging initialized.");
		}

		public static void Information(String message)
		{
			Trace.TraceInformation(message);
		}

		public static void Warning(String message)
		{
			Trace.TraceWarning(message);
		}

		public static void Error(String message)
		{
			Trace.TraceError(message);
		}

		public static void Error(
			String message,
			Exception exception)
		{
			Trace.TraceError(
				$"{message}{Environment.NewLine}{exception}");
		}
	}
}