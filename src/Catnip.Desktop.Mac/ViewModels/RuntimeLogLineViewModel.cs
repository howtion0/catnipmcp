using Catnip.Desktop.Mac.Models;

namespace Catnip.Desktop.Mac.ViewModels;

public sealed record RuntimeLogLineViewModel(string Timestamp, string Stream, string Message)
{
    public static RuntimeLogLineViewModel FromModel(RuntimeLogLine line)
    {
        return new RuntimeLogLineViewModel(
            line.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
            line.Stream,
            line.Message);
    }
}
