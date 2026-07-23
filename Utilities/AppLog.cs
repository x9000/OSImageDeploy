using System;
using System.Diagnostics;
using System.IO;

namespace Utilities
{
	public static class AppLog
	{
		private static readonly Object _initializationLock =
			new Object();

		private static Boolean _initialized;

		public static void Initialize()
		{
			lock (_initializationLock)
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
					new TextWriterTraceListener(
						logPath,
						"OSImageDeployFileLog"));

				Trace.AutoFlush = true;

				_initialized = true;
			}

			Information("Application logging initialized.");
		}

		public static void Information(String message)
		{
			Write("INFO ", message);
		}

		public static void Warning(String message)
		{
			Write("WARN ", message);
		}

		public static void Error(String message)
		{
			Write("ERROR", message);
		}

		public static void Error(
			String message,
			Exception exception)
		{
			Error(
				$"{message}{Environment.NewLine}{exception}");
		}

		private static void Write(
			String level,
			String message)
		{
			String timestamp =
				DateTimeOffset.Now.ToString(
					"yyyy-MM-dd HH:mm:ss.fff zzz");

			Trace.WriteLine(
				$"{timestamp} [{level}] {message}");
		}
	}
}
