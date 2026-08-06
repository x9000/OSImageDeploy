using Grpc.Core;
using Grpc.Net.Client;
using OSImageDeploy.Client;
using OSImageDeploy.Contracts;
using OSImageDeploy.Transport.Grpc;
using OSImageDeploy.Transport.Grpc.V1;
using ContractOperationProgress = OSImageDeploy.Contracts.OperationProgress;
using ContractOperationState = OSImageDeploy.Contracts.UsbMediaOperationState;

UsbTargetDescriptor original = new UsbTargetDescriptor
{
	TargetId = "disk-contract-round-trip",
	DiskNumber = 12,
	DisplayName = "Contract test USB target",
	Model = "Contract model",
	SerialNumber = "CONTRACT-SERIAL",
	BusType = "USB",
	SizeBytes = 128UL * 1024 * 1024 * 1024,
	IsSystemDisk = false,
	IsBootDisk = false,
	IsReadOnly = false,
	IsOffline = false,
	IsClustered = false,
	HealthStatus = 0
};

UsbTarget message = GrpcTargetMapper.ToMessage(original);
UsbTargetDescriptor roundTrip = GrpcTargetMapper.ToDescriptor(message);

Assert(roundTrip.TargetId == original.TargetId, "TargetId changed.");
Assert(roundTrip.DiskNumber == original.DiskNumber, "DiskNumber changed.");
Assert(roundTrip.DisplayName == original.DisplayName, "DisplayName changed.");
Assert(roundTrip.Model == original.Model, "Model changed.");
Assert(roundTrip.SerialNumber == original.SerialNumber, "SerialNumber changed.");
Assert(roundTrip.BusType == original.BusType, "BusType changed.");
Assert(roundTrip.SizeBytes == original.SizeBytes, "SizeBytes changed.");
Assert(roundTrip.IsSystemDisk == original.IsSystemDisk, "IsSystemDisk changed.");
Assert(roundTrip.IsBootDisk == original.IsBootDisk, "IsBootDisk changed.");
Assert(roundTrip.IsReadOnly == original.IsReadOnly, "IsReadOnly changed.");
Assert(roundTrip.IsOffline == original.IsOffline, "IsOffline changed.");
Assert(roundTrip.IsClustered == original.IsClustered, "IsClustered changed.");
Assert(roundTrip.HealthStatus == original.HealthStatus, "HealthStatus changed.");

Console.WriteLine("PASS: USB target gRPC contract round trip.");

UsbMediaOperationSnapshot operationSnapshot =
	new UsbMediaOperationSnapshot
	{
		OperationId = "operation-contract-round-trip",
		State = ContractOperationState.Running,
		Progress = new ContractOperationProgress
		{
			Stage = "Testing",
			Message = "Operation contract progress.",
			OverallPercent = 42
		},
		StartedUtc = DateTimeOffset.UtcNow
	};

UsbMediaOperationSnapshot operationRoundTrip =
	GrpcOperationMapper.ToSnapshot(
		GrpcOperationMapper.ToMessage(operationSnapshot));

Assert(
	operationRoundTrip.OperationId == operationSnapshot.OperationId,
	"OperationId changed.");
Assert(
	operationRoundTrip.State == operationSnapshot.State,
	"Operation state changed.");
Assert(
	operationRoundTrip.Progress?.OverallPercent == 42,
	"Operation progress changed.");

Console.WriteLine("PASS: USB media operation gRPC contract round trip.");

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
	using GrpcChannel channel = NamedPipeGrpcChannelFactory.Create();

	OsImageDeployControl.OsImageDeployControlClient client =
		new OsImageDeployControl.OsImageDeployControlClient(channel);

	GetServiceStatusResponse status = await client.GetServiceStatusAsync(
		new GetServiceStatusRequest(),
		deadline: DateTime.UtcNow.AddSeconds(10));

	Assert(
		status.ServiceName == GrpcTransportDefaults.ServiceName,
		"The service returned an unexpected name.");
	Assert(
		status.ApiVersion == GrpcTransportDefaults.ApiVersion,
		"The service returned an unexpected API version.");
	Assert(!status.ReadOnly, "The service did not report media-build support.");

	Console.WriteLine("PASS: Live named-pipe service status call.");

	Boolean unconfirmedRequestRejected = false;

	try
	{
		await client.StartUsbMediaBuildAsync(
			new StartUsbMediaBuildRequest
			{
				SelectedTarget = GrpcTargetMapper.ToMessage(original),
				DestructiveActionConfirmed = false
			},
			deadline: DateTime.UtcNow.AddSeconds(10));
	}
	catch (RpcException exception) when (
		exception.StatusCode == StatusCode.InvalidArgument)
	{
		unconfirmedRequestRejected = true;
	}

	Assert(
		unconfirmedRequestRejected,
		"The service accepted an unconfirmed destructive request.");

	Console.WriteLine("PASS: Live destructive-operation confirmation guard.");
}

static void Assert(Boolean condition, String message)
{
	if (!condition)
	{
		throw new InvalidOperationException(message);
	}
}
