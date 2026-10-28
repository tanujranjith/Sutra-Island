using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Q.Core;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;
namespace DynamicIsland.Windows.Views;

public partial class AssistantActionView : UserControl
{
    public static readonly DependencyProperty FeedbackProperty=DependencyProperty.Register(nameof(Feedback),typeof(AssistantActionFeedback),typeof(AssistantActionView),new PropertyMetadata(null,OnStateChanged));
    public static readonly DependencyProperty ReducedMotionProperty=DependencyProperty.Register(nameof(ReducedMotion),typeof(bool),typeof(AssistantActionView),new PropertyMetadata(false,OnStateChanged));
    public AssistantActionFeedback? Feedback {get=>(AssistantActionFeedback?)GetValue(FeedbackProperty);set=>SetValue(FeedbackProperty,value);}
    public bool ReducedMotion {get=>(bool)GetValue(ReducedMotionProperty);set=>SetValue(ReducedMotionProperty,value);}
    private long _lastSequence=-1; private AssistantActionPhase? _lastPhase;
    internal bool HasWorkingMotion {get;private set;}
    internal double DisplayedLevel=>LevelScale.ScaleX;
    internal string DisplayedTitle=>ActionTitle.Text;
    public AssistantActionView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>UpdateState();
        IsVisibleChanged+=(_,_)=>{if(IsVisible)UpdateState();else StopMotion();};
        Unloaded+=(_,_)=>StopMotion();
    }
    private static void OnStateChanged(DependencyObject owner,DependencyPropertyChangedEventArgs e)
    {var view=(AssistantActionView)owner;if(view.Card is not null)view.UpdateState();}
    private static SolidColorBrush Brush(string color){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));brush.Freeze();return brush;}
    private void StopMotion()
    {
        HasWorkingMotion=false;
        OrbitAngle.BeginAnimation(RotateTransform.AngleProperty,null);
        IconScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);IconScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        Card.BeginAnimation(OpacityProperty,null);CardOffset.BeginAnimation(TranslateTransform.YProperty,null);
        LevelScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);SuccessTick.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,null);
    }
    private void UpdateState()
    {
        StopMotion();
        Card.Visibility=Feedback is null?Visibility.Collapsed:Visibility.Visible;
        if(Feedback is not { } feedback){ActionTitle.Text="";ActionDetail.Text="";return;}
        var animate=IsVisible && !ReducedMotion;
        var newPhase=feedback.Sequence!=_lastSequence || feedback.Phase!=_lastPhase;
        var working=feedback.Phase==AssistantActionPhase.Working;
        var succeeded=feedback.Phase==AssistantActionPhase.Succeeded;
        var failed=feedback.Phase==AssistantActionPhase.Failed;
        var accent=failed?"#FFE6B68B":succeeded?"#FF88DBBA":working?"#FFB9A5FD":"#FF94A3B7";
        Card.Background=Brush(failed?"#FF2B2524":succeeded?"#B01F2527":"#B01D1F25");
        Card.BorderBrush=Brush(failed?"#FF674D3A":succeeded?"#FF315650":"#FF3B3745");
        ActionIcon.Text=Glyph(feedback.Command);ActionIcon.Foreground=Brush(accent);
        IconWell.Fill=ResultWell.Fill=Brush(failed?"#FF433329":succeeded?"#FF24443D":"#FF362F42");
        Orbit.Stroke=Brush(accent);Orbit.Visibility=working?Visibility.Visible:Visibility.Collapsed;
        SuccessTick.Visibility=succeeded?Visibility.Visible:Visibility.Collapsed;
        SuccessTick.StrokeDashOffset=0;
        ResultIcon.Visibility=succeeded?Visibility.Collapsed:Visibility.Visible;
        ResultIcon.Text=working?"···":failed?"!":"−";ResultIcon.Foreground=Brush(accent);
        ActionTitle.Text=feedback.Phase switch {AssistantActionPhase.Working=>WorkingTitle(feedback.Command),AssistantActionPhase.Succeeded=>SuccessTitle(feedback.Command),AssistantActionPhase.Failed=>"Couldn't complete action",_=>"Action stopped"};
        ActionDetail.Text=working?"Applying island control…":failed?"See the details below":feedback.Phase==AssistantActionPhase.Cancelled?"Stopped before confirmation":
            feedback.Command.Equals("Volume",StringComparison.OrdinalIgnoreCase) && feedback.Level is double percent?$"{percent:0}% · System output":"Confirmed by island controls";
        System.Windows.Automation.AutomationProperties.SetName(Card,$"{ActionTitle.Text}. {feedback.Message??ActionDetail.Text}");
        Card.Opacity=1;CardOffset.Y=0;IconScale.ScaleX=IconScale.ScaleY=1;OrbitAngle.Angle=0;
        var hasLevel=succeeded && feedback.Command.Equals("Volume",StringComparison.OrdinalIgnoreCase) && feedback.Level is double value && double.IsFinite(value);
        LevelTrack.Visibility=hasLevel?Visibility.Visible:Visibility.Collapsed;
        var level=hasLevel?Math.Clamp(feedback.Level!.Value/100d,0,1):0;
        LevelScale.ScaleX=level;
        if(animate && newPhase)
        {
            Card.BeginAnimation(OpacityProperty,Motion(.5,1,180));CardOffset.BeginAnimation(TranslateTransform.YProperty,Motion(5,0,220));
            IconScale.BeginAnimation(ScaleTransform.ScaleXProperty,Motion(.85,1,260));IconScale.BeginAnimation(ScaleTransform.ScaleYProperty,Motion(.85,1,260));
            if(succeeded)SuccessTick.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,Motion(20,0,300));
            if(hasLevel)LevelScale.BeginAnimation(ScaleTransform.ScaleXProperty,Motion(0,level,360));
        }
        if(animate && working)
        {
            HasWorkingMotion=true;
            OrbitAngle.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(1200)){RepeatBehavior=RepeatBehavior.Forever});
        }
        if(IsVisible){_lastSequence=feedback.Sequence;_lastPhase=feedback.Phase;}
    }
    private static DoubleAnimation Motion(double from,double to,int milliseconds)=>new(from,to,TimeSpan.FromMilliseconds(milliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
    private static string Glyph(string command)=>command.ToLowerInvariant() switch
    {
        "volume" or "unmute"=>"\uE767","mute"=>"\uE74F","audiooutput" or "setoutput" or "listoutputs"=>"\uE7F5",
        "play" or "pause" or "next" or "previous" or "seek" or "openmedia"=>"\uE8D6",
        "timer" or "pausetimer" or "resumetimer" or "canceltimer" or "stopwatch" or "startstopwatch" or "pausestopwatch" or "resumestopwatch" or "resetstopwatch" or "lapstopwatch"=>"\uE916",
        "alarm" or "cancelalarm" or "dismissalarm" or "snoozealarm"=>"\uE707",
        "focuson" or "focusoff" or "focussession" or "endfocus"=>"\uE8A8",
        "clipboard"=>"\uE8C8","notificationson" or "notificationsoff" or "notificationhistory" or "dismissbanner" or "clearnotificationhistory"=>"\uE7F4",_=>"\uE713"
    };
    private static string WorkingTitle(string command)=>command.ToLowerInvariant() switch
    {
        "volume"=>"Adjusting volume","mute" or "unmute"=>"Updating audio","setoutput"=>"Switching audio output",
        "play" or "pause" or "next" or "previous" or "seek"=>"Controlling playback","timer"=>"Starting timer","alarm"=>"Setting alarm",
        "setsetting"=>"Updating island setting","focussession" or "focuson" or "focusoff" or "endfocus"=>"Updating focus",_=>"Using island controls"
    };
    private static string SuccessTitle(string command)=>command.ToLowerInvariant() switch
    {
        "volume"=>"Volume updated","mute"=>"Audio muted","unmute"=>"Audio unmuted","setoutput"=>"Audio output switched",
        "timer"=>"Timer started","alarm"=>"Alarm set","setsetting"=>"Island setting updated","status" or "getsetting" or "listoutputs"=>"Status checked",_=>"Island action complete"
    };
}
