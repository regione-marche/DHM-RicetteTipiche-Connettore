using ConnettoreRicett.Services;
using ConnettoreRicett.Models;
using System.Net.Http.Headers;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.Configure<OAuth2Settings>(
    builder.Configuration.GetSection("OAuth2"));

builder.Services.Configure<LiferayConfiguration>(
    builder.Configuration.GetSection("LiferayConfiguration"));

// Configuro IHttpClientFactory per Liferay
builder.Services.AddHttpClient("LiferayClient", (serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var liferayUrl = configuration["OAuth2:TokenUrl"]; 
    client.BaseAddress = new Uri(liferayUrl);
});

// Configuro un HttpClient dedicato per Nominatim con User-Agent personalizzato
builder.Services.AddHttpClient("NominatimClient", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ConnettoreRicett/1.0");
});

// Registro i servizi
builder.Services.AddHttpClient<Oauth2Service>();
builder.Services.AddScoped<TaxonomyService>();
builder.Services.AddScoped<NominatimService>();
builder.Services.AddScoped<LocationHandler>();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();

// Se uso autenticazione o autorizzazione, abilita i middleware (opzionale)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Ricetta}/{action=FormRicetta}/{id?}");

app.Run();
