using BridgePay.Agent.Api;
using BridgePay.Agent.Messaging;
using BridgePay.Agent.PosLink;
using BridgePay.Agent.Storage;
using BridgePay.Agent.Terminals;
using BridgePay.Agent.Worker;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "BridgePay Agent";
});

builder.Services.Configure<TerminalOptions>(
    builder.Configuration.GetSection("TerminalOptions"));
builder.Services.Configure<PaymentApiOptions>(
    builder.Configuration.GetSection(PaymentApiOptions.SectionName));

builder.Services.AddHostedService<Worker>();

builder.Services.AddSingleton<IPaxPosLinkClient, PaxPosLinkClient>();
builder.Services.AddSingleton<ITerminalRegistry, TerminalRegistry>();
builder.Services.AddSingleton<IPubSubConsumer, PubSubConsumer>();
builder.Services.AddHttpClient<IPaymentApiClient, PaymentApiClient>();
builder.Services.AddSingleton<IExecutionStore, FileExecutionStore>();

var host = builder.Build();
host.Run();
