using System;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace BorgMate.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (Design.IsDesignMode) return;

        // Spinner animations handled by job-spinner style in BorgTheme
    }

    private static void StartSpinnerAnimation(Panel panel)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(RotateTransform.AngleProperty, 0.0) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(RotateTransform.AngleProperty, 360.0) }
                }
            }
        };

        animation.RunAsync(panel);
    }
}
