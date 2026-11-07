using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using System.ComponentModel;
using UserIdentityDesktop.ViewModels;

namespace UserIdentityDesktop.Views;

internal static class ComposeAppearance
{
    public static void Apply(Window window)
    {
        window.Classes.Add("compose-form");
        window.Styles.Add(new StyleInclude(new Uri("avares://Fire%20User%20Connect/")){Source=new Uri("avares://Fire%20User%20Connect/Styles/ComposeForms.axaml")});
        AppearanceSettingsViewModel? appearance=null;
        void Refresh()
        {
            if(appearance==null)return;
            var palette=NativeAppearance.Palette((int)Math.Clamp(Math.Round(appearance.Brightness),0,100));
            window.RequestedThemeVariant=palette["ForgeTextPrimary"]==Colors.White?ThemeVariant.Dark:ThemeVariant.Light;
            foreach(var (key,color) in palette)window.Resources[key]=new SolidColorBrush(color);
            window.Resources["ComposeInput"]=new SolidColorBrush(palette["ForgeSurface"]);
        }
        void Changed(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(AppearanceSettingsViewModel.Brightness))Refresh();}
        window.Opened+=(_,_)=>
        {
            for(Window? owner=window;owner!=null;owner=owner.Owner as Window)
            {
                var workspace=owner.DataContext as WorkspaceViewModel ?? (owner.DataContext as ChatViewModel)?.Workspace;
                if(workspace==null)continue;
                appearance=workspace.Appearance;appearance.PropertyChanged+=Changed;
                if(appearance.Brightness is >0 and <100)Refresh();
                break;
            }
        };
        window.Closed+=(_,_)=>{if(appearance!=null)appearance.PropertyChanged-=Changed;};
    }
}
