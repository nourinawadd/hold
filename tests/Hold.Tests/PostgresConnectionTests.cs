using Hold.Data;
using Npgsql;

namespace Hold.Tests;

public class PostgresConnectionTests
{
    private const string NeonUrl =
        "postgresql://neondb_owner:npg_A1b2C3d4E5f6@ep-quiet-fog-12345678.us-east-2.aws.neon.tech/neondb?sslmode=require&channel_binding=require";

    private static NpgsqlConnectionStringBuilder Parse(string value) =>
        new(PostgresConnection.Normalise(value));

    [Fact]
    public void ReadsEveryPartOfANeonUrl()
    {
        var result = Parse(NeonUrl);

        Assert.Equal("ep-quiet-fog-12345678.us-east-2.aws.neon.tech", result.Host);
        Assert.Equal("neondb", result.Database);
        Assert.Equal("neondb_owner", result.Username);
        Assert.Equal("npg_A1b2C3d4E5f6", result.Password);
        Assert.Equal(SslMode.Require, result.SslMode);
    }

    [Fact]
    public void AcceptsThePostgresSchemeToo()
    {
        var result = Parse("postgres://user:secret@db.example.com/hold?sslmode=require");

        Assert.Equal("db.example.com", result.Host);
        Assert.Equal("hold", result.Database);
    }

    [Fact]
    public void LeavesAKeyValueConnectionStringAlone()
    {
        const string Configured = "Host=localhost;Port=5432;Database=hold;Username=hold;Password=hold";

        var result = Parse(Configured);

        Assert.Equal("localhost", result.Host);
        Assert.Equal("hold", result.Database);
        Assert.Equal("hold", result.Username);
    }

    [Fact]
    public void KeepsANonDefaultPort()
    {
        var result = Parse("postgresql://user:pass@db.example.com:6543/hold");

        Assert.Equal(6543, result.Port);
    }

    [Fact]
    public void DecodesAnEscapedPassword()
    {
        var result = Parse("postgresql://user:p%40ss%3Aword%2F1@db.example.com/hold");

        Assert.Equal("p@ss:word/1", result.Password);
    }

    [Fact]
    public void SurvivesAPasswordContainingASemicolon()
    {
        var result = Parse("postgresql://user:pa%3Bss@db.example.com/hold");

        Assert.Equal("pa;ss", result.Password);
    }

    [Theory]
    [InlineData("disable", SslMode.Disable)]
    [InlineData("allow", SslMode.Allow)]
    [InlineData("prefer", SslMode.Prefer)]
    [InlineData("require", SslMode.Require)]
    [InlineData("verify-ca", SslMode.VerifyCA)]
    [InlineData("verify-full", SslMode.VerifyFull)]
    public void TranslatesEverySslMode(string sslmode, SslMode expected)
    {
        var result = Parse($"postgresql://user:pass@db.example.com/hold?sslmode={sslmode}");

        Assert.Equal(expected, result.SslMode);
    }

    [Fact]
    public void RequiresSslForARemoteHostThatDidNotSayAnything()
    {
        var result = Parse("postgresql://user:pass@db.example.com/hold");

        Assert.Equal(SslMode.Require, result.SslMode);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("postgres")]
    public void DoesNotRequireSslForALocalContainer(string host)
    {
        var result = Parse($"postgresql://hold:hold@{host}/hold");

        Assert.Equal(SslMode.Disable, result.SslMode);
    }

    [Fact]
    public void CapsThePoolBelowAFreeTierConnectionLimit()
    {
        Assert.Equal(20, Parse(NeonUrl).MaxPoolSize);
    }

    [Fact]
    public void IgnoresSurroundingWhitespace()
    {
        var result = Parse($"  {NeonUrl}\n");

        Assert.Equal("neondb", result.Database);
    }

    [Fact]
    public void PassesAnUnknownParameterByRatherThanGuessingAtIt()
    {
        Assert.Equal(SslMode.Require, Parse(NeonUrl).SslMode);
    }
}
