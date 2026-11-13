#pragma warning disable 1591

using Microsoft.EntityFrameworkCore.Design;
using Sanakan.Config;
using Sanakan.Config.Model;

namespace Sanakan.Database
{
    // used only by dotnet ef: the migrations are for MySQL, and making them opens no connection
    public class DesignTimeContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext(string[] args) => new DatabaseContext(new DesignConfig());

        private sealed class DesignConfig : IConfig
        {
            private readonly ConfigModel _model = new ConfigModel
            {
                DatabaseProvider = DatabaseProvider.MySql,
                ConnectionString = "Server=localhost;Database=sanakan_design",
            };

            public ConfigModel Get() => _model;
            public void Save() { }
        }
    }
}
