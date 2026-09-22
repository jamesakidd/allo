using Allo.Client;
using Allo.Client.Auth;
using Allo.Client.Lists;
using Allo.Client.Sync;
using Allo.Shared.Lists;
using Allo.Shared.Sync;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Same origin as the API, so the auth cookie is sent automatically. Singletons (one per
// app) so the data services below share one instance with what Program.cs pre-loads;
// pages resolve scoped services from a different scope than host.Services.
builder.Services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Messages at the bottom: they don't cover the app bar, and it's where a thumb is.
builder.Services.AddMudServices(config =>
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomCenter);
builder.Services.AddBlazoredLocalStorageAsSingleton();

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AlloAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AlloAuthStateProvider>());

// Local-first data: pages read and write LocalStore; the engine syncs it in the background.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISyncStorage, BrowserSyncStorage>();
builder.Services.AddSingleton<LocalStore>();
builder.Services.AddSingleton<ISyncTransport, HttpSyncTransport>();
builder.Services.AddSingleton<SyncEngine>();
builder.Services.AddSingleton<ListActions>();
builder.Services.AddSingleton<UiState>();
builder.Services.AddScoped<SyncCoordinator>();

var host = builder.Build();
// Load the device's copy before the first render, so the app opens with data even offline.
await host.Services.GetRequiredService<LocalStore>().LoadAsync();
await host.RunAsync();
