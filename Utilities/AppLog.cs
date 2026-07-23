using System;
using System.Diagnostics;
using System.IO;

namespace Utilities
{
	public static class AppLog
	{
		private const Int32 LogRetentionDays = 30;

		private static readonly Object _initializationLock =
			new Object();

		private static Boolean _initialized;

		public static void Initialize()
		{
			String logFolder;

			lock (_initializationLock)
			{
				if (_initialized)
				{
					return;
				}

				logFolder = Path.Combine(
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

			DeleteExpiredLogFiles(logFolder);
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

		private static void DeleteExpiredLogFiles(
			String logFolder)
		{
			DateTime expirationTimeUtc =
				DateTime.UtcNow.AddDays(-LogRetentionDays);

			String[] logFiles;

			try
			{
				logFiles = Directory.GetFiles(
					logFolder,
					"OSImageDeploy-*.log",
					SearchOption.TopDirectoryOnly);
			}
			catch (Exception exception)
			{
				Warning(
					$"Unable to enumerate old application logs: " +
					exception.Message);

				return;
			}

			Int32 deletedLogCount = 0;

			foreach (String logFile in logFiles)
			{
				try
				{
					FileInfo fileInfo =
						new FileInfo(logFile);

					if (fileInfo.LastWriteTimeUtc >= expirationTimeUtc)
					{
						continue;
					}

					File.Delete(logFile);
					deletedLogCount++;
				}
				catch (Exception exception)
				{
					Warning(
						$"Unable to delete expired application log " +
						$"'{logFile}': {exception.Message}");
				}
			}

			if (deletedLogCount > 0)
			{
				Information(
					$"Deleted {deletedLogCount} application log file(s) " +
					$"older than {LogRetentionDays} days.");
			}
		}
	}
}
