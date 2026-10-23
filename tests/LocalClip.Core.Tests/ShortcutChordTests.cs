using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public class ShortcutChordTests
{
    [Theory]
    [InlineData(6u, 120u, true)] // Ctrl+Shift+F9
    [InlineData(6u, 119u, true)]
    [InlineData(0u, 65u, false)]
    [InlineData(4u, 65u, false)]
    [InlineData(10u, 65u, false)] // Win reserved
    [InlineData(3u, 46u, false)] // Ctrl Alt Delete
    [InlineData(1u, 9u, false)]
    [InlineData(1u, 115u, false)]
    [InlineData(6u, 27u, false)]
    [InlineData(2u, 17u, false)]
    [InlineData(2u, 123u, false)]
    public void RejectsUnsafeOrIncompleteChords(uint modifiers, uint key, bool allowed) =>
        Assert.Equal(allowed, ShortcutChordRules.IsAllowed(modifiers, key));
}
