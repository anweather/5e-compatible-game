using NUnit.Framework;

// BuildVersion: parsing and comparing versions, finding a newer release in
// GitHub's releases JSON (the update check), and the join handshake the host
// checks (NetSession.Approve).
public class BuildVersionTests
{
    [TestCase("0.1.0", 0, 1, 0)]
    [TestCase("alpha-1.20.3", 1, 20, 3)]
    [TestCase("v2.0.10", 2, 0, 10)]
    [TestCase("0.2.0-rc1", 0, 2, 0)]
    public void ParsesVersions(string text, int major, int minor, int patch)
    {
        Assert.That(BuildVersion.Parse(text), Is.EqualTo(new[] { major, minor, patch }));
    }

    [TestCase("")]
    [TestCase("0.1")]
    [TestCase("1.2.x")]
    [TestCase("latest")]
    public void RejectsNonVersions(string text) => Assert.That(BuildVersion.Parse(text), Is.Null);

    [Test]
    public void ComparesBySemverNotByText()
    {
        Assert.That(BuildVersion.Compare("0.10.0", "0.9.0"), Is.Positive);
        Assert.That(BuildVersion.Compare("0.1.2", "0.1.10"), Is.Negative);
        Assert.That(BuildVersion.Compare("1.0.0", "alpha-1.0.0"), Is.Zero);
        Assert.That(BuildVersion.Compare("junk", "0.0.1"), Is.Negative);
    }

    const string Releases = @"[
      { ""tag_name"": ""alpha-0.3.0"", ""draft"": true,  ""prerelease"": true, ""html_url"": ""https://example/draft"" },
      { ""tag_name"": ""alpha-0.2.1"", ""draft"": false, ""prerelease"": true, ""html_url"": ""https://example/0.2.1"" },
      { ""tag_name"": ""alpha-0.10.0"", ""draft"": false, ""prerelease"": true, ""html_url"": ""https://example/0.10.0"" },
      { ""tag_name"": ""beta-9.0.0"",  ""draft"": false, ""prerelease"": false, ""html_url"": ""https://example/beta"" },
      { ""tag_name"": ""alpha-0.1.0"", ""draft"": false, ""prerelease"": true, ""html_url"": ""https://example/0.1.0"" }
    ]";

    [Test]
    public void FindsTheNewestAlphaReleaseSkippingDraftsAndOtherTags()
    {
        var newer = BuildVersion.NewestRelease(Releases, "0.1.0");
        Assert.That(newer.Version, Is.EqualTo("0.10.0"));
        Assert.That(newer.Url, Is.EqualTo("https://example/0.10.0"));
    }

    [Test]
    public void IsUpToDateWhenNothingIsNewer()
    {
        Assert.That(BuildVersion.NewestRelease(Releases, "0.10.0"), Is.Null);
        Assert.That(BuildVersion.NewestRelease("[]", "0.1.0"), Is.Null); // no releases yet
    }

    [Test]
    public void IgnoresResponsesThatArentAReleasesList()
    {
        Assert.That(BuildVersion.NewestRelease(@"{ ""message"": ""API rate limit exceeded"" }", "0.1.0"), Is.Null);
        Assert.That(BuildVersion.NewestRelease("<html>", "0.1.0"), Is.Null);
    }

    static BuildVersion.Stamp Stamp(string version, string commit, bool editor = false) =>
        new BuildVersion.Stamp { Version = version, Commit = commit, Editor = editor };

    [Test]
    public void HandshakeRoundTrips()
    {
        var back = BuildVersion.Decode(BuildVersion.Encode(Stamp("0.1.0", "abc1234", true)));
        Assert.That((back.Version, back.Commit, back.Editor), Is.EqualTo(("0.1.0", "abc1234", true)));
        Assert.That(BuildVersion.Decode(new byte[0]), Is.Null);
        Assert.That(BuildVersion.Decode(System.Text.Encoding.UTF8.GetBytes("hello")), Is.Null);
    }

    [Test]
    public void SameBuildIsAllowed()
    {
        Assert.That(BuildVersion.Mismatch(Stamp("0.1.0", "abc1234"), Stamp("0.1.0", "abc1234")), Is.Null);
        // The Editor's commit is whatever is checked out, so only the version counts.
        Assert.That(BuildVersion.Mismatch(Stamp("0.1.0", "abc1234"), Stamp("0.1.0", "fff0000", editor: true)), Is.Null);
    }

    [Test]
    public void AnOlderClientIsToldToUpdate()
    {
        string reason = BuildVersion.Mismatch(Stamp("0.1.1", "def5678"), Stamp("0.1.0", "abc1234"));
        Assert.That(reason, Is.EqualTo($"Host is on alpha 0.1.1 (def5678); you're on alpha 0.1.0 (abc1234). Update from {BuildVersion.ReleasesPage}"));
    }

    [Test]
    public void ANewerClientIsToldTheHostMustUpdate()
    {
        string reason = BuildVersion.Mismatch(Stamp("0.1.0", "abc1234"), Stamp("0.2.0", "0000000"));
        StringAssert.Contains("The host needs to update", reason);
    }

    [Test]
    public void SameVersionFromAnotherCommitIsRefused()
    {
        Assert.That(BuildVersion.Mismatch(Stamp("0.1.0", "abc1234"), Stamp("0.1.0", "abc9999")), Is.Not.Null);
    }

    [Test]
    public void AClientWithoutAHandshakeIsRefused()
    {
        StringAssert.Contains("older or different build", BuildVersion.Mismatch(Stamp("0.1.0", "abc1234"), null));
    }

    [Test]
    public void JoinFailureUsesTheHostsReasonOrGuessesTheVersion()
    {
        Assert.That(BuildVersion.JoinFailure("Host is on alpha 0.1.1"), Is.EqualTo("Couldn't join: Host is on alpha 0.1.1"));
        string silent = BuildVersion.JoinFailure("[Disconnect Event][Client-0][TransportClientId-0][TransportDisconnect] Disconnected.");
        StringAssert.Contains("probably a different version", silent);
        StringAssert.Contains("probably a different version", BuildVersion.JoinFailure(""));
    }
}
