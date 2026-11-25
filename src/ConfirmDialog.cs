using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
namespace ZaloManagerPoc
{
    public class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string prefix, string target, string note, string confirmText, bool danger)
            : this(title, prefix, target, note, confirmText, danger, false) { }

        private ConfirmDialog(string title, string prefix, string target, string note, string confirmText, bool danger, bool recallDeletion)
        {
            Title=title; Width=recallDeletion ? 574 : 460; SizeToContent=SizeToContent.Height;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
            ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; WindowStartupLocation=WindowStartupLocation.CenterOwner; FontFamily=new FontFamily("Segoe UI");
            Grid design;
            string resource = recallDeletion ? "ZaloManager.RecallDeleteConfirmation.xaml" : "ZaloManager.ConfirmDialog.xaml";
            using(var stream=typeof(ConfirmDialog).Assembly.GetManifestResourceStream(resource))
                design=(Grid)XamlReader.Load(stream);
            Content=design;
            ((TextBlock)design.FindName("TxtTitle")).Text=title;
            if (recallDeletion) ((Run)design.FindName("TxtMessagePrefix")).Text = prefix;
            else
            {
                ((TextBlock)design.FindName("TxtMessagePrefix")).Text = prefix;
                ((TextBlock)design.FindName("TxtMessageSuffix")).Text = "?";
            }
            ((TextBlock)design.FindName("TxtTargetName")).Text=target;
            if(String.IsNullOrWhiteSpace(target)) ((Border)design.FindName("TargetPill")).Visibility=Visibility.Collapsed;
            ((TextBlock)design.FindName("TxtNote")).Text=note;
            if(String.IsNullOrWhiteSpace(note)) ((Border)design.FindName("NoteBox")).Visibility=Visibility.Collapsed;
            var confirm=(Button)design.FindName("BtnConfirm");confirm.Content=confirmText;
            confirm.Style=(Style)design.Resources[danger?"DangerConfirmButton":"PrimaryConfirmButton"];
            confirm.Click+=delegate{DialogResult=true;};
            ((Button)design.FindName("BtnCancel")).Click+=delegate{DialogResult=false;};
            var header = recallDeletion ? (Grid)design.FindName("DialogHeader") : (Grid)((Grid)((Border)design.Children[0]).Child).Children[0];
            var close = recallDeletion ? (Button)design.FindName("BtnClose") : (Button)header.Children[2];
            close.Click+=delegate{DialogResult=false;};
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

        internal static ConfirmDialog CreateManagerExit(Window owner)
        {
            var dialog = new ConfirmDialog("Thoát Zalo Manager", "Bạn muốn thoát", "Zalo Manager",
                "Manager sẽ lưu dữ liệu và kết thúc các tác vụ nền. Theo dõi tin nhắn và lưu tin thu hồi sẽ tạm dừng đến khi bạn mở lại Manager. Các cửa sổ Zalo đang chạy vẫn hoạt động.",
                "Thoát hoàn toàn", true);
            if (owner != null && owner.IsVisible) dialog.Owner = owner;
            else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return dialog;
        }

        internal static bool AskRecallDeletion(Window owner, string profileName, string conversationName)
        {
            bool wholeProfile = conversationName == null;
            string prefix = wholeProfile ? "Xóa toàn bộ tin thu hồi của " : "Xóa toàn bộ tin thu hồi trong cuộc trò chuyện với ";
            string target = wholeProfile ? profileName : conversationName;
            string note = wholeProfile
                ? "Các tin đã đọc và chưa đọc của tài khoản này sẽ bị xóa khỏi máy."
                : "Các tin đã đọc và chưa đọc trong cuộc trò chuyện này sẽ bị xóa khỏi máy.";
            return new ConfirmDialog("Xóa tất cả tin thu hồi", prefix, target, note, "Xóa tất cả", true, true)
                { Owner = owner }.ShowDialog() == true;
        }
    }
}
