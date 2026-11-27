using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

internal static class NativeBackupUpgradeDialog
{
    internal static string? RequestPassword(Window owner,NativeBackupUpgradeIntent intent)
    {
        if(!intent.IsValid) return null;
        var window=new Window
        {
            Owner=owner, Title="ترقية نسخة احتياطية مشفرة",
            Width=520, SizeToContent=SizeToContent.Height, ResizeMode=ResizeMode.NoResize,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ShowInTaskbar=false, FlowDirection=FlowDirection.RightToLeft
        };
        var panel=new StackPanel { Margin=new Thickness(22) };
        panel.Children.Add(new TextBlock {
            Text="سيتم إنشاء نسخة مشفرة جديدة متوافقة مع الإصدار الحالي دون استعادة البيانات أو تعديل النسخة الأصلية.",
            FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
        });
        panel.Children.Add(new TextBlock {
            Text="الملف: "+intent.Evidence.Path+"\nالإصدار: "+intent.Evidence.SchemaVersion+
                " ← "+intent.Evidence.CurrentSchemaVersion+"\nSHA-256: "+intent.Evidence.Sha256,
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
        });
        panel.Children.Add(new TextBlock { Text="كلمة مرور الإدارة العامة الحالية" });
        var password=new PasswordBox { MaxLength=1024,Margin=new Thickness(0,6,0,8) };
        panel.Children.Add(password);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left };
        var confirm=new Button { Content="إنشاء نسخة مترقاة",IsEnabled=false,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0) };
        var cancel=new Button { Content="إلغاء",IsCancel=true,Padding=new Thickness(12,6,12,6) };
        password.PasswordChanged+=(_,_)=>confirm.IsEnabled=password.Password.Length>0;
        confirm.Click+=(_,_)=>{if(password.Password.Length>0) window.DialogResult=true;};
        actions.Children.Add(confirm);actions.Children.Add(cancel);panel.Children.Add(actions);
        window.Content=panel;window.Loaded+=(_,_)=>password.Focus();
        try{return window.ShowDialog()==true?password.Password:null;}
        finally{password.Clear();}
    }
}
