using Grpc.Net.Client;
using OSImageDeploy.Contracts;
using OSImageDeploy.Transport.Grpc;
using OSImageDeploy.Transport.Grpc.V1;
using System.IO.Pipes;

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

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
	SocketsHttpHandler handler = new SocketsHttpHandler
	{
		ConnectCallback = async (context, cancellationToken) =>
		{
			NamedPipeClientStream pipe = new NamedPipeClientStream(
				serverName: ".",
				pipeName: GrpcTransportDefaults.PipeName,
				direction: PipeDirection.InOut,
				options: PipeOptions.Asynchronous);

			await pipe.ConnectAsync(cancellationToken);

			return pipe;
		}
	};

	using GrpcChannel channel = GrpcChannel.ForAddress(
		"http://localhost",
		new GrpcChannelOptions
		{
			HttpHandler = handler
		});

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
	Assert(status.ReadOnly, "The initial service did not report read-only mode.");

	Console.WriteLine("PASS: Live named-pipe service status call.");
}

static void Assert(Boolean condition, String message)
{
	if (!condition)
	{
		throw new InvalidOperationException(message);
	}
}
