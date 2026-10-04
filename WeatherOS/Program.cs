using Serilog;

using var log = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Init server.");
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((context, loggerConfiguration) =>
    {
        loggerConfiguration.WriteTo.Console();
        loggerConfiguration.ReadFrom.Configuration(context.Configuration);
    });
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    app.UseHealthChecks("/health");

    app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Server stoped (error)");
}
finally
{
    Log.CloseAndFlush();
}
