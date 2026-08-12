using Grpc.Net.Client;
using OSImageDeploy.Transport.Grpc;
using System.IO.Pipes;
using System.Net.Http;
using System.Security.Principal;

namespace OSImageDeploy.Client
{
	public static class NamedPipeGrpcChannelFactory
	{
		public static GrpcChannel Create(
			String pipeName = GrpcTransportDefaults.PipeName)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

			SocketsHttpHandler handler = new SocketsHttpHandler
			{
				ConnectCallback = async (_, cancellationToken) =>
				{
					NamedPipeClientStream pipe = new NamedPipeClientStream(
						serverName: ".",
						pipeName: pipeName,
						direction: PipeDirection.InOut,
						options:
							PipeOptions.WriteThrough |
							PipeOptions.Asynchronous,
						impersonationLevel: TokenImpersonationLevel.Anonymous);

					try
					{
						await pipe.ConnectAsync(cancellationToken)
							.ConfigureAwait(false);

						return pipe;
					}
					catch
					{
						await pipe.DisposeAsync().ConfigureAwait(false);
						throw;
					}
				}
			};

			return GrpcChannel.ForAddress(
				"http://localhost",
				new GrpcChannelOptions
				{
					HttpHandler = handler,
					DisposeHttpClient = true
				});
		}
	}
}
