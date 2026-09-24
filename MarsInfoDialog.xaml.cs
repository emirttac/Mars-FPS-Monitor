using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FPSOverlay
{
    /// <summary>In-app themed info dialog with a dimmed owner backdrop.</summary>
    public partial class MarsInfoDialog : Window
    {
        public MarsInfoDialog(
            string title,
            string subtitle,
            string body,
            string comingTitle,
            string comingBody,
            string okText)
        {
            InitializeComponent();
            LblTitle.Text = title ?? "";
            LblSubtitle.Text = subtitle ?? "";
            TxtBody.Text = body ?? "";
            LblComingTitle.Text = comingTitle ?? "";
            TxtComingBody.Text = comingBody ?? "";
            BtnOk.Content = string.IsNullOrWhiteSpace(okText) ? "OK" : okText;

            Loaded += OnLoaded;
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    CloseAnimated();
                    e.Handled = true;
                }
            };
            DimBackdrop.MouseLeftButtonDown += (_, _) => CloseAnimated();
        }

        public static void ShowInfo(
            Window? owner,
            string title,
            string subtitle,
            string body,
            string comingTitle,
            string comingBody,
            string okText)
        {
            var dlg = new MarsInfoDialog(title, subtitle, body, comingTitle, comingBody, okText);
            AlignToOwner(dlg, owner);
            dlg.ShowDialog();
        }

        private static void AlignToOwner(MarsInfoDialog dlg, Window? owner)
        {
            if (owner != null && owner.IsVisible)
            {
                dlg.Owner = owner;
                // Cover the owner exactly so the dim veil darkens Mars, not the whole desktop.
                dlg.Left = owner.Left;
                dlg.Top = owner.Top;
                dlg.Width = Math.Max(320, owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width);
                dlg.Height = Math.Max(240, owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height);
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dlg.Width = 520;
                dlg.Height = 420;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            DimBackdrop.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            CardRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e) => CloseAnimated();

        private bool _closing;

        private void CloseAnimated()
        {
            if (_closing) return;
            _closing = true;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
            fadeOut.Completed += (_, _) =>
            {
                try { DialogResult = true; }
                catch { Close(); }
            };
            DimBackdrop.BeginAnimation(OpacityProperty, fadeOut);
            CardRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
        }
    }
}
