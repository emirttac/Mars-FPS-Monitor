using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace FPSOverlay
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            CaptureAndBlurDesktop();
        }

        public void ApplyStrings(UiStrings s)
        {
            TxtSplashTitle.Text = s.SplashTitle;
            TxtSplashSub.Text = s.SplashSubtitle;
            TxtSplashStatus.Text = s.SplashBoot;
        }

        public void SetStatus(string text)
        {
            Dispatcher.Invoke(() => TxtSplashStatus.Text = text);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var scale = new ScaleTransform(0.85, 0.85);
            var rotate = new RotateTransform(0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(rotate);
            MarsGlobe.RenderTransform = group;
            MarsGlobe.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }
            });
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(8))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });

            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void CaptureAndBlurDesktop()
        {
            try
            {
                int w = (int)SystemParameters.PrimaryScreenWidth;
                int h = (int)SystemParameters.PrimaryScreenHeight;
                using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
                }

                int tw = Math.Max(320, w / 4);
                int th = Math.Max(180, h / 4);
                using var small = new Bitmap(bmp, tw, th);
                ImgBlurBg.Source = BitmapToImageSource(small);
            }
            catch
            {
                ImgBlurBg.Source = null;
            }
        }

        private static BitmapSource BitmapToImageSource(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int size = data.Stride * data.Height;
                byte[] bytes = new byte[size];
                Marshal.Copy(data.Scan0, bytes, 0, size);
                var bs = BitmapSource.Create(
                    bitmap.Width, bitmap.Height, 96, 96,
                    PixelFormats.Bgra32, null, bytes, data.Stride);
                bs.Freeze();
                return bs;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
