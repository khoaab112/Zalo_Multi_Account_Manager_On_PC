using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZaloManagerPoc
{
    public partial class ManagerWindow
    {
        private Action stopRecallAudio;

        private void AddRecallAudioPlayer(Border tile, StackPanel details, CdpMessageWatcher.RecalledMessage message)
        {
            var bars = new UniformGrid { Rows = 1, Height = 25, Width = 190, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4), IsHitTestVisible = false };
            var idleBrush = ModernBrush("#AEBFD1");
            var playingBrush = ModernBrush("#0068FF");
            foreach (int height in new[] { 6, 10, 17, 11, 21, 13, 24, 15, 9, 19, 23, 14, 8, 17, 12, 7, 11, 5 })
                bars.Children.Add(new Border { Width = 2, Height = height, Background = idleBrush, CornerRadius = new CornerRadius(1),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var time = new TextBlock { Text = "Tin nhắn thoại", FontWeight = FontWeights.SemiBold, FontSize = 12,
                Foreground = ModernBrush("#475569"), TextWrapping = TextWrapping.Wrap };
            details.Children.Add(bars);
            details.Children.Add(time);

            var button = RecallIconButton("Nghe", "M 4,2 L 14,8 L 4,14 Z", "#0068FF", "#FFFFFF", 40, null, true);
            var playIcon = button.Content;
            var pauseIcon = RecallIcon("M 4,2 H 7 V 14 H 4 Z M 10,2 H 13 V 14 H 10 Z", Brushes.White, true);
            tile.Child = button;

            MediaPlayer player = null;
            string path = null;
            bool ready = false;
            bool playing = false;
            TimeSpan total = TimeSpan.Zero;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            Action updateButton = delegate
            {
                button.Content = playing ? pauseIcon : playIcon;
                string label = playing ? "Tạm dừng ghi âm" : "Nghe";
                button.ToolTip = label;
                AutomationProperties.SetName(button, label);
            };
            Action<TimeSpan> updateProgress = delegate(TimeSpan elapsed)
            {
                if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
                if (total > TimeSpan.Zero && elapsed > total) elapsed = total;
                int filled = total > TimeSpan.Zero ? (int)(elapsed.TotalSeconds / total.TotalSeconds * bars.Children.Count) : 0;
                for (int index = 0; index < bars.Children.Count; index++)
                    ((Border)bars.Children[index]).Background = index < filled ? playingBrush : idleBrush;
                time.Text = PlaybackTime(elapsed) + " / " + (total > TimeSpan.Zero ? PlaybackTime(total) : "--:--");
            };
            Action release = delegate
            {
                timer.Stop();
                var closing = player;
                player = null;
                if (closing != null) closing.Close();
                DeletePlaybackFile(path);
                path = null;
                playing = ready = false;
                updateButton();
            };
            Action stop = null;
            stop = delegate
            {
                release();
                total = TimeSpan.Zero;
                updateProgress(TimeSpan.Zero);
                time.Text = "Tin nhắn thoại";
                if (stopRecallAudio == stop) stopRecallAudio = null;
            };
            Action fail = delegate
            {
                stop();
                time.Text = "Không phát được ghi âm. Bạn có thể tải tệp về.";
            };
            timer.Tick += delegate { if (player != null) updateProgress(player.Position); };
            tile.Unloaded += delegate { if (player != null) stop(); };
            button.Click += delegate
            {
                if (player != null)
                {
                    if (!ready) { stop(); return; }
                    playing = !playing;
                    if (playing) { player.Play(); timer.Start(); }
                    else { player.Pause(); timer.Stop(); updateProgress(player.Position); }
                    updateButton();
                    return;
                }

                if (stopRecallAudio != null) stopRecallAudio();
                stopRecallAudio = stop;
                try
                {
                    path = WritePlaybackFile(message);
                    var opening = new MediaPlayer();
                    player = opening;
                    total = TimeSpan.Zero;
                    updateProgress(TimeSpan.Zero);
                    time.Text = "Đang mở ghi âm…";
                    opening.MediaOpened += delegate
                    {
                        if (player != opening) return;
                        total = opening.NaturalDuration.HasTimeSpan ? opening.NaturalDuration.TimeSpan : TimeSpan.Zero;
                        ready = playing = true;
                        updateProgress(TimeSpan.Zero);
                        updateButton();
                        opening.Play();
                        timer.Start();
                    };
                    opening.MediaEnded += delegate
                    {
                        if (player != opening) return;
                        updateProgress(total > TimeSpan.Zero ? total : opening.Position);
                        release();
                        if (stopRecallAudio == stop) stopRecallAudio = null;
                    };
                    opening.MediaFailed += delegate { if (player == opening) fail(); };
                    opening.Open(new Uri(path));
                }
                catch (Exception error)
                {
                    if (!IsPlaybackFileError(error)) throw;
                    fail();
                }
            };
        }

        private static string PlaybackTime(TimeSpan time)
        {
            return time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        }

        private static string WritePlaybackFile(CdpMessageWatcher.RecalledMessage message)
        {
            if (message.Attachment != null && !String.IsNullOrEmpty(message.Attachment.Data)
                && message.Attachment.Data.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            {
                string localPath = message.Attachment.Data.Substring("local:".Length);
                if (File.Exists(localPath)) return localPath;
            }
            string extension = Path.GetExtension(RecallAttachmentName(message));
            string path = Path.Combine(Path.GetTempPath(), "zalo-recall-" + Guid.NewGuid().ToString("N") + extension);
            try { File.WriteAllBytes(path, Convert.FromBase64String(message.Attachment.Data)); }
            catch { DeletePlaybackFile(path); throw; }
            return path;
        }

        private static void DeletePlaybackFile(string path)
        {
            if (path == null) return;
            if (!path.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool IsPlaybackFileError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is FormatException;
        }
    }
}
