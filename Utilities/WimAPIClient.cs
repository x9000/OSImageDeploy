#nullable disable

namespace Imaging
{
	using System;
	using System.ComponentModel;
	using System.IO;
	using System.IO.Compression;
	using System.Runtime.InteropServices;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Xml.Linq;

	public sealed class WimImageService : IDisposable
	{
		private readonly IWimLogger _logger;
		private const UInt32 WIM_MSG_SUCCESS = 0;
		private const UInt32 WIM_MSG_DONE = 0xFFFFFFFE;
		private const UInt32 WIM_MSG_ABORT_IMAGE = 0xFFFFFFFF;

		public event EventHandler<WimOperationProgressEventArgs> ProgressChanged;
		public event EventHandler<WimLogEventArgs> LogMessage;
		public event EventHandler<WimOperationCompletedEventArgs> OperationCompleted;
		public event EventHandler<WimOperationFailedEventArgs> OperationFailed;

		public WimImageService(IWimLogger logger = null, String dismLogPath = null, DismLogLevel dismLogLevel = DismLogLevel.WarningsInfo)
		{
			_logger = logger ?? new NullWimLogger();
			DismBootstrap.Initialize(dismLogPath, dismLogLevel);
		}

		public Task<WimServicingSession> MountForServicingAsync(String wimPath, Int32 imageIndex, String mountPath, Boolean readOnly = false, Boolean commitOnDispose = false, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Mount image", async delegate
			{
				WimServicingSession session = await WimServicingSession.MountAsync(wimPath, imageIndex, mountPath, readOnly, commitOnDispose, _logger, cancellationToken).ConfigureAwait(false);

				session.ProgressChanged += ForwardProgressChanged;
				session.LogMessage += ForwardLogMessage;
				session.OperationCompleted += ForwardOperationCompleted;
				session.OperationFailed += ForwardOperationFailed;

				return session;
			}, cancellationToken);
		}

		public Task ApplyImageAsync(String wimPath, Int32 imageIndex, String targetPath, WimOptions options = null, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Apply image", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();

				using WimHandle wim = OpenWim(
					wimPath,
					WimAccess.Read,
					WimCreationDisposition.OpenExisting,
					options);
				using WimCallbackRegistration callback = RegisterWimCallback(wim, "Apply image");
				SetWimTemporaryPath(wim, @"W:\Windows\Temp");
				

				using WimHandle image = WimNative.WIMLoadImage(wim, imageIndex);
				ThrowIfInvalid(image, "WIMLoadImage");

				Boolean ok = WimNative.WIMApplyImage(image, targetPath, ToWimFlags(options));
				ThrowIfFalse(ok, "WIMApplyImage");
			}, cancellationToken);
		}

		public Task CaptureImageAsync(String sourcePath, String wimPath, String imageName, String imageDescription = null, WimOptions options = null, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Capture image", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();

				using WimHandle wim = OpenWim(wimPath, WimAccess.Write, WimCreationDisposition.CreateAlways, options);
				using WimCallbackRegistration callback = RegisterWimCallback(wim, "Capture image");

				using WimHandle image = WimNative.WIMCaptureImage(wim, sourcePath, ToWimFlags(options));
				ThrowIfInvalid(image, "WIMCaptureImage");

				SetImageMetadata(image, imageName, imageDescription);
			}, cancellationToken);
		}

		public Task<XDocument> GetWimInfoAsync(String wimPath, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Get WIM info", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();

				using WimHandle wim = OpenWim(wimPath, WimAccess.Read, WimCreationDisposition.OpenExisting, null);
				return GetWimXmlInfo(wim);
			}, cancellationToken);
		}

		public Task<XDocument> GetImageInfoAsync(String wimPath, Int32 imageIndex, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Get image info", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();

				using WimHandle wim = OpenWim(wimPath, WimAccess.Read, WimCreationDisposition.OpenExisting, null);
				using WimHandle image = WimNative.WIMLoadImage(wim, imageIndex);
				ThrowIfInvalid(image, "WIMLoadImage");

				return GetWimXmlInfo(image);
			}, cancellationToken);
		}

		public Task AddDriverPacksToAppliedWindowsAsync(String windowsPartitionRoot = @"W:\", CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Install driver packs", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();

				String windowsRoot = windowsPartitionRoot.TrimEnd('\\') + @"\";
				String windowsTemp = Path.Combine(windowsRoot, @"Windows\Temp");

				foreach (DriveInfo drive in DriveInfo.GetDrives())
				{
					if (!drive.IsReady || drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
					{
						continue;
					}

					String driverPacksRoot = Path.Combine(drive.RootDirectory.FullName, "DriverPacks");

					if (!Directory.Exists(driverPacksRoot))
					{
						continue;
					}

					Log(WimLogLevel.Information, "DriverPacks folder found: " + driverPacksRoot);

					String[] driverPacks = DriverPackHelper.GetValidDriverPacks(driverPacksRoot, delegate (String message)
					{
						Log(WimLogLevel.Information, message);
					});

					Int32 total = driverPacks.Length;
					Int32 index = 0;

					foreach (String driverPack in driverPacks)
					{
						cancellationToken.ThrowIfCancellationRequested();

						index++;

						String driverFileName = Path.GetFileNameWithoutExtension(driverPack);
						String extractionPath = Path.Combine(windowsTemp, driverFileName);
						OnProgressChanged($"Extracting Driver Pack ({driverFileName})", ProgressSource.ManagedCode, 0, index - 1, total);

						try
						{
							Log(WimLogLevel.Information, "Decompressing Driver Pack: " + driverPack);

							if (Directory.Exists(extractionPath))
							{
								Directory.Delete(extractionPath, true);
							}

							Directory.CreateDirectory(extractionPath);

							ZipFile.ExtractToDirectory(driverPack, extractionPath);

							Log(WimLogLevel.Information, "Importing Driver Pack to Windows: " + driverFileName);

							AddDriversToAppliedWindows(windowsRoot, extractionPath, true, false);

							OnProgressChanged("Install Driver Pack: " + driverFileName, ProgressSource.ManagedCode, 0, index, total);
						}
						finally
						{
							//if (Directory.Exists(extractionPath))
							//{
							//	Log(WimLogLevel.Information, "Removing Expanded Driver Pack: " + extractionPath);
							//	Directory.Delete(extractionPath, true);
							//}
						}
					}
				}
			}, cancellationToken);
		}

		public Task SplitAsync(String sourceWimPath, String destinationPattern, UInt64 partSizeBytes, WimOptions options = null, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Split WIM", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();

				using WimHandle wim = OpenWim(sourceWimPath, WimAccess.Read, WimCreationDisposition.OpenExisting, options);
				using WimCallbackRegistration callback = RegisterWimCallback(wim, "Split WIM");

				Boolean ok = WimNative.WIMSplitFile(wim, destinationPattern, partSizeBytes, 0);
				ThrowIfFalse(ok, "WIMSplitFile");
			}, cancellationToken);
		}

		public Task MergeImageAsync(String sourceWimPath, Int32 sourceImageIndex, String destinationWimPath, WimOptions options = null, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Merge image", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();

				WimCreationDisposition disposition = File.Exists(destinationWimPath) ? WimCreationDisposition.OpenAlways : WimCreationDisposition.CreateAlways;

				using WimHandle sourceWim = OpenWim(sourceWimPath, WimAccess.Read, WimCreationDisposition.OpenExisting, options);
				using WimHandle destinationWim = OpenWim(destinationWimPath, WimAccess.Write, disposition, options);
				using WimCallbackRegistration callback = RegisterWimCallback(destinationWim, "Merge image");

				using WimHandle image = WimNative.WIMLoadImage(sourceWim, sourceImageIndex);
				ThrowIfInvalid(image, "WIMLoadImage");

				Boolean ok = WimNative.WIMExportImage(image, destinationWim, ToWimFlags(options));
				ThrowIfFalse(ok, "WIMExportImage");
			}, cancellationToken);

		}

		public void Dispose()
		{
			DismBootstrap.Shutdown();
		}

		private Task ExecuteAsync(String operationName, Action action, CancellationToken cancellationToken)
		{
			return Task.Run(delegate
			{
				try
				{
					Log(WimLogLevel.Information, operationName + " started.");
					action();
					OnOperationCompleted(operationName);
					Log(WimLogLevel.Information, operationName + " completed.");
				}
				catch (Exception exception)
				{
					OnOperationFailed(operationName, exception);
					throw;
				}
			}, cancellationToken);
		}

		private void AddDriversToAppliedWindows(String windowsRoot, String driverFolder, Boolean recurse, Boolean forceUnsigned)
		{
			IntPtr session;
			Int32 hr = DismNative.DismOpenSession(windowsRoot, null, null, out session);

			if (hr < 0)
			{
				Marshal.ThrowExceptionForHR(hr);
			}

			try
			{
				SearchOption searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
				String[] infFiles = Directory.GetFiles(driverFolder, "*.inf", searchOption);

				Int32 total = infFiles.Length;
				Int32 index = 0;

				foreach (String infFile in infFiles)
				{
					index++;

					Log(WimLogLevel.Information, "Adding driver: " + infFile);

					OnProgressChanged(
						"Add Windows Driver",
						ProgressSource.DismApi,
						0,
						index,
						total);

					hr = DismNative.DismAddDriver(session, infFile, forceUnsigned);

					if (hr < 0)
					{
						//Marshal.ThrowExceptionForHR(hr);
					}
				}
			}
			finally
			{
				DismNative.DismCloseSession(session);
			}
		}

		private Task<T> ExecuteAsync<T>(String operationName, Func<T> action, CancellationToken cancellationToken)
		{
			return Task.Run(delegate
			{
				try
				{
					Log(WimLogLevel.Information, operationName + " started.");
					T result = action();
					OnOperationCompleted(operationName);
					Log(WimLogLevel.Information, operationName + " completed.");
					return result;
				}
				catch (Exception exception)
				{
					OnOperationFailed(operationName, exception);
					throw;
				}
			}, cancellationToken);
		}

		private async Task<T> ExecuteAsync<T>(String operationName, Func<Task<T>> action, CancellationToken cancellationToken)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();

				Log(WimLogLevel.Information, operationName + " started.");
				T result = await action().ConfigureAwait(false);
				OnOperationCompleted(operationName);
				Log(WimLogLevel.Information, operationName + " completed.");

				return result;
			}
			catch (Exception exception)
			{
				OnOperationFailed(operationName, exception);
				throw;
			}
		}

		private WimHandle OpenWim(String path, WimAccess access, WimCreationDisposition disposition, WimOptions options)
		{
			UInt32 creationResult;

			WimHandle handle = WimNative.WIMCreateFile(path, (UInt32)access, (UInt32)disposition, ToWimFlags(options), ToCompression(options), out creationResult);
			ThrowIfInvalid(handle, "WIMCreateFile");

			return handle;
		}

		private WimCallbackRegistration RegisterWimCallback(WimHandle handle, String operationName)
		{
			Int32 lastProgressPercentage = -1;
			DateTime lastProgressUpdate = DateTime.MinValue;

			WimNative.WimMessageCallback callback = delegate (UInt32 messageId, IntPtr wParam, IntPtr lParam, IntPtr userData)
			{
				WimMessage message = (WimMessage)messageId;

				if (message == WimMessage.Progress || message == WimMessage.MountCleanupProgress)
				{
					Int32 percentage = Convert.ToInt32(wParam.ToInt64());
					Int32 secondsRemaining = Convert.ToInt32(lParam.ToInt64() / 1000);

					percentage = Math.Max(0, Math.Min(100, percentage));

					Boolean percentageChanged = percentage != lastProgressPercentage;
					Boolean enoughTimePassed = DateTime.Now.Subtract(lastProgressUpdate).TotalMilliseconds >= 250;

					if (percentageChanged && enoughTimePassed)
					{
						lastProgressPercentage = percentage;
						lastProgressUpdate = DateTime.Now;

						OnProgressChanged(
							operationName,
							ProgressSource.WimgApi,
							messageId,
							percentage,
							secondsRemaining);
					}
				}
				else if (message == WimMessage.Error)
				{
					String fileName = Marshal.PtrToStringUni(wParam);
					Int32 errorCode = Convert.ToInt32(lParam.ToInt64());

					Log(
						WimLogLevel.Error,
						"WIMGAPI error processing " + fileName + ". Error code: " + errorCode);
				}
				else if (message == WimMessage.Warning)
				{
					String fileName = Marshal.PtrToStringUni(wParam);
					Int32 errorCode = Convert.ToInt32(lParam.ToInt64());

					Log(
						WimLogLevel.Warning,
						"WIMGAPI warning processing " + fileName + ". Error code: " + errorCode);
				}

				return (UInt32)WimMessage.Success;
			};

			UInt32 result = WimNative.WIMRegisterMessageCallback(handle, callback, IntPtr.Zero);

			if (result == UInt32.MaxValue)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "WIMRegisterMessageCallback failed.");
			}

			return new WimCallbackRegistration(callback, handle.DangerousGetHandle());
		}

		internal enum WimMessage : UInt32
		{
			Progress = 38008,
			Process = 38009,
			SetRange = 38011,
			SetPos = 38012,
			Error = 38015,
			Info = 38020,
			Warning = 38021,
			MountCleanupProgress = 38026,
			QueryAbort = 38030,

			Success = 0,
			SkipError = 65534,
			AbortImage = 65535
		}

		private void ForwardProgressChanged(Object sender, WimOperationProgressEventArgs e)
		{
			ProgressChanged?.Invoke(this, e);
		}

		private void ForwardLogMessage(Object sender, WimLogEventArgs e)
		{
			LogMessage?.Invoke(this, e);
		}

		private void ForwardOperationCompleted(Object sender, WimOperationCompletedEventArgs e)
		{
			OperationCompleted?.Invoke(this, e);
		}

		private void ForwardOperationFailed(Object sender, WimOperationFailedEventArgs e)
		{
			OperationFailed?.Invoke(this, e);
		}

		private void Log(WimLogLevel level, String message)
		{
			if (level == WimLogLevel.Debug)
			{
				_logger.Debug(message);
			}
			else if (level == WimLogLevel.Information)
			{
				_logger.Information(message);
			}
			else if (level == WimLogLevel.Warning)
			{
				_logger.Warning(message);
			}
			else if (level == WimLogLevel.Error)
			{
				_logger.Error(message);
			}

			LogMessage?.Invoke(this, new WimLogEventArgs(level, message));
		}

		private void OnProgressChanged(String operationName, ProgressSource source, UInt32 messageId, Int64 current, Int64 total)
		{
			ProgressChanged?.Invoke(this, new WimOperationProgressEventArgs(operationName, source, messageId, current, total));
		}

		private void OnOperationCompleted(String operationName)
		{
			OperationCompleted?.Invoke(this, new WimOperationCompletedEventArgs(operationName));
		}

		private void OnOperationFailed(String operationName, Exception exception)
		{
			_logger.Error(operationName + " failed.", exception);
			OperationFailed?.Invoke(this, new WimOperationFailedEventArgs(operationName, exception));
		}

		private static XDocument GetWimXmlInfo(WimHandle handle)
		{
			IntPtr buffer;
			UInt32 size;

			Boolean ok = WimNative.WIMGetImageInformation(handle, out buffer, out size);
			ThrowIfFalse(ok, "WIMGetImageInformation");

			String xml = Marshal.PtrToStringUni(buffer, (Int32)size / 2);

			if (xml != null)
			{
				xml = xml.Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');
			}

			if (String.IsNullOrWhiteSpace(xml))
			{
				throw new InvalidOperationException("WIMGetImageInformation returned empty XML.");
			}

			if (!xml.StartsWith("<"))
			{
				String preview = xml.Length > 200 ? xml.Substring(0, 200) : xml;

				throw new InvalidOperationException(
					"WIMGetImageInformation returned invalid XML. Preview: " + preview);
			}

			return XDocument.Parse(xml);
		}

		private static void SetImageMetadata(WimHandle image, String name, String description)
		{
			XDocument document = new XDocument(
				new XElement("IMAGE",
					new XElement("NAME", name ?? String.Empty),
					new XElement("DESCRIPTION", description ?? String.Empty)));

			Byte[] bytes = Encoding.Unicode.GetBytes(document.ToString(SaveOptions.DisableFormatting) + "\0");
			IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);

			try
			{
				Marshal.Copy(bytes, 0, buffer, bytes.Length);

				Boolean ok = WimNative.WIMSetImageInformation(image, buffer, (UInt32)bytes.Length);
				ThrowIfFalse(ok, "WIMSetImageInformation");
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}

		private static UInt32 ToWimFlags(WimOptions options)
		{
			UInt32 flags = 0;

			if (options != null && options.Verify)
			{
				flags |= 0x00000002;
			}

			return flags;
		}

		private static UInt32 ToCompression(WimOptions options)
		{
			if (options == null)
			{
				return (UInt32)WimCompression.Lzx;
			}

			return (UInt32)options.Compression;
		}

		private static void ThrowIfInvalid(WimHandle handle, String operation)
		{
			if (handle == null || handle.IsInvalid)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed.");
			}
		}

		private static void ThrowIfFalse(Boolean value, String operation)
		{
			if (!value)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed.");
			}
		}
		private static void SetWimTemporaryPath(WimHandle wim, String temporaryPath)
		{
			Directory.CreateDirectory(temporaryPath);

			Boolean ok = WimNative.WIMSetTemporaryPath(wim, temporaryPath);

			if (!ok)
			{
				throw new Win32Exception(
					Marshal.GetLastWin32Error(),
					"WIMSetTemporaryPath failed.");
			}
		}
	}



	public sealed class WimServicingSession : IAsyncDisposable, IDisposable
	{
		private readonly String _mountPath;
		private readonly Boolean _commitOnDispose;
		private readonly IWimLogger _logger;
		private readonly DismNative.DismProgressCallback _progressCallback;

		private IntPtr _dismSession;
		private Boolean _mounted;
		private Boolean _closed;

		public event EventHandler<WimOperationProgressEventArgs> ProgressChanged;
		public event EventHandler<WimLogEventArgs> LogMessage;
		public event EventHandler<WimOperationCompletedEventArgs> OperationCompleted;
		public event EventHandler<WimOperationFailedEventArgs> OperationFailed;

		private WimServicingSession(String mountPath, Boolean commitOnDispose, IWimLogger logger)
		{
			_mountPath = mountPath;
			_commitOnDispose = commitOnDispose;
			_logger = logger ?? new NullWimLogger();
			_progressCallback = OnDismProgress;
		}

		public String MountPath
		{
			get { return _mountPath; }
		}

		public static async Task<WimServicingSession> MountAsync(String wimPath, Int32 imageIndex, String mountPath, Boolean readOnly, Boolean commitOnDispose, IWimLogger logger, CancellationToken cancellationToken)
		{
			WimServicingSession session = new WimServicingSession(mountPath, commitOnDispose, logger);

			await session.ExecuteAsync("Mount image", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();

				Directory.CreateDirectory(mountPath);

				Int64 hr = DismNative.DismMountImage(
					wimPath,
					mountPath,
					(UInt32)imageIndex,
					null,
					DismImageIdentifier.ImageIndex,
					readOnly ? DismMountMode.ReadOnly : DismMountMode.ReadWrite,
					IntPtr.Zero,
					session._progressCallback,
					IntPtr.Zero);

				ThrowIfFailed(hr, "DismMountImage");

				session._mounted = true;

				hr = DismNative.DismOpenSession(mountPath, null, null, out session._dismSession);
				ThrowIfFailed(hr, "DismOpenSession");
			}, cancellationToken).ConfigureAwait(false);

			return session;
		}

		public void AddDriver(String driverInfOrFolder, Boolean recurse = false, Boolean forceUnsigned = false)
		{
			Execute("Add driver", delegate
			{
				EnsureOpen();
				if (Directory.Exists(driverInfOrFolder))
				{
					SearchOption option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
					String[] infFiles = Directory.GetFiles(driverInfOrFolder, "*.inf", option);
					foreach (String infFile in infFiles)
					{
						AddSingleDriver(infFile, forceUnsigned);
						ProgressChanged?.Invoke(this, new WimOperationProgressEventArgs("Add driver", ProgressSource.DismApi, 0, infFiles.IndexOf(infFile), infFiles.Length));
					}

					return;
				}
				AddSingleDriver(driverInfOrFolder, forceUnsigned);
			});
		}

		private void AddDriversToAppliedWindows(String windowsRoot, String driverFolder, Boolean recurse, Boolean forceUnsigned)
		{
			IntPtr session;
			Int32 hr = DismNative.DismOpenSession(windowsRoot, null, null, out session);

			if (hr < 0)
			{
				Marshal.ThrowExceptionForHR(hr);
			}

			try
			{
				SearchOption searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
				String[] infFiles = Directory.GetFiles(driverFolder, "*.inf", searchOption);

				Int32 total = infFiles.Length;
				Int32 index = 0;

				foreach (String infFile in infFiles)
				{
					index++;

					Log(WimLogLevel.Information, "Adding driver: " + infFile);

					ProgressChanged?.Invoke(this, new WimOperationProgressEventArgs("Add Windows Driver", ProgressSource.DismApi, 0, index, total));

					hr = DismNative.DismAddDriver(session, infFile, forceUnsigned);

					if (hr < 0)
					{
						Marshal.ThrowExceptionForHR(hr);
					}
				}
			}
			finally
			{
				DismNative.DismCloseSession(session);
			}
		}

		public void AddPackage(String packagePath, Boolean ignoreCheck = false, Boolean preventPending = false)
		{
			Execute("Add package", delegate
			{
				EnsureOpen();
				Log(WimLogLevel.Information, "Adding package: " + packagePath);

				Int32 hr = DismNative.DismAddPackage(_dismSession, packagePath, ignoreCheck, preventPending, IntPtr.Zero, _progressCallback, IntPtr.Zero);
				ThrowIfFailed(hr, "DismAddPackage");
			});
		}

		public void EnableFeature(String featureName, Boolean limitAccess = false)
		{
			Execute("Enable feature", delegate
			{
				EnsureOpen();
				Log(WimLogLevel.Information, "Enabling feature: " + featureName);

				Int32 hr = DismNative.DismEnableFeature(_dismSession, featureName, null, false, limitAccess, IntPtr.Zero, _progressCallback, IntPtr.Zero);
				ThrowIfFailed(hr, "DismEnableFeature");
			});
		}

		public void DisableFeature(String featureName, Boolean removePayload = false)
		{
			Execute("Disable feature", delegate
			{
				EnsureOpen();
				Log(WimLogLevel.Information, "Disabling feature: " + featureName);

				Int32 hr = DismNative.DismDisableFeature(_dismSession, featureName, null, removePayload, IntPtr.Zero, _progressCallback, IntPtr.Zero);
				ThrowIfFailed(hr, "DismDisableFeature");
			});
		}

		public void ExtractFile(String imageRelativePath, String destinationPath, Boolean overwrite = true)
		{
			Execute("Extract file", delegate
			{
				EnsureOpen();

				String sourcePath = Path.Combine(_mountPath, TrimImagePath(imageRelativePath));
				String destinationDirectory = Path.GetDirectoryName(destinationPath);

				if (!String.IsNullOrWhiteSpace(destinationDirectory))
				{
					Directory.CreateDirectory(destinationDirectory);
				}

				File.Copy(sourcePath, destinationPath, overwrite);
			});
		}

		public void OverwriteFile(String sourcePath, String imageRelativePath)
		{
			Execute("Overwrite file", delegate
			{
				EnsureOpen();

				String destinationPath = Path.Combine(_mountPath, TrimImagePath(imageRelativePath));
				String destinationDirectory = Path.GetDirectoryName(destinationPath);

				if (!String.IsNullOrWhiteSpace(destinationDirectory))
				{
					Directory.CreateDirectory(destinationDirectory);
				}

				File.Copy(sourcePath, destinationPath, true);
			});
		}

		public void RemoveFile(String imageRelativePath)
		{
			Execute("Remove file", delegate
			{
				EnsureOpen();

				String targetPath = Path.Combine(_mountPath, TrimImagePath(imageRelativePath));

				if (File.Exists(targetPath))
				{
					File.Delete(targetPath);
				}
			});
		}

		public Task CommitAsync(CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Commit image", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();
				EnsureOpen();

				Int32 hr = DismNative.DismCommitImage(_dismSession, DismCommitFlags.None, IntPtr.Zero, _progressCallback, IntPtr.Zero);
				ThrowIfFailed(hr, "DismCommitImage");
			}, cancellationToken);
		}

		public Task UnmountAsync(CancellationToken cancellationToken = default)
		{
			return UnmountAsync(_commitOnDispose, cancellationToken);
		}

		public Task UnmountAsync(Boolean commit, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync("Unmount image", delegate
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (_closed)
				{
					return;
				}

				if (_dismSession != IntPtr.Zero)
				{
					DismNative.DismCloseSession(_dismSession);
					_dismSession = IntPtr.Zero;
				}

				if (_mounted)
				{
					Int32 hr = DismNative.DismUnmountImage(_mountPath, commit ? DismUnmountFlag.Commit : DismUnmountFlag.Discard, IntPtr.Zero, _progressCallback, IntPtr.Zero);
					ThrowIfFailed(hr, "DismUnmountImage");

					_mounted = false;
				}

				_closed = true;
			}, cancellationToken);
		}

		public void Dispose()
		{
			UnmountAsync(_commitOnDispose).GetAwaiter().GetResult();
		}

		public async ValueTask DisposeAsync()
		{
			await UnmountAsync(_commitOnDispose).ConfigureAwait(false);
		}

		private void AddSingleDriver(String infPath, Boolean forceUnsigned)
		{
			Log(WimLogLevel.Information, "Adding driver: " + infPath);

			Int32 hr = DismNative.DismAddDriver(_dismSession, infPath, forceUnsigned);
			ThrowIfFailed(hr, "DismAddDriver");
		}

		private void OnDismProgress(UInt32 current, UInt32 total, IntPtr userData)
		{
			ProgressChanged?.Invoke(this, new WimOperationProgressEventArgs("DISM operation", ProgressSource.DismApi, 0, current, total));
		}

		private void Execute(String operationName, Action action)
		{
			try
			{
				Log(WimLogLevel.Information, operationName + " started.");
				action();
				OperationCompleted?.Invoke(this, new WimOperationCompletedEventArgs(operationName));
				Log(WimLogLevel.Information, operationName + " completed.");
			}
			catch (Exception exception)
			{
				_logger.Error(operationName + " failed.", exception);
				OperationFailed?.Invoke(this, new WimOperationFailedEventArgs(operationName, exception));
				throw;
			}
		}

		private Task ExecuteAsync(String operationName, Action action, CancellationToken cancellationToken)
		{
			return Task.Run(delegate
			{
				Execute(operationName, action);
			}, cancellationToken);
		}

		private void Log(WimLogLevel level, String message)
		{
			if (level == WimLogLevel.Debug)
			{
				_logger.Debug(message);
			}
			else if (level == WimLogLevel.Information)
			{
				_logger.Information(message);
			}
			else if (level == WimLogLevel.Warning)
			{
				_logger.Warning(message);
			}
			else if (level == WimLogLevel.Error)
			{
				_logger.Error(message);
			}

			LogMessage?.Invoke(this, new WimLogEventArgs(level, message));
		}

		private void EnsureOpen()
		{
			if (_closed || _dismSession == IntPtr.Zero)
			{
				throw new ObjectDisposedException(nameof(WimServicingSession));
			}
		}

		private static String TrimImagePath(String path)
		{
			return path.TrimStart('\\', '/');
		}

		private static void ThrowIfFailed(Int64 hr, String operation)
		{
			if (hr < 0)
			{
				throw new InvalidOperationException(operation + " failed. HRESULT: 0x" + hr.ToString("X8"));
			}
		}
	}

	public interface IWimLogger
	{
		void Debug(String message);
		void Information(String message);
		void Warning(String message);
		void Error(String message, Exception exception = null);
	}

	public sealed class NullWimLogger : IWimLogger
	{
		public void Debug(String message) { }
		public void Information(String message) { }
		public void Warning(String message) { }
		public void Error(String message, Exception exception = null) { }
	}

	public sealed class WimOperationProgressEventArgs : EventArgs
	{
		public WimOperationProgressEventArgs(String operationName, ProgressSource source, UInt32 messageId, Int64 current, Int64 total)
		{
			OperationName = operationName;
			Source = source;
			MessageId = messageId;
			Current = current;
			Total = total;
		}

		public String OperationName { get; }
		public ProgressSource Source { get; }
		public UInt32 MessageId { get; }
		public Int64 Current { get; }
		public Int64 Total { get; }

		public Double Percentage
		{
			get
			{
				if (Source == ProgressSource.WimgApi)
				{
					return Current;
				}

				if (Total == 0)
				{
					return 0;
				}

				return (Double)Current / Total * 100;
			}
		}
		public Int32 SecondsRemaining
		{
			get
			{
				if (Source == ProgressSource.WimgApi)
				{
					return Convert.ToInt32(Total);
				}

				return 0;
			}
		}
	}

	public sealed class WimLogEventArgs : EventArgs
	{
		public WimLogEventArgs(WimLogLevel level, String message)
		{
			Level = level;
			Message = message;
		}

		public WimLogLevel Level { get; }
		public String Message { get; }
	}

	public sealed class WimOperationCompletedEventArgs : EventArgs
	{
		public WimOperationCompletedEventArgs(String operationName)
		{
			OperationName = operationName;
		}

		public String OperationName { get; }
	}

	public sealed class WimOperationFailedEventArgs : EventArgs
	{
		public WimOperationFailedEventArgs(String operationName, Exception exception)
		{
			OperationName = operationName;
			Exception = exception;
		}

		public String OperationName { get; }
		public Exception Exception { get; }
	}

	internal static class DismBootstrap
	{
		private static readonly Object _lock = new Object();
		private static Int32 _referenceCount;

		public static void Initialize(String logPath, DismLogLevel logLevel)
		{
			lock (_lock)
			{
				if (_referenceCount == 0)
				{
					Int32 hr = DismNative.DismInitialize(logLevel, logPath, null);

					if (hr < 0)
					{
						Marshal.ThrowExceptionForHR(hr);
					}
				}

				_referenceCount++;
			}
		}

		public static void Shutdown()
		{
			lock (_lock)
			{
				if (_referenceCount <= 0)
				{
					return;
				}

				_referenceCount--;

				if (_referenceCount == 0)
				{
					DismNative.DismShutdown();
				}
			}
		}
	}

	public sealed class WimOptions
	{
		public Boolean Verify { get; set; }
		public WimCompression Compression { get; set; } = WimCompression.Lzx;
	}

	public enum ProgressSource
	{
		WimgApi,
		DismApi,
		ManagedCode
	}

	public enum WimLogLevel
	{
		Debug,
		Information,
		Warning,
		Error
	}

	public enum WimCompression : UInt32
	{
		None = 0,
		Xpress = 1,
		Lzx = 2,
		Lzms = 3
	}

	public enum WimAccess : UInt32
	{
		Read = 0x80000000,
		Write = 0x40000000,
		ReadWrite = 0xC0000000
	}

	public enum WimCreationDisposition : UInt32
	{
		CreateNew = 1,
		CreateAlways = 2,
		OpenExisting = 3,
		OpenAlways = 4
	}

	public enum DismLogLevel : UInt32
	{
		Errors = 0,
		Warnings = 1,
		WarningsInfo = 2
	}

	public enum DismImageIdentifier : UInt32
	{
		ImageIndex = 0,
		ImageName = 1
	}

	public enum DismMountMode : UInt32
	{
		ReadWrite = 0,
		ReadOnly = 1
	}

	public enum DismUnmountFlag : UInt32
	{
		Commit = 0,
		Discard = 1
	}

	public enum DismCommitFlags : UInt32
	{
		None = 0
	}

	internal sealed class WimHandle : SafeHandle
	{
		public WimHandle() : base(IntPtr.Zero, true)
		{
		}

		public override Boolean IsInvalid
		{
			get { return handle == IntPtr.Zero || handle == new IntPtr(-1); }
		}

		protected override Boolean ReleaseHandle()
		{
			return WimNative.WIMCloseHandle(handle);
		}
	}

	internal sealed class WimCallbackRegistration : IDisposable
	{
		private readonly WimNative.WimMessageCallback _callback;
		private readonly IntPtr _handle;

		public WimCallbackRegistration(WimNative.WimMessageCallback callback, IntPtr handle)
		{
			_callback = callback;
			_handle = handle;
		}

		public void Dispose()
		{
			if (_callback != null)
			{
				WimNative.WIMUnregisterMessageCallback(_handle, _callback);
			}
		}
	}

	internal static class WimNative
	{
		private const String DllName = "wimgapi.dll";

		public delegate UInt32 WimMessageCallback(UInt32 messageId, IntPtr wParam, IntPtr lParam, IntPtr userData);

		[DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern WimHandle WIMCreateFile(String path, UInt32 desiredAccess, UInt32 creationDisposition, UInt32 flagsAndAttributes, UInt32 compressionType, out UInt32 creationResult);

		[DllImport(DllName, SetLastError = true)]
		public static extern Boolean WIMCloseHandle(IntPtr handle);

		[DllImport(DllName, SetLastError = true)]
		public static extern WimHandle WIMLoadImage(WimHandle wimHandle, Int32 imageIndex);

		[DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern WimHandle WIMCaptureImage(WimHandle wimHandle, String path, UInt32 captureFlags);

		[DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern Boolean WIMApplyImage(WimHandle imageHandle, String path, UInt32 applyFlags);

		[DllImport(DllName, SetLastError = true)]
		public static extern Boolean WIMGetImageInformation(WimHandle handle, out IntPtr imageInfo, out UInt32 size);

		[DllImport(DllName, SetLastError = true)]
		public static extern Boolean WIMSetImageInformation(WimHandle handle, IntPtr imageInfo, UInt32 size);

		[DllImport(DllName, SetLastError = true)]
		public static extern UInt32 WIMRegisterMessageCallback(WimHandle wimHandle, WimMessageCallback callback, IntPtr userData);

		[DllImport(DllName, SetLastError = true)]
		public static extern Boolean WIMUnregisterMessageCallback(IntPtr wimHandle, WimMessageCallback callback);

		[DllImport(DllName, SetLastError = true)]
		public static extern Boolean WIMSplitFile(WimHandle wimHandle, String partPath, UInt64 partSize, UInt32 flags);

		[DllImport(DllName, SetLastError = true)]
		public static extern Boolean WIMExportImage(WimHandle imageHandle, WimHandle destinationWimHandle, UInt32 flags);

		[DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern Boolean WIMSetTemporaryPath(WimHandle wimHandle, String path);
	}

	internal static class DismNative
	{
		private const String DllName = "dismapi.dll";

		public delegate void DismProgressCallback(UInt32 current, UInt32 total, IntPtr userData);

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		public static extern Int32 DismInitialize(DismLogLevel logLevel, String logFilePath, String scratchDirectory);

		[DllImport(DllName)]
		public static extern Int32 DismShutdown();

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		public static extern Int32 DismOpenSession(String imagePath, String windowsDirectory, String systemDrive, out IntPtr session);

		[DllImport(DllName)]
		public static extern Int32 DismCloseSession(IntPtr session);

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		public static extern Int64 DismMountImage(String imageFilePath, String mountPath, UInt32 imageIndex, String imageName, DismImageIdentifier imageIdentifier, DismMountMode mountMode, IntPtr cancelEvent, DismProgressCallback progress, IntPtr userData);

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		public static extern Int32 DismUnmountImage(String mountPath, DismUnmountFlag flags, IntPtr cancelEvent, DismProgressCallback progress, IntPtr userData);

		[DllImport(DllName)]
		public static extern Int32 DismCommitImage(IntPtr session, DismCommitFlags flags, IntPtr cancelEvent, DismProgressCallback progress, IntPtr userData);

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		public static extern Int32 DismAddDriver(IntPtr session, String driverPath, Boolean forceUnsigned);

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		public static extern Int32 DismAddPackage(IntPtr session, String packagePath, Boolean ignoreCheck, Boolean preventPending, IntPtr cancelEvent, DismProgressCallback progress, IntPtr userData);

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		public static extern Int32 DismEnableFeature(IntPtr session, String featureName, String identifier, Boolean packageName, Boolean limitAccess, IntPtr cancelEvent, DismProgressCallback progress, IntPtr userData);

		[DllImport(DllName, CharSet = CharSet.Unicode)]
		public static extern Int32 DismDisableFeature(IntPtr session, String featureName, String packageName, Boolean removePayload, IntPtr cancelEvent, DismProgressCallback progress, IntPtr userData);
	}
}