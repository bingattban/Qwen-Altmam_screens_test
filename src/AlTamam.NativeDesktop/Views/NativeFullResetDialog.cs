using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

internal static class NativeFullResetDialog
{
    internal sealed record Approval(NativeFullResetConsent Consent,string Password,TimeSpan Elapsed);

    internal static Approval? Confirm(Window owner,NativeFullResetPreview review)
    {
        var dialog=new Window
        {
            Owner=owner,Title="تصفير جميع البيانات — الإدارة العامة",
            Width=620,Height=650,MinHeight=480,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ShowInTaskbar=false,FlowDirection=FlowDirection.RightToLeft
        };
        var panel=new StackPanel { Margin=new Thickness(20) };
        dialog.Content=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock
        {
            Text="تحذير: سيتم حذف البيانات والحسابات وسجل العمليات والنسخ المسجلة المطابقة لبصماتها. "+
                "ينشئ البرنامج نسخة إنقاذ مشفرة داخلية قبل الحذف، ويتوقف بعدها لتبدأ التهيئة الأولى عند فتحه مجددا.",
            Foreground=owner.TryFindResource("NativeDangerText") as Brush ?? Brushes.DarkRed,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,12)
        });
        panel.Children.Add(new TextBlock
        {
            Text=$"الأفراد: {review.People}   |   التحضير: {review.Attendance}\n"+
                $"الحسابات: {review.Users}   |   سجل العمليات: {review.AuditEvents}\n"+
                $"الحركات: {review.Movements}   |   التعيينات: {review.Assignments}\n"+
                $"قيود النسخ الاحتياطية المسجلة: {review.BackupEntries}",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
        });
        if(review.BackupFileNames.Count>0)
            panel.Children.Add(new TextBlock
            {
                Text="النسخ المسجلة (حتى 30):\n"+string.Join("\n",review.BackupFileNames)+
                    (review.BackupFileNamesTruncated?"\nتوجد قيود إضافية.":""),
                TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
            });
        var live=new CheckBox { Content="أوافق على حذف جميع البيانات والحسابات وسجل العمليات",IsChecked=false,
            Margin=new Thickness(0,8,0,8) };
        var backups=new CheckBox { Content="أوافق على حذف النسخ المسجلة المملوكة المطابقة للبصمة",IsChecked=false,
            Margin=new Thickness(0,0,0,8) };
        panel.Children.Add(live); panel.Children.Add(backups);
        panel.Children.Add(new TextBlock { Text="اكتب حرفيا: "+NativeFullResetPreview.RequiredPhrase });
        var phrase=new TextBox { MaxLength=64,Margin=new Thickness(0,5,0,10) };
        panel.Children.Add(phrase);
        panel.Children.Add(new TextBlock { Text="كلمة مرور حساب الإدارة العامة الحالي:" });
        var password=new PasswordBox { MaxLength=1024,Margin=new Thickness(0,5,0,10) };
        panel.Children.Add(password);
        var countdown=new TextBlock { Margin=new Thickness(0,0,0,10) };
        panel.Children.Add(countdown);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal };
        var cancel=new Button { Content="إلغاء",IsCancel=true,IsDefault=true,Padding=new Thickness(18,8,18,8) };
        var approve=new Button { Content="تأكيد التصفير النهائي",IsEnabled=false,
            Foreground=owner.TryFindResource("NativeDangerText") as Brush ?? Brushes.DarkRed,Padding=new Thickness(18,8,18,8),Margin=new Thickness(8,0,0,0) };
        buttons.Children.Add(cancel); buttons.Children.Add(approve); panel.Children.Add(buttons);
        var elapsed=new Stopwatch();
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(150) };
        NativeFullResetConsent Consent()=>new(phrase.Text,live.IsChecked==true,backups.IsChecked==true);
        void Refresh()
        {
            int left=Math.Max(0,(int)Math.Ceiling(NativeFullResetPreview.CountdownSeconds-elapsed.Elapsed.TotalSeconds));
            countdown.Text=left>0?$"مهلة التأكيد: {left} ثانية":"يمكن التأكيد بعد استيفاء جميع الشروط.";
            approve.IsEnabled=review.CanCommit(review.Actor,Consent(),elapsed.Elapsed) && password.Password.Length>0;
        }
        Approval? result=null;
        approve.Click+=(_,_)=>
        {
            if(!review.CanCommit(review.Actor,Consent(),elapsed.Elapsed) || password.Password.Length==0) return;
            result=new Approval(Consent(),password.Password,elapsed.Elapsed);
            dialog.DialogResult=true;
        };
        live.Checked+=(_,_)=>Refresh(); live.Unchecked+=(_,_)=>Refresh();
        backups.Checked+=(_,_)=>Refresh(); backups.Unchecked+=(_,_)=>Refresh();
        phrase.TextChanged+=(_,_)=>Refresh(); password.PasswordChanged+=(_,_)=>Refresh();
        timer.Tick+=(_,_)=>Refresh();
        dialog.Loaded+=(_,_)=>{ elapsed.Start(); timer.Start(); Refresh(); cancel.Focus(); };
        try { return dialog.ShowDialog()==true?result:null; }
        finally { timer.Stop(); elapsed.Stop(); password.Clear(); }
    }
}
