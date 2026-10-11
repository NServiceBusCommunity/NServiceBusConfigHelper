var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var services = builder.Services;
services.AddScoped<ClipboardService>();
services.AddScoped<DownloadService>();
services.AddScoped<BrowserStorage>();
services.AddScoped<PackageVersionLookup>();
services.AddScoped(_ => new HttpClient {Timeout = TimeSpan.FromSeconds(60)});
services.AddSingleton(TimeProvider.System);

await builder.Build().RunAsync();
