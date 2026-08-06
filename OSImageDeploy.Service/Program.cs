using Microsoft.AspNetCore.Server.Kestrel.Core;
using OSImageDeploy.Engine;
using OSImageDeploy.Platform.Windows;
using OSImageDeploy.Service.Security;
using OSImageDeploy.Service.Services;
using OSImageDeploy.Transport.Grpc;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
	options.ServiceName = GrpcTransportDefaults.ServiceName;
});

builder.WebHost.ConfigureKestrel(options =>
{
	options.AddServerHeader = false;

	options.ListenNamedPipe(
		GrpcTransportDefaults.PipeName,
		listenOptions =>
		{
			listenOptions.Protocols = HttpProtocols.Http2;
		});
});

builder.WebHost.UseNamedPipes(options =>
{
	options.CurrentUserOnly = false;
	options.ListenerQueueCount = 2;
	options.MaxReadBufferSize = 64 * 1024;
	options.MaxWriteBufferSize = 256 * 1024;
	options.PipeSecurity = PipeSecurityFactory.CreateReadOnlyServiceSecurity();
});

builder.Services.AddGrpc(options =>
{
	options.EnableDetailedErrors = false;
	options.MaxReceiveMessageSize = 64 * 1024;
	options.MaxSendMessageSize = 256 * 1024;
});

builder.Services.AddSingleton<WindowsUsbTargetProvider>();
builder.Services.AddSingleton<IUsbTargetDiscovery>(services =>
	services.GetRequiredService<WindowsUsbTargetProvider>());
builder.Services.AddSingleton<IUsbTargetValidator>(services =>
	services.GetRequiredService<WindowsUsbTargetProvider>());

WebApplication app = builder.Build();

app.MapGrpcService<OsImageDeployControlService>();

app.Run();
