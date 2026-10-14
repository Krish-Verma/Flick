using System;

namespace LocalClip.Core.Rules;

public class TrimValidationResult
{
    public bool IsValid { get; set; }
    public double ValidatedStart { get; set; }
    public double ValidatedEnd { get; set; }
    public double ValidatedDuration => Math.Max(0.0, ValidatedEnd - ValidatedStart);
    public string ErrorMessage { get; set; } = "";
}

public static class TrimValidator
{
    public const double EPSILON_SECONDS = 0.05;

    public static TrimValidationResult Validate(double startSec, double endSec, double originalDuration)
    {
        if (originalDuration <= 0.0)
        {
            return new TrimValidationResult
            {
                IsValid = false,
                ErrorMessage = "Original clip duration is non-positive or unavailable."
            };
        }

        // Clamp negative start
        if (startSec < 0.0)
        {
            if (startSec < -EPSILON_SECONDS)
            {
                return new TrimValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Start time ({startSec:F2}s) cannot be negative."
                };
            }
            startSec = 0.0;
        }

        // Default endSec to originalDuration if 0
        if (endSec <= 0.0)
        {
            endSec = originalDuration;
        }

        // Check end within original duration + tolerance
        if (endSec > originalDuration + EPSILON_SECONDS)
        {
            return new TrimValidationResult
            {
                IsValid = false,
                ErrorMessage = $"End time ({endSec:F2}s) exceeds original duration ({originalDuration:F2}s)."
            };
        }

        if (endSec > originalDuration)
        {
            endSec = originalDuration;
        }

        if (endSec <= startSec)
        {
            return new TrimValidationResult
            {
                IsValid = false,
                ErrorMessage = $"End time ({endSec:F2}s) must be strictly greater than start time ({startSec:F2}s)."
            };
        }

        double duration = endSec - startSec;
        if (duration < 0.25)
        {
            return new TrimValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Selected trim range ({duration:F2}s) is too short (minimum 0.25s)."
            };
        }

        return new TrimValidationResult
        {
            IsValid = true,
            ValidatedStart = startSec,
            ValidatedEnd = endSec
        };
    }
}
