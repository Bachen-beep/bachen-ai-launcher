using System.Drawing;

namespace BaChenAiLauncher;

internal readonly record struct ServiceControlLayout(
    bool ShowLabels,
    int Height,
    Point StopButton,
    Point RefreshButton,
    Point OpenButton,
    Point CheckUpdatesButton,
    Point UpdateSourceButton,
    Point StatusLabel);

internal static class ServiceControlLayoutPlanner
{
    private const int MinimumWidth = 760;
    private const int WideLayoutWidth = 1240;
    private const int MediumLayoutWidth = 920;
    private const int Gap = 12;

    public static ServiceControlLayout Calculate(int availableWidth, int stopWidth, int refreshWidth, int openWidth, int checkWidth, int updateWidth)
    {
        var width = Math.Max(MinimumWidth, availableWidth);
        if (width >= WideLayoutWidth)
        {
            var actionGroupWidth = stopWidth + refreshWidth + openWidth + checkWidth + updateWidth + Gap * 4;
            var actionStart = Math.Max(282, width - actionGroupWidth - 30);
            var stop = new Point(actionStart, 23);
            var refresh = new Point(stop.X + stopWidth + Gap, 23);
            var open = new Point(refresh.X + refreshWidth + Gap, 23);
            var check = new Point(open.X + openWidth + Gap, 23);
            var update = new Point(check.X + checkWidth + Gap, 23);
            return new ServiceControlLayout(true, 120, stop, refresh, open, check, update, new Point(31, 78));
        }

        if (width >= MediumLayoutWidth)
        {
            const int actionStart = 330;
            var stop = new Point(actionStart, 16);
            var refresh = new Point(stop.X + stopWidth + Gap, 16);
            var open = new Point(refresh.X + refreshWidth + Gap, 16);
            var check = new Point(actionStart, 68);
            var update = new Point(check.X + checkWidth + Gap, 68);
            return new ServiceControlLayout(true, 164, stop, refresh, open, check, update, new Point(31, 120));
        }

        var compactStop = new Point(30, 16);
        var compactRefresh = new Point(compactStop.X + stopWidth + Gap, 16);
        var compactOpen = new Point(compactRefresh.X + refreshWidth + Gap, 16);
        var compactCheck = new Point(30, 68);
        var compactUpdate = new Point(compactCheck.X + checkWidth + Gap, 68);
        return new ServiceControlLayout(false, 164, compactStop, compactRefresh, compactOpen, compactCheck, compactUpdate, new Point(31, 120));
    }
}
