using MailVoidSmtpServer.Services;

namespace MailVoidSmtpServer.Tests;

public class MailboxFilterTests
{
    private static readonly string[] Allowed = { "mailvoid.com", "example.org" };

    [Theory]
    [InlineData("mailvoid.com", true)]               // exact match
    [InlineData("MailVoid.com", true)]               // case-insensitive
    [InlineData("sub.mailvoid.com", true)]           // subdomain
    [InlineData("a.b.mailvoid.com", true)]           // nested subdomain
    [InlineData("example.org", true)]                // second allowed domain
    [InlineData("notmailvoid.com", false)]           // lookalike must not match (open-relay guard)
    [InlineData("mailvoid.com.evil.com", false)]     // suffix trickery
    [InlineData("evil.com", false)]                  // unrelated
    [InlineData("", false)]                          // empty
    public void IsAllowedDomain_MatchesSubdomainsButNotLookalikes(string domain, bool expected)
    {
        Assert.Equal(expected, MailVoidMailboxFilter.IsAllowedDomain(domain, Allowed));
    }

    [Fact]
    public void IsAllowedDomain_EmptyAllowList_RejectsEverything()
    {
        Assert.False(MailVoidMailboxFilter.IsAllowedDomain("mailvoid.com", Array.Empty<string>()));
    }

    [Theory]
    [InlineData("user@mailvoid.com", true)]
    [InlineData("first.last+tag@sub.mailvoid.com", true)]
    [InlineData("no-at-sign", false)]
    [InlineData("@nolocal.com", false)]
    [InlineData("nodomain@", false)]
    [InlineData("two@@at.com", false)]
    [InlineData("nodot@localhost", false)]
    [InlineData("has space@mailvoid.com", false)]
    [InlineData("angle<@mailvoid.com", false)]
    [InlineData("", false)]
    public void IsValidEmailFormat_Validates(string email, bool expected)
    {
        Assert.Equal(expected, MailVoidMailboxFilter.IsValidEmailFormat(email));
    }
}
