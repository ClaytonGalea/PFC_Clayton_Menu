using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Google.Cloud.SecretManager.V1;

var builder = WebApplication.CreateBuilder(args);

// Google credentials for Cloud services
Environment.SetEnvironmentVariable(
    "GOOGLE_APPLICATION_CREDENTIALS",
    Path.Combine(Directory.GetCurrentDirectory(), "keys", "gcp-key.json")
);

// Load Google OAuth Client Secret from Secret Manager
var secretClient = SecretManagerServiceClient.Create();

var secretName = new SecretVersionName(
    "cg-pfc-menu-ai-system",
    "google-client-secret",
    "latest"
);

var secret = secretClient.AccessSecretVersion(secretName);
string googleClientSecret = secret.Payload.Data.ToStringUtf8();

// Add services
builder.Services.AddControllersWithViews();

// Google OAuth + Cookie login
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie()
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    options.ClientSecret = googleClientSecret;
});

var app = builder.Build();

// Configure pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();