using System;
using CommunityToolkit.Mvvm.ComponentModel;
namespace UserIdentityDesktop.ViewModels;
public partial class ClockDisplaySettings : ObservableObject
{
    public static ClockDisplaySettings Shared { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Identifier))] private bool _twelveHour;
    partial void OnTwelveHourChanged(bool value) => UserIdentityDesktop.Core.Models.WorkClock.TwelveHour = value;
    private ClockDisplaySettings() => UserIdentityDesktop.Core.Locale.LocaleCatalog.Changed+=()=>{OnPropertyChanged(nameof(Identifier));};
    public string Identifier => TwelveHour ? "12HourClock" : "24HourClock";
    public string Time(DateTime value) => value.ToString(TwelveHour ? "h:mm tt" : "HH:mm", UserIdentityDesktop.Core.Locale.LocaleCatalog.DisplayCulture);
}
