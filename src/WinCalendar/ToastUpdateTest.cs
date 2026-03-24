using Microsoft.Toolkit.Uwp.Notifications;

namespace WinCalendar;

/// <summary>
/// Quick test to see if updating a toast notification causes visible flicker.
/// Run this from the main form to test the behavior.
/// </summary>
public static class ToastUpdateTest
{
    public static void RunTest()
    {
        // Show initial toast
        var startTime = DateTime.Now;
        ShowTestToast(startTime, 0);

        // Update it every 2 seconds for 10 seconds to demonstrate the effect
        var timer = new System.Windows.Forms.Timer();
        var counter = 0;
        timer.Interval = 2000; // 2 seconds
        timer.Tick += (s, e) =>
        {
            counter++;
            ShowTestToast(startTime, counter);

            if (counter >= 5) // Stop after 5 updates (10 seconds total)
            {
                timer.Stop();
                timer.Dispose();
            }
        };
        timer.Start();
    }

    private static void ShowTestToast(DateTime startTime, int updateCount)
    {
        var elapsed = (int)(DateTime.Now - startTime).TotalSeconds;

        var builder = new ToastContentBuilder()
            .AddText($"Test Toast (Update #{updateCount})")
            .AddText($"Elapsed time: {elapsed} seconds")
            .AddText("Watch for flicker when this updates")
            .SetToastScenario(ToastScenario.Reminder)
            .AddButton(new ToastButton()
                .SetContent("Dismiss")
                .AddArgument("action", "dismiss"));

        // Use the same tag so it replaces the previous toast
        builder.Show(toast =>
        {
            toast.Tag = "test-toast";
            toast.Group = "test";
        });
    }
}
