using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UserIdentityDesktop.Core.Models;

namespace UserIdentityDesktop.Views;

/// <summary>Native day cards and balance impact from the server's exact vacation preview.</summary>
internal sealed class VacationReviewWindow : Window
{
    public VacationReviewWindow(DateTimeOffset? start, DateTimeOffset? end, string reason, VacationPreview review)
    {
        ComposeAppearance.Apply(this);
        Title="Review vacation request";Width=680;Height=640;MinWidth=440;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty,this.GetResourceObservable("ForgeBg"));this.Bind(ForegroundProperty,this.GetResourceObservable("ForgeTextPrimary"));
        var heading=new StackPanel {Spacing=6,Margin=new Thickness(20,16)};
        heading.Children.Add(new TextBlock{Text=Title,FontSize=16.8,FontWeight=FontWeight.Bold});
        heading.Children.Add(new TextBlock{Text=$"{start:ddd, d MMM yyyy} – {end:ddd, d MMM yyyy}",TextWrapping=TextWrapping.Wrap});
        var body=new StackPanel{Spacing=8,Margin=new Thickness(20,0,20,16)};
        var days=review.Shifts.Sum(x=>x.Days);
        var total=new TextBlock{Text=$"{days:0.##} leave {(days==1?"day":"days")}",FontSize=24,FontWeight=FontWeight.SemiBold};total.Bind(TextBlock.ForegroundProperty,this.GetResourceObservable("ForgePrimary"));body.Children.Add(total);
        var balances=new Grid{ColumnSpacing=12,RowSpacing=8};
        var balanceCards=new System.Collections.Generic.List<Border>();
        if(review.Balances.Count>0)body.Children.Add(balances);
        foreach(var balance in review.Balances)
        {
            var requested=review.Shifts.Where(x=>x.AnchorDate.Year==balance.Year).Sum(x=>x.Days);
            var content=new StackPanel{Spacing=8};content.Children.Add(new TextBlock{Text=$"{balance.Year} balance",FontWeight=FontWeight.SemiBold});
            var values=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto,*"),ColumnSpacing=16};
            values.Children.Add(Metric("Available",$"{balance.Available:0.##} days"));var arrow=new TextBlock{Text="→",FontSize=24,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(arrow,1);values.Children.Add(arrow);
            var after=Metric("After this request",$"{balance.Available-requested:0.##} days");Grid.SetColumn(after,2);values.Children.Add(after);content.Children.Add(values);var card=Card(content);balanceCards.Add(card);balances.Children.Add(card);
        }
        void ArrangeBalances()
        {
            var columns=balanceCards.Count>1 && (ClientSize.Width>0?ClientSize.Width:Width)>=600?2:1;
            balances.ColumnDefinitions=new ColumnDefinitions(columns==2?"*,*":"*");
            balances.RowDefinitions=new RowDefinitions(string.Join(",",Enumerable.Repeat("Auto",Math.Max(1,(balanceCards.Count+columns-1)/columns))));
            for(var i=0;i<balanceCards.Count;i++){Grid.SetColumn(balanceCards[i],i%columns);Grid.SetRow(balanceCards[i],i/columns);}
        }
        ArrangeBalances();SizeChanged+=(_,_)=>ArrangeBalances();
        if(!string.IsNullOrWhiteSpace(reason))body.Children.Add(new StackPanel{Spacing=4,Children={new TextBlock{Text="Reason",FontWeight=FontWeight.SemiBold},new TextBlock{Text=reason,TextWrapping=TextWrapping.Wrap}}});
        body.Children.Add(new TextBlock{Text="Selected shifts",FontSize=16,FontWeight=FontWeight.SemiBold});
        body.Children.Add(new TextBlock{Text="Weekends, public holidays and existing reservations are excluded. Times shown in "+TimeZoneInfo.Local.DisplayName+".",TextWrapping=TextWrapping.Wrap,FontSize=12});
        foreach(var shift in review.Shifts)
        {
            var content=new StackPanel{Spacing=6};content.Children.Add(new TextBlock{Text=shift.AnchorDate.ToString("dddd, d MMM yyyy"),FontWeight=FontWeight.SemiBold});
            content.Children.Add(new TextBlock{Text=shift.HoursLabel,TextWrapping=TextWrapping.Wrap});var card=Card(content);card.Classes.Add("vacation-shift-card");body.Children.Add(card);
        }
        if(review.Shifts.Count==0)body.Children.Add(new TextBlock{Text="No working shifts fall within these dates.",TextWrapping=TextWrapping.Wrap});
        var motion=new WindowMotion(this);
        var back=new Button{Content="Back",MinHeight=40};back.Classes.Add("quiet");back.Click+=async(_,_)=>await motion.CloseAsync(false);
        var confirm=new Button{Name="ConfirmVacationRequest",Content="Confirm vacation request",MinHeight=40,IsEnabled=days>0};confirm.Classes.Add("fire-btn");confirm.Click+=async(_,_)=>await motion.CloseAsync(true);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Children={back,confirm}};
        var footer=new Border{Padding=new Thickness(20,12),BorderThickness=new Thickness(0,1,0,0),Child=actions};footer.Bind(Border.BorderBrushProperty,this.GetResourceObservable("ForgeBorder"));footer.Bind(Border.BackgroundProperty,this.GetResourceObservable("ForgeSurface"));
        var layout=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto")};layout.Children.Add(heading);var scroll=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);layout.Children.Add(scroll);Grid.SetRow(footer,2);layout.Children.Add(footer);Content=layout;
        KeyDown+=async(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;await motion.CloseAsync(false);}};
        Opened+=(_,_)=>{if(Owner is {} owner && owner.Screens.ScreenFromWindow(owner) is {} screen)MaxHeight=Math.Max(400,screen.WorkingArea.Height/screen.Scaling-48);back.Focus();};
    }
    private static StackPanel Metric(string label,string value)=>new(){Spacing=4,Children={new TextBlock{Text=label,FontSize=12,TextWrapping=TextWrapping.Wrap},new TextBlock{Text=value,FontSize=21,FontWeight=FontWeight.SemiBold}}};
    private static Border Card(Control content)
    {
        var card=new Border{Child=content,Padding=new Thickness(12),CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1)};card.Bind(Border.BackgroundProperty,card.GetResourceObservable("ForgeSurfaceCard"));card.Bind(Border.BorderBrushProperty,card.GetResourceObservable("ForgeBorder"));return card;
    }
}
