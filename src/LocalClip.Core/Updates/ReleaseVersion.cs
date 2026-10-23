using System.Text.RegularExpressions;

namespace LocalClip.Core.Updates;

/// <summary>SemVer 2 precedence, including numeric prerelease identifiers and ignored build metadata.</summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        @"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly string[] _numbers;
    private readonly string[] _prerelease;

    private ReleaseVersion(string text, string[] numbers, string[] prerelease)
    {
        Text = text;
        _numbers = numbers;
        _prerelease = prerelease;
    }

    public string Text { get; }
    public bool IsPrerelease => _prerelease.Length != 0;

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = null!;
        if (string.IsNullOrEmpty(text) || text.Length > 128) return false;
        var match = Pattern.Match(text);
        if (!match.Success) return false;
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (prerelease.Any(part => IsNumeric(part) && part.Length > 1 && part[0] == '0')) return false;
        version = new ReleaseVersion(text[0] == 'v' ? text[1..] : text,
            [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value], prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        for (var index = 0; index < _numbers.Length; index++)
        {
            var comparison = CompareNumber(_numbers[index], other._numbers[index]);
            if (comparison != 0) return comparison;
        }
        if (!IsPrerelease || !other.IsPrerelease)
            return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
        for (var index = 0; index < Math.Min(_prerelease.Length, other._prerelease.Length); index++)
        {
            var left = _prerelease[index];
            var right = other._prerelease[index];
            var leftNumeric = IsNumeric(left);
            var rightNumeric = IsNumeric(right);
            var comparison = leftNumeric && rightNumeric ? CompareNumber(left, right)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1
                : string.CompareOrdinal(left, right);
            if (comparison != 0) return comparison;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    private static bool IsNumeric(string value) => value.All(character => character is >= '0' and <= '9');
    private static int CompareNumber(string left, string right) => left.Length == right.Length
        ? string.CompareOrdinal(left, right) : left.Length.CompareTo(right.Length);
}
