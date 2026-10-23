using System.Text.Json;
using LocalClip.Core.Media;

namespace LocalClip.Core.Tests;

public class ImportedDisplayOrientationTests
{
    [Theory]
    [InlineData(90, 180, 320)]
    [InlineData(-90, 180, 320)]
    [InlineData(270, 180, 320)]
    [InlineData(450, 180, 320)]
    [InlineData(0, 320, 180)]
    [InlineData(180, 320, 180)]
    public void DisplayMatrixMatchesTheOrientationFfmpegWillEncode(int rotation, int width, int height)
    {
        var stream = JsonSerializer.SerializeToElement(new {
            width = 320, height = 180, side_data_list = new[] { new { rotation } }
        });
        Assert.Equal((width, height), MediaProber.ReadDisplayDimensions(stream));
    }

    [Fact]
    public void MatrixRotationTakesPrecedenceOverLegacyTagAndMalformedMetadataIsIgnored()
    {
        using var matrix = JsonDocument.Parse("{\"width\":320,\"height\":180,\"side_data_list\":[{\"rotation\":0}],\"tags\":{\"rotate\":\"90\"}}");
        Assert.Equal((320, 180), MediaProber.ReadDisplayDimensions(matrix.RootElement));
        using var legacy = JsonDocument.Parse("{\"width\":320,\"height\":180,\"side_data_list\":[null,{\"rotation\":\"bad\"}],\"tags\":{\"rotate\":\"-90\"}}");
        Assert.Equal((180, 320), MediaProber.ReadDisplayDimensions(legacy.RootElement));
        using var malformed = JsonDocument.Parse("{\"width\":320,\"height\":180,\"tags\":{\"rotate\":\"NaN\"}}");
        Assert.Equal((320, 180), MediaProber.ReadDisplayDimensions(malformed.RootElement));
    }
}
