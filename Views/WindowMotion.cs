using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace UserIdentityDesktop.Views;

internal sealed class WindowMotion
{
    private readonly Window window;
    private readonly SurfaceMotion motion=new();
    private bool closing,closed;
    private Task? closeTask;
    private readonly SurfaceMotionKind kind;
    public WindowMotion(Window window,SurfaceMotionKind kind=SurfaceMotionKind.Popup)
    {
        this.window=window;this.kind=kind;
        window.Opened+=(_,_)=>{if(window.Content is Control content)_=motion.EnterAsync(content,SurfaceMotion.Reduced(window),kind);};
        window.PropertyChanged+=PreferenceChanged;
        window.Closed+=(_,_)=>{closed=true;motion.Cancel();window.PropertyChanged-=PreferenceChanged;};
        window.Closing+=(_,e)=>
        {
            if(closing || e.IsProgrammatic || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown or WindowCloseReason.OwnerWindowClosing)return;
            e.Cancel=true;_=CloseAsync<object?>(null);
        };
    }
    private void PreferenceChanged(object? sender,AvaloniaPropertyChangedEventArgs e)
    {if(e.Property==MotionPreferences.ReducedProperty && MotionPreferences.GetReduced(window))motion.Cancel();}
    public Task CloseAsync<T>(T result)
    {
        if(closed)return Task.CompletedTask;
        if(closing)return closeTask??Task.CompletedTask;
        closing=true;return closeTask=CloseCoreAsync(result);
    }
    private async Task CloseCoreAsync<T>(T result)
    {
        try{if(window.Content is Control content)await motion.ExitAsync(content,SurfaceMotion.Reduced(window),kind);}
        finally{if(!closed)window.Close(result);}
    }
}
