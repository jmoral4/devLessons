using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

// Load configuration from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

// Bind configuration to AppSettings class
var appSettings = new AppSettings();
configuration.Bind(appSettings);

// Use database name from configuration
var conn = new SqliteConnection($"Data Source={appSettings.Database}");
conn.Open();
conn.LoadExtension("vec0.dll");   // loads vec0 virtual table
Console.WriteLine("SQLite Vec0 extension loaded successfully.");
Console.WriteLine($"Connected to database: {appSettings.Database}");
