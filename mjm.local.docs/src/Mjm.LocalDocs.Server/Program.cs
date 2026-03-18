using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.DependencyInjection;
using Mjm.LocalDocs.Core.Models;
using LocalDocsAuthOptions = Mjm.LocalDocs.Core.Models.AuthenticationOptions;
using Mjm.LocalDocs.Infrastructure.DependencyInjection;
using Mjm.LocalDocs.Infrastructure.Persistence;
using Mjm.LocalDocs.Server.Components;
using Mjm.LocalDocs.Server.McpTools;
using Mjm.LocalDocs.Server.Middleware;
using ModelContextProtocol.AspNetCore;
using Mjm.LocalDocs.Server.Services;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog from appsettings.json
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

// Add Blazor services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add theme service (scoped per Blazor circuit)
builder.Services.AddScoped<ThemeService>();

// Add HttpClient for login API calls
builder.Services.AddHttpClient();

// Add Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Add MCP Server with tools
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<ProjectTools>()
    .WithTools<AddDocumentTool>()
    .WithTools<DocumentTools>()
    .WithTools<UpdateDocumentTool>()
    .WithTools<ListProjectsTool>()
    .WithTools<SearchDocsTool>();

// Add LocalDocs services
builder.Services.AddLocalDocsCoreServices();

// Bind authentication options from configuration
builder.Services.Configure<LocalDocsAuthOptions>(
    builder.Configuration.GetSection(LocalDocsAuthOptions.SectionName));

// Bind MCP options from configuration
builder.Services.Configure<McpOptions>(
    builder.Configuration.GetSection(McpOptions.SectionName));

// Bind Server options from configuration
builder.Services.Configure<ServerOptions>(
    builder.Configuration.GetSection(ServerOptions.SectionName));

// Add Infrastructure services - configured from appsettings.json
// See LocalDocs:Embeddings section for provider configuration (Fake, OpenAI)
// See LocalDocs:Storage section for storage configuration (InMemory, Sqlite)
var connectionString = builder.Configuration.GetConnectionString("LocalDocs");
builder.Services.AddLocalDocsInfrastructure(builder.Configuration, connectionString);

var app = builder.Build();

// Ensure database and vector store are initialized
using (var scope = app.Services.CreateScope())
{
    // Initialize EF Core database (Projects, Documents, DocumentChunks tables)
    // Try to get DbContext directly (works for SQLite)
    var context = scope.ServiceProvider.GetService<LocalDocsDbContext>();
    
    // If not available directly, try via factory (works for SQL Server with pooled factory)
    if (context is null)
    {
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<LocalDocsDbContext>>();
        context = factory?.CreateDbContext();
    }
    
    if (context is not null)
    {
        // Use Migrate() instead of EnsureCreated() to support incremental schema upgrades.
        // For databases created with EnsureCreated() before migrations were introduced,
        // we check if the migrations history table exists. If not, we create it and mark
        // the InitialCreate migration as already applied (since the schema already exists).
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        
        if (!appliedMigrations.Any() && pendingMigrations.Contains("20260215174229_InitialCreate"))
        {
            // Database was created with EnsureCreated() — tables exist but no migration history.
            // Check if a known table exists to confirm this is a pre-migration database.
            var tableExists = false;
            try
            {
                var conn = context.Database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = context.Database.IsSqlServer()
                    ? "SELECT CASE WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Projects') THEN 1 ELSE 0 END"
                    : "SELECT CASE WHEN EXISTS (SELECT 1 FROM sqlite_master WHERE type='table' AND name='Projects') THEN 1 ELSE 0 END";
                var result = await cmd.ExecuteScalarAsync();
                tableExists = Convert.ToInt64(result) == 1;
            }
            catch
            {
                // If check fails, proceed with normal migration (will create tables)
            }
            
            if (tableExists)
            {
                // Mark InitialCreate as already applied without executing it
                if (context.Database.IsSqlServer())
                {
                    await context.Database.ExecuteSqlRawAsync(
                        """
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '__EFMigrationsHistory')
                        CREATE TABLE [__EFMigrationsHistory] (
                            [MigrationId] NVARCHAR(150) NOT NULL,
                            [ProductVersion] NVARCHAR(32) NOT NULL,
                            CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                        )
                        """);
                    await context.Database.ExecuteSqlRawAsync(
                        """
                        IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20260215174229_InitialCreate')
                        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ('20260215174229_InitialCreate', '10.0.2')
                        """);
                }
                else
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL)");
                    await context.Database.ExecuteSqlRawAsync(
                        "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20260215174229_InitialCreate', '10.0.2')");
                }
                
                // Now apply only the remaining pending migrations
                await context.Database.MigrateAsync();
            }
            else
            {
                // Fresh database — apply all migrations
                await context.Database.MigrateAsync();
            }
        }
        else
        {
            // Normal case: migrations are already being tracked, apply pending ones
            await context.Database.MigrateAsync();
        }
    }

    // Initialize vector store (chunk_embeddings table for SQL Server, etc.)
    var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStore>();
    await vectorStore.InitializeAsync();
}

// Get server options for HTTPS configuration
var serverOptions = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    
    // Only enable HSTS when HTTPS is configured
    if (serverOptions.UseHttps)
    {
        app.UseHsts();
    }
}

// Support for running behind a reverse proxy (IIS, nginx, Azure App Service)
// This ensures the app correctly identifies HTTPS requests forwarded from the proxy
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// HTTPS redirect only when UseHttps is enabled, and only for non-MCP requests
// MCP tools may connect via HTTP even when HTTPS is enabled for the web UI
if (serverOptions.UseHttps)
{
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/mcp"),
        appBuilder => appBuilder.UseHttpsRedirection());
}

app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// MCP Authentication middleware (Bearer token validation)
app.UseMcpAuthentication("/mcp");

// Login endpoint (POST) - browser form submission
app.MapPost("/api/login", async (
    HttpContext context,
    IOptions<LocalDocsAuthOptions> authOptions) =>
{
    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var rememberMe = form["rememberMe"].ToString() == "true";
    
    var options = authOptions.Value;
    
    // Validate credentials against configuration
    if (!string.Equals(username, options.Username, StringComparison.OrdinalIgnoreCase) ||
        password != options.Password)
    {
        // Redirect back to login with error
        return Results.Redirect("/login?error=invalid");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, options.Username),
        new(ClaimTypes.NameIdentifier, options.Username)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    var authProperties = new AuthenticationProperties
    {
        IsPersistent = rememberMe,
        ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null
    };

    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

    // Redirect to home page after successful login
    return Results.Redirect("/");
}).AllowAnonymous().DisableAntiforgery();

// Logout endpoint
app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).AllowAnonymous();

// Map MCP endpoint
app.MapMcp("/mcp");

// Map Blazor
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
