using LocalClip.Core.Updates;

namespace LocalClip.Core.Tests;

public class UpdateVersionTests
{
    [Fact]
    public void UsesCanonicalSemverPrereleaseOrdering()
    {
        string[] precedence = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"];
        for (var index = 1; index < precedence.Length; index++)
        {
            Assert.True(ReleaseVersion.TryParse(precedence[index - 1], out var earlier));
            Assert.True(ReleaseVersion.TryParse(precedence[index], out var later));
            Assert.True(earlier.CompareTo(later) < 0);
            Assert.True(later.CompareTo(earlier) > 0);
        }
    }

    [Fact]
    public void BuildMetadataDoesNotChangePrecedenceOrPreviewClassification()
    {
        Assert.True(ReleaseVersion.TryParse("v1.0.3+ci-123", out var build));
        Assert.True(ReleaseVersion.TryParse("1.0.3+other", out var other));
        Assert.False(build.IsPrerelease);
        Assert.Equal("1.0.3+ci-123", build.Text);
        Assert.Equal(0, build.CompareTo(other));
        Assert.True(ReleaseVersion.TryParse("v1.0.3-preview.1+ci-123", out var preview));
        Assert.True(preview.IsPrerelease);
    }
}
