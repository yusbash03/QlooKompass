using KultureCompass.Infrastructure.Configuration;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests;

public class PostgresUrlTests
{
    [Fact]
    public void Render_style_url_becomes_an_npgsql_connection_string()
    {
        PostgresUrl.ToConnectionString("postgres://kc_user:s3cr%40t@dpg-abc123-a.oregon-postgres.render.com/kulture_compass")
            .ShouldBe("Host=dpg-abc123-a.oregon-postgres.render.com;Port=5432;Database=kulture_compass;Username=kc_user;Password=s3cr@t;SSL Mode=Prefer");
    }

    [Fact]
    public void Explicit_port_and_sslmode_are_kept()
    {
        PostgresUrl.ToConnectionString("postgresql://u:p@host:6543/db?sslmode=Require")
            .ShouldBe("Host=host;Port=6543;Database=db;Username=u;Password=p;SSL Mode=Require");
    }

    [Fact]
    public void Non_postgres_scheme_is_rejected() =>
        Should.Throw<ArgumentException>(() => PostgresUrl.ToConnectionString("mysql://u:p@host/db"));
}
