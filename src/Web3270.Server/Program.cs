using Web3270.Server.Configuration;
using Web3270.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);
builder.AddCustomSerilog();
builder.AddAServerConfiguration();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.MapHub<TerminalHub>(TerminalHub.Path);
//app.MapRoutes();
app.ConfigureHealthChecks();

app.RunApplication();