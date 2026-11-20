using System.Globalization;
using System.Windows.Data;
using StarComms.Core;

namespace StarComms.App;

public sealed class SpeakingChannelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [uint id, IReadOnlyDictionary<uint, VoiceChannel> speakers] && speakers.TryGetValue(id, out var channel)
            ? channel.ToString() : "";

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
