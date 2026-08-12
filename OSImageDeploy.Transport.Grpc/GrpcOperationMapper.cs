using OSImageDeploy.Contracts;
using OSImageDeploy.Transport.Grpc.V1;
using ContractLogLevel = OSImageDeploy.Contracts.OperationLogLevel;
using ContractProgress = OSImageDeploy.Contracts.OperationProgress;
using ContractState = OSImageDeploy.Contracts.UsbMediaOperationState;
using GrpcLogLevel = OSImageDeploy.Transport.Grpc.V1.OperationLogLevel;
using GrpcProgress = OSImageDeploy.Transport.Grpc.V1.OperationProgress;
using GrpcState = OSImageDeploy.Transport.Grpc.V1.UsbMediaOperationState;

namespace OSImageDeploy.Transport.Grpc
{
	public static class GrpcOperationMapper
	{
		public static UsbMediaOperationUpdate ToMessage(
			UsbMediaOperationSnapshot snapshot)
		{
			ArgumentNullException.ThrowIfNull(snapshot);

			UsbMediaOperationUpdate message =
				new UsbMediaOperationUpdate
				{
					OperationId = snapshot.OperationId,
					State = ToMessage(snapshot.State),
					ErrorMessage = snapshot.ErrorMessage,
					StartedUnixTimeMs = snapshot.StartedUtc.ToUnixTimeMilliseconds(),
					CompletedUnixTimeMs = snapshot.CompletedUtc?.ToUnixTimeMilliseconds() ?? 0
				};

			if (snapshot.Progress != null)
			{
				message.Progress = ToMessage(snapshot.Progress);
			}

			return message;
		}

		public static UsbMediaOperationSnapshot ToSnapshot(
			UsbMediaOperationUpdate message)
		{
			ArgumentNullException.ThrowIfNull(message);

			return new UsbMediaOperationSnapshot
			{
				OperationId = message.OperationId,
				State = ToContract(message.State),
				Progress = message.Progress == null
					? null
					: ToContract(message.Progress),
				ErrorMessage = message.ErrorMessage,
				StartedUtc = DateTimeOffset.FromUnixTimeMilliseconds(
					message.StartedUnixTimeMs),
				CompletedUtc = message.CompletedUnixTimeMs == 0
					? null
					: DateTimeOffset.FromUnixTimeMilliseconds(
						message.CompletedUnixTimeMs)
			};
		}

		private static GrpcProgress ToMessage(ContractProgress progress)
		{
			return new GrpcProgress
			{
				Stage = progress.Stage,
				Message = progress.Message,
				OverallPercent = progress.OverallPercent ?? 0,
				HasOverallPercent = progress.OverallPercent.HasValue,
				StagePercent = progress.StagePercent ?? 0,
				HasStagePercent = progress.StagePercent.HasValue,
				LogLevel = (GrpcLogLevel)(Int32)progress.LogLevel
			};
		}

		private static ContractProgress ToContract(GrpcProgress progress)
		{
			return new ContractProgress
			{
				Stage = progress.Stage,
				Message = progress.Message,
				OverallPercent = progress.HasOverallPercent
					? progress.OverallPercent
					: null,
				StagePercent = progress.HasStagePercent
					? progress.StagePercent
					: null,
				LogLevel = (ContractLogLevel)(Int32)progress.LogLevel
			};
		}

		private static GrpcState ToMessage(ContractState state)
		{
			return (GrpcState)(Int32)state;
		}

		private static ContractState ToContract(GrpcState state)
		{
			return (ContractState)(Int32)state;
		}
	}
}
