using System;
using System.Globalization;
using Calendar = Avalonia.Controls.Calendar;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UserIdentityDesktop.Core.Models;

namespace UserIdentityDesktop.Views;

/// <summary>A native date/time popover with the same single trigger and Done flow as the web compose fields.</summary>
public sealed class ComposeDateTimePicker : UserControl
{
    public static readonly StyledProperty<DateTimeOffset?> DateProperty=AvaloniaProperty.Register<ComposeDateTimePicker,DateTimeOffset?>(nameof(Date),defaultBindingMode:Avalonia.Data.BindingMode.TwoWay);
    public DateTimeOffset? Date {get=>GetValue(DateProperty);set=>SetValue(DateProperty,value);}
    public static readonly StyledProperty<TimeSpan?> TimeProperty=AvaloniaProperty.Register<ComposeDateTimePicker,TimeSpan?>(nameof(Time),defaultBindingMode:Avalonia.Data.BindingMode.TwoWay);
    public TimeSpan? Time {get=>GetValue(TimeProperty);set=>SetValue(TimeProperty,value);}
    public static readonly StyledProperty<string> ClockIdentifierProperty=AvaloniaProperty.Register<ComposeDateTimePicker,string>(nameof(ClockIdentifier),"24HourClock");
    public string ClockIdentifier {get=>GetValue(ClockIdentifierProperty);set=>SetValue(ClockIdentifierProperty,value);}
    public static readonly StyledProperty<bool> ClearableProperty=AvaloniaProperty.Register<ComposeDateTimePicker,bool>(nameof(Clearable));
    public bool Clearable {get=>GetValue(ClearableProperty);set=>SetValue(ClearableProperty,value);}
    private readonly Button clear;
    private readonly Button trigger;
    private readonly TextBlock label;
    private readonly Calendar calendar;
    private readonly TimePicker clock;
    private readonly Popup popup;
    private bool syncing;
    public bool IsOpen {get=>popup.IsOpen;set{popup.IsOpen=value;if(value){calendar.DisplayDate=Date?.Date??System.DateTime.Today;calendar.Focus();}else trigger.Focus();}}
    public Calendar Calendar=>calendar;
    public void FocusTrigger()=>trigger.Focus();
    public ComposeDateTimePicker()
    {
        label=new TextBlock{VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=15.2,FontWeight=FontWeight.Normal};label.Bind(TextBlock.ForegroundProperty,this.GetResourceObservable("ForgeTextPrimary"));
        var icon=DesktopIconography.Icon("Calendar");icon.Width=16;icon.Height=16;icon.Bind(PathIcon.ForegroundProperty,this.GetResourceObservable("ForgePrimary"));
        var arrow=new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse("M4,6 L8,10 12,6"),StrokeThickness=1.6,Stretch=Stretch.Uniform,Width=12,Height=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};arrow.Bind(Avalonia.Controls.Shapes.Path.StrokeProperty,this.GetResourceObservable("ForgeTextSecondary"));
        var face=new Grid{ColumnDefinitions=new ColumnDefinitions("40,*,40")};face.Children.Add(icon);face.Children.Add(label);Grid.SetColumn(label,1);face.Children.Add(arrow);Grid.SetColumn(arrow,2);
        trigger=new Button{Content=face,Padding=new Thickness(0),MinHeight=46,CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};DesktopIconography.SetEnabled(trigger,false);trigger.Bind(BackgroundProperty,this.GetResourceObservable("ComposeInput"));trigger.Bind(BorderBrushProperty,this.GetResourceObservable("ForgeBorder"));AutomationProperties.SetName(trigger,"Choose date and time");
        calendar=new Calendar{IsTodayHighlighted=true};clock=new TimePicker{HorizontalAlignment=HorizontalAlignment.Stretch};
        var done=new Button{Content="Done",HorizontalAlignment=HorizontalAlignment.Right,Classes={"fire-btn"}};
        clear=new Button{Content="Clear",Classes={"quiet"},IsVisible=false};
        var footer=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),Children={clear,done}};Grid.SetColumn(done,1);
        var content=new StackPanel{Spacing=12,Children={calendar,new TextBlock{Text="Time"},clock,footer}};
        var surface=new Border{Child=content,Padding=new Thickness(14),CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1)};surface.Bind(BackgroundProperty,this.GetResourceObservable("ForgeSurfaceCard"));surface.Bind(BorderBrushProperty,this.GetResourceObservable("ForgeBorder"));
        popup=new Popup{PlacementTarget=trigger,Placement=PlacementMode.Bottom,IsLightDismissEnabled=true,Child=surface};
        var root=new Panel{Children={trigger,popup}};Content=root;
        trigger.Click+=(_,_)=>IsOpen=!IsOpen;done.Click+=(_,_)=>IsOpen=false;clear.Click+=(_,_)=>{SetCurrentValue(DateProperty,null);SetCurrentValue(TimeProperty,null);IsOpen=false;};
        calendar.SelectedDatesChanged+=(_,_)=>{if(!syncing){var selected=calendar.SelectedDate;SetCurrentValue(DateProperty,selected==null?null:new DateTimeOffset(System.DateTime.SpecifyKind(selected.Value.Date,DateTimeKind.Unspecified)));}};
        clock.PropertyChanged+=(_,e)=>{if(e.Property==TimePicker.SelectedTimeProperty&&!syncing)SetCurrentValue(TimeProperty,clock.SelectedTime);};
        PropertyChanged+=(_,e)=>{if(e.Property==DateProperty||e.Property==TimeProperty||e.Property==ClockIdentifierProperty||e.Property==ClearableProperty)Update();};
        surface.AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Escape){IsOpen=false;e.Handled=true;}},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DetachedFromVisualTree+=(_,_)=>popup.IsOpen=false;
        AttachedToVisualTree+=(_,_)=>{if(this.TryFindResource("FireCalendarTheme",out var theme)&&theme is Avalonia.Styling.ControlTheme calendarTheme)calendar.Theme=calendarTheme;};Update();
    }
    private void Update()
    {
        clear.IsVisible=Clearable;
        syncing=true;
        try{calendar.SelectedDate=Date?.Date;clock.SelectedTime=Time;clock.ClockIdentifier=ClockIdentifier;}
        finally{syncing=false;}
        var time=Time is {} value?System.DateTime.Today.Add(value).ToString(ClockIdentifier=="12HourClock"?"h:mm tt":"HH:mm",CultureInfo.CurrentCulture):"";
        label.Text=Date is {} day?day.ToString("dd-MM-yyyy",CultureInfo.InvariantCulture)+(time.Length>0?"  "+time:""):"dd-MM-yyyy HH:mm";
    }
}
