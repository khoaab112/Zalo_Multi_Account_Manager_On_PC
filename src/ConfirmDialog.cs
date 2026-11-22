using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
namespace ZaloManagerPoc
{
    public class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string prefix, string target, string note, string confirmText, bool danger)
        {
            Title=title; Width=460; SizeToContent=SizeToContent.Height;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
            ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; WindowStartupLocation=WindowStartupLocation.CenterOwner; FontFamily=new FontFamily("Segoe UI");
            Grid design;
            using(var stream=typeof(ConfirmDialog).Assembly.GetManifestResourceStream("ZaloManager.ConfirmDialog.xaml"))
                design=(Grid)XamlReader.Load(stream);
            Content=design;
            ((TextBlock)design.FindName("TxtTitle")).Text=title;
            ((TextBlock)design.FindName("TxtMessagePrefix")).Text=prefix;
            ((TextBlock)design.FindName("TxtMessageSuffix")).Text="?";
            ((TextBlock)design.FindName("TxtTargetName")).Text=target;
            if(String.IsNullOrWhiteSpace(target)) ((Border)design.FindName("TargetPill")).Visibility=Visibility.Collapsed;
            ((TextBlock)design.FindName("TxtNote")).Text=note;
            if(String.IsNullOrWhiteSpace(note)) ((Border)design.FindName("NoteBox")).Visibility=Visibility.Collapsed;
            var confirm=(Button)design.FindName("BtnConfirm");confirm.Content=confirmText;
            confirm.Style=(Style)design.Resources[danger?"DangerConfirmButton":"PrimaryConfirmButton"];
            confirm.Click+=delegate{DialogResult=true;};
            ((Button)design.FindName("BtnCancel")).Click+=delegate{DialogResult=false;};
            var card=(Border)design.Children[0];var layout=(Grid)card.Child;var header=(Grid)layout.Children[0];
            ((Button)header.Children[2]).Click+=delegate{DialogResult=false;};
            header.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
                if(e.ChangedButton!=MouseButton.Left)return;
                DependencyObject child=e.OriginalSource as DependencyObject;
                while(child!=null && child!=header){if(child is Button)return;child=VisualTreeHelper.GetParent(child);}
                DragMove();
            };
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){DialogResult=false;e.Handled=true;}};
            if(!danger){
                var badge=(Border)design.FindName("IconBadge");badge.Background=(Brush)new BrushConverter().ConvertFromString("#EFF6FF");badge.BorderBrush=(Brush)new BrushConverter().ConvertFromString("#BFDBFE");
                ((System.Windows.Shapes.Path)design.FindName("IconPath")).Fill=(Brush)new BrushConverter().ConvertFromString("#0068FF");
            }
        }
        public static bool Ask(Window owner,string title,string prefix,string target,string note,string confirmText,bool danger=true)
        {return new ConfirmDialog(title,prefix,target,note,confirmText,danger){Owner=owner}.ShowDialog()==true;}
    }
}