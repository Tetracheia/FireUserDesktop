using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using UserIdentityDesktop.ViewModels;

namespace UserIdentityDesktop.Views;

/// <summary>Carry the user's motion preference into owned windows and inherited control styling.</summary>
public sealed class MotionPreferences : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<MotionPreferences,Window,bool>("Enabled");
    public static readonly AttachedProperty<bool> ReducedProperty = AvaloniaProperty.RegisterAttached<MotionPreferences,Control,bool>("Reduced",inherits:true);
    private static readonly AttachedProperty<State?> StateProperty = AvaloniaProperty.RegisterAttached<MotionPreferences,Window,State?>("State");
    public static bool GetEnabled(Window window)=>window.GetValue(EnabledProperty);
    public static void SetEnabled(Window window,bool value)=>window.SetValue(EnabledProperty,value);
    public static bool GetReduced(Control control)=>control.GetValue(ReducedProperty);
    public static void SetReduced(Control control,bool value)=>control.SetValue(ReducedProperty,value);
    static MotionPreferences()=>EnabledProperty.Changed.AddClassHandler<Window>((window,_)=>
    {
        window.GetValue(StateProperty)?.Dispose();window.ClearValue(StateProperty);
        if(GetEnabled(window))window.SetValue(StateProperty,new State(window));
    });
    private sealed class State : IDisposable
    {
        private readonly Window window;
        private MainViewModel? main;
        private WorkspaceViewModel? workspace;
        internal State(Window window)
        {
            this.window=window;window.DataContextChanged+=ContextChanged;window.Opened+=ContextChanged;window.Closed+=Closed;Refresh();
        }
        private void ContextChanged(object? sender,EventArgs e)=>Refresh();
        private void Closed(object? sender,EventArgs e)=>Dispose();
        private void Changed(object? sender,PropertyChangedEventArgs e)
        { if(e.PropertyName==nameof(MainViewModel.CurrentView))Refresh();else if(e.PropertyName==nameof(WorkspaceViewModel.ReduceMotion))Apply(); }
        private void Refresh()
        {
            if(main!=null)main.PropertyChanged-=Changed;if(workspace!=null)workspace.PropertyChanged-=Changed;
            object? context=window.DataContext;
            for(var owner=window.Owner;context is not (WorkspaceViewModel or MainViewModel or ChatViewModel) && owner!=null;owner=owner.Owner)context=owner.DataContext;
            main=context as MainViewModel;
            workspace=context as WorkspaceViewModel ?? (context as ChatViewModel)?.Workspace ?? main?.CurrentView as WorkspaceViewModel ?? App.MainViewModelInstance?.CurrentView as WorkspaceViewModel;
            if(main!=null)main.PropertyChanged+=Changed;if(workspace!=null)workspace.PropertyChanged+=Changed;Apply();
        }
        private void Apply()
        {
            var reduced=Environment.GetEnvironmentVariable("FIREUSER_REDUCED_MOTION")=="1" || workspace?.ReduceMotion==true;
            SetReduced(window,reduced);window.Classes.Set("reduced-motion",reduced);
        }
        public void Dispose()
        {
            window.DataContextChanged-=ContextChanged;window.Opened-=ContextChanged;window.Closed-=Closed;
            if(main!=null)main.PropertyChanged-=Changed;if(workspace!=null)workspace.PropertyChanged-=Changed;
        }
    }
}
