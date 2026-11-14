using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using UserIdentityDesktop.Core.Locale;
namespace UserIdentityDesktop.Localization;
public sealed class TranslateExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding($"[{key}]") { Source=LocalizedStrings.Shared };
}
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    public static LocalizedStrings Shared { get; } = new();
    private readonly Dictionary<string,string> labels;
    private LocalizedStrings()
    {
        using var stream=typeof(LocalizedStrings).Assembly.GetManifestResourceStream("UserIdentityDesktop.Localization.labels.json")!;
        labels=JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
        LocaleCatalog.Changed+=()=>Dispatcher.UIThread.Post(()=>{
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Direction)));
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Language)));
        });
    }
    public static Binding Bind(string text)
    {
        var key="loc_"+System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..12];
        return new Binding($"[{key}]") {Source=Shared};
    }
    public string Language => LocaleCatalog.Current;
    public string this[string key] => LocaleCatalog.Text(labels.GetValueOrDefault(key,key));
    public FlowDirection Direction => LocaleCatalog.Current=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SystemTextConverter : Avalonia.Data.Converters.IValueConverter, Avalonia.Data.Converters.IMultiValueConverter
{
    public static SystemTextConverter Shared { get; } = new();
    public object? Convert(object? value,Type targetType,object? parameter,System.Globalization.CultureInfo culture) => value is string text?LocaleCatalog.Text(text):value;
    public object? Convert(IList<object?> values,Type targetType,object? parameter,System.Globalization.CultureInfo culture) => Convert(values.Count>0?values[0]:null,targetType,parameter,culture);
    public object? ConvertBack(object? value,Type targetType,object? parameter,System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

public sealed class TranslateBindingExtension(string path) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var result=new MultiBinding {Converter=SystemTextConverter.Shared};
        result.Bindings.Add(new Binding(path));
        result.Bindings.Add(new Binding(nameof(LocalizedStrings.Language)) {Source=LocalizedStrings.Shared});
        return result;
    }
}
