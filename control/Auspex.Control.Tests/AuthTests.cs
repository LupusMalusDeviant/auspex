using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Auspex.Control.Services;

namespace Auspex.Control.Tests;

public class PasswordAuthTests
{
    private static PasswordAuth Build(AuthOptions options)
        => new(Options.Create(options), NullLogger<PasswordAuth>.Instance);

    [Fact]
    public void The_right_password_is_accepted()
    {
        var auth = Build(new AuthOptions { Username = "admin", PasswordHash = PasswordAuth.Hash("geheim") });

        Assert.True(auth.Verify("admin", "geheim"));
    }

    [Fact]
    public void A_wrong_password_and_a_wrong_user_are_rejected()
    {
        var auth = Build(new AuthOptions { Username = "admin", PasswordHash = PasswordAuth.Hash("geheim") });

        Assert.False(auth.Verify("admin", "falsch"));
        Assert.False(auth.Verify("jemand", "geheim"));
        Assert.False(auth.Verify("admin", ""));
        Assert.False(auth.Verify("admin", null));
    }

    [Fact]
    public void A_plaintext_password_from_the_configuration_works()
    {
        var auth = Build(new AuthOptions { Username = "admin", Password = "notnagel" });

        Assert.True(auth.Verify("admin", "notnagel"));
    }

    /// <summary>
    /// Without a password it must not stand open - but must not lock anybody
    /// out either. Hence a random one that appears in the log.
    /// </summary>
    [Fact]
    public void With_no_configuration_a_password_is_generated()
    {
        var auth = Build(new AuthOptions());

        Assert.NotNull(auth.GeneratedPassword);
        Assert.True(auth.GeneratedPassword!.Length >= 16);
        Assert.True(auth.Verify("admin", auth.GeneratedPassword));
        Assert.False(auth.Verify("admin", "irgendwas"));
    }

    [Fact]
    public void Every_hash_has_a_salt_of_its_own()
    {
        // Equal passwords must not look equal, otherwise a glance at the
        // configuration reveals who uses the same one.
        Assert.NotEqual(PasswordAuth.Hash("gleich"), PasswordAuth.Hash("gleich"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("kaputt")]
    [InlineData("pbkdf2-sha256$abc$def")]
    [InlineData("md5$1$aaaa$bbbb")]
    [InlineData("pbkdf2-sha256$1000$not-base64!$also-not!")]
    public void Broken_hashes_let_nobody_through(string stored)
    {
        var auth = Build(new AuthOptions { Username = "admin", PasswordHash = stored });

        Assert.False(auth.Verify("admin", "irgendwas"));
        Assert.False(auth.Verify("admin", ""));
    }

    [Fact]
    public void The_hash_format_is_self_describing()
    {
        var hash = PasswordAuth.Hash("test");
        var parts = hash.Split(':');

        // The algorithm and the round count stand in the hash - otherwise it
        // cannot be moved to stronger parameters in two years' time.
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.True(int.Parse(parts[1]) >= 100_000);
    }
}

public class HashFormatTests
{
    /// <summary>
    /// The hash ends up in .env files and YAML. A dollar sign in it is a
    /// variable there - Docker Compose silently expands it away, and signing
    /// in fails with no error message. Exactly this happened at the first
    /// deployment.
    /// </summary>
    [Fact]
    public void The_hash_contains_no_dollar_sign()
    {
        for (var i = 0; i < 20; i++)
        {
            var hash = PasswordAuth.Hash($"kennwort{i}");
            Assert.DoesNotContain('$', hash);
        }
    }

    [Fact]
    public void Old_hashes_in_the_dollar_format_stay_valid()
    {
        // Rebuilt the way the earlier version wrote them.
        var fresh = PasswordAuth.Hash("geheim");
        var old = fresh.Replace(':', '$');

        var auth = new PasswordAuth(
            Microsoft.Extensions.Options.Options.Create(
                new AuthOptions { Username = "admin", PasswordHash = old }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PasswordAuth>.Instance);

        Assert.True(auth.Verify("admin", "geheim"));
        Assert.False(auth.Verify("admin", "falsch"));
    }
}
