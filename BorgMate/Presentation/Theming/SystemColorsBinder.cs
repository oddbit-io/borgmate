using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace BorgMate.Presentation.Theming;

/// <summary>
/// Keeps the accent resources in sync with the operating system's accent colour for the
/// lifetime of the application.
/// </summary>
public class SystemColorsBinder
{
    private const double HoverLightnessShift = 0.12;

    private readonly Application _app;

    // Avalonia reports a real system accent only on Windows and macOS; Linux keeps the brand accent.
    private readonly bool _followsSystemAccent = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public SystemColorsBinder(Application app, IPlatformSettings settings)
    {
        _app = app;
        Apply(settings.GetColorValues());
        settings.ColorValuesChanged += (_, values) => Apply(values);
    }

    private void Apply(PlatformColorValues values)
    {
        if (_followsSystemAccent)
            ApplyAccent(values.AccentColor1);
    }

    private void ApplyAccent(Color accent)
    {
        _app.Resources["AccentColor"] = accent;
        _app.Resources["AccentHoverColor"] = ShiftLightness(accent, HoverLightnessShift);
        _app.Resources["AccentPressedColor"] = ShiftLightness(accent, -HoverLightnessShift);
    }

    private static Color ShiftLightness(Color color, double shift)
    {
        var hsl = color.ToHsl();
        return new HslColor(hsl.A, hsl.H, hsl.S, Math.Clamp(hsl.L + shift, 0, 1)).ToRgb();
    }
}
