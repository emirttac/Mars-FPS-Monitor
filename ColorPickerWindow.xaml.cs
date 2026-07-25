using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;

namespace FPSOverlay
{
    public partial class ColorPickerWindow : Window
    {
        public string SelectedColorHex { get; private set; } = "#FFFFFF";
        private OverlayConfig _config;

        private double _currentHue = 0; // 0-360
        private double _currentSaturation = 1; // 0-1
        private double _currentValue = 1; // 0-1

        private bool _isUpdatingFromText = false;

        private WriteableBitmap _hueBitmap;
        private WriteableBitmap _svBitmap;

        public ColorPickerWindow(OverlayConfig config)
        {
            InitializeComponent();
            _config = config;

            ApplyLocalization(_config.Language);

            this.MouseLeftButtonDown += (s, e) =>
            {
                // DON'T DragMove the window mid color pick 😤
                if (!_isSelectingHue && !_isSelectingSatVal)
                    this.DragMove();
            };

            _hueBitmap = new WriteableBitmap(200, 200, 96, 96, PixelFormats.Bgra32, null);
            ImgHueRing.Source = _hueBitmap;
            
            _svBitmap = new WriteableBitmap(110, 110, 96, 96, PixelFormats.Bgra32, null);
            ImgSvSquare.Source = _svBitmap;

            DrawHueRing();
            
            SetColorFromHex(_config.TextColorHex);
            
            this.Loaded += ColorPickerWindow_Loaded;
        }

        private void ColorPickerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateCustomColors();
        }

        private void DrawHueRing()
        {
            int width = _hueBitmap.PixelWidth;
            int height = _hueBitmap.PixelHeight;
            int cx = width / 2;
            int cy = height / 2;
            int outerRadius = 100;
            int innerRadius = 75;

            byte[] pixels = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double dx = x - cx;
                    double dy = y - cy;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance >= innerRadius && distance <= outerRadius)
                    {
                        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                        if (angle < 0) angle += 360;

                        // Atan2: 0=right, 90=bottom — WPF angles are spicy
                        // rotate so 0 is TOP like the indicator
                        double hue = (angle + 90) % 360; 

                        Color c = ColorFromHsv(hue, 1, 1);
                        int idx = (y * width + x) * 4;
                        pixels[idx] = c.B;
                        pixels[idx + 1] = c.G;
                        pixels[idx + 2] = c.R;
                        pixels[idx + 3] = 255;
                    }
                }
            }

            _hueBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        }

        private void DrawSvSquare()
        {
            int width = _svBitmap.PixelWidth;
            int height = _svBitmap.PixelHeight;
            byte[] pixels = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double s = (double)x / (width - 1);
                    double v = 1.0 - (double)y / (height - 1);

                    Color c = ColorFromHsv(_currentHue, s, v);
                    int idx = (y * width + x) * 4;
                    pixels[idx] = c.B;
                    pixels[idx + 1] = c.G;
                    pixels[idx + 2] = c.R;
                    pixels[idx + 3] = 255;
                }
            }
            _svBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        }

        private void UpdateIndicatorsAndText()
        {
            // park hue indicator with trig on the ring mid-radius
            // Hue 0 = Red = TOP (that's -90 in normal angles)
            double rad = (_currentHue - 90.0) * (Math.PI / 180.0);
            double hueX = GridCenter + HueRingMidRadius * Math.Cos(rad);
            double hueY = GridCenter + HueRingMidRadius * Math.Sin(rad);

            Canvas.SetLeft(HueIndicator, hueX);
            Canvas.SetTop(HueIndicator, hueY);

            // move the SV picker dot
            double svX = _currentSaturation * 110;
            double svY = (1.0 - _currentValue) * 110;
            
            // nudge for indicator size + canvas offset
            Canvas.SetLeft(SvIndicator, 45 + svX); // (200-110)/2 = 45
            Canvas.SetTop(SvIndicator, 45 + svY);

            // refresh that color preview square
            Color c = ColorFromHsv(_currentHue, _currentSaturation, _currentValue);
            SelectedColorHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            ColorPreview.Background = new SolidColorBrush(c);

            _isUpdatingFromText = true;
            TxtR.Text = c.R.ToString();
            TxtG.Text = c.G.ToString();
            TxtB.Text = c.B.ToString();
            _isUpdatingFromText = false;
        }

        private void SetColorFromHex(string hex)
        {
            try
            {
                Color c = (Color)ColorConverter.ConvertFromString(hex);
                HsvFromColor(c, out _currentHue, out _currentSaturation, out _currentValue);
                DrawSvSquare();
                UpdateIndicatorsAndText();
            }
            catch { }
        }

        private void RgbTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromText) return;

            if (byte.TryParse(TxtR.Text, out byte r) && 
                byte.TryParse(TxtG.Text, out byte g) && 
                byte.TryParse(TxtB.Text, out byte b))
            {
                Color c = Color.FromRgb(r, g, b);
                HsvFromColor(c, out _currentHue, out _currentSaturation, out _currentValue);
                DrawSvSquare();
                UpdateIndicatorsAndText();
            }
        }

        #region Mouse Events

        // grid 200x200, center (100,100)
        // hue ring outer=100 inner=75 mid=87.5
        // SV square 110x110 → offset 45 from edge

        private const double GridCenter = 100.0;
        private const double HueInnerRadius = 75.0;
        private const double HueRingMidRadius = 87.5; // (75 + 100) / 2 — indicator sits here
        private const double SvOffset = 45.0;   // (200 - 110) / 2
        private const double SvSize = 110.0;

        // lock mode ONCE on MouseDown — never flip mid-drag
        private bool _isSelectingHue = false;
        private bool _isSelectingSatVal = false;

        private void PickerGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // reset both locks
            _isSelectingHue = false;
            _isSelectingSatVal = false;

            Point p = e.GetPosition(PickerGrid);
            double dx = p.X - GridCenter;
            double dy = p.Y - GridCenter;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // click decides hue vs SV — FINAL for whole drag
            if (distance >= HueInnerRadius)
            {
                _isSelectingHue = true;
            }
            else
            {
                _isSelectingSatVal = true;
            }

            PickerGrid.CaptureMouse();

            // stop bubble or Window DragMove steals our session
            e.Handled = true;

            // first click = pure math, no pixel sniffing
            if (_isSelectingHue)
                ProcessHueInput(p);
            else
                ProcessSvInput(p);
        }

        private void PickerGrid_MouseMove(object sender, MouseEventArgs e)
        {
            // no drag? ignore mouse noise
            if (!_isSelectingHue && !_isSelectingSatVal) return;

            Point p = e.GetPosition(PickerGrid);

            // route by LOCKED state — never re-measure distance mid-drag
            if (_isSelectingHue)
                ProcessHueInput(p);
            else if (_isSelectingSatVal)
                ProcessSvInput(p);
        }

        private void PickerGrid_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isSelectingHue = false;
            _isSelectingSatVal = false;
            PickerGrid.ReleaseMouseCapture();
        }

        /// <summary>
        /// Pure math Hue — NO pixel sniffing.
        /// Atan2 gives the angle even if mouse is way off the ring center.
        /// </summary>
        private void ProcessHueInput(Point p)
        {
            double dx = p.X - GridCenter;
            double dy = p.Y - GridCenter;

            // Atan2 → rad → deg 0..360
            double angleRad = Math.Atan2(dy, dx);
            double angleDeg = angleRad * (180.0 / Math.PI);
            if (angleDeg < 0) angleDeg += 360.0;

            // +90 so top of ring = Hue 0 (Red)
            _currentHue = (angleDeg + 90.0) % 360.0;

            // redraw SV for new hue + refresh indicators/text
            DrawSvSquare();
            UpdateIndicatorsAndText();
        }

        /// <summary>
        /// Pure math S/V — coordinate proportion, NO pixel sniffing.
        /// </summary>
        private void ProcessSvInput(Point p)
        {
            // PickerGrid → SV local (square starts at SvOffset)
            double localX = p.X - SvOffset;
            double localY = p.Y - SvOffset;

            // clamp into [0, SvSize]
            localX = Math.Max(0, Math.Min(SvSize, localX));
            localY = Math.Max(0, Math.Min(SvSize, localY));

            // proportion → 0..1 easy
            _currentSaturation = localX / SvSize;
            _currentValue = 1.0 - (localY / SvSize);

            UpdateIndicatorsAndText();
        }

        #endregion

        #region Standard & Custom Colors

        private void StandardColor_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string hex)
            {
                SetColorFromHex(hex);
            }
        }

        private void CustomColorSlot_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border slot && slot.Background is SolidColorBrush brush)
            {
                // empty slot? noop
                if (brush.Color.A == 0) return;

                // yeet hex from the clicked custom slot bg
                string hex = $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
                SetColorFromHex(hex);
            }
        }

        private void PopulateCustomColors()
        {
            CustomColorContainer.Children.Clear();
            for (int i = 0; i < 7; i++)
            {
                Border slot = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 0, 8, 0),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };

                if (i < _config.CustomColors.Count)
                {
                    try
                    {
                        Color c = (Color)ColorConverter.ConvertFromString(_config.CustomColors[i]);
                        slot.Background = new SolidColorBrush(c);
                        slot.Tag = _config.CustomColors[i];
                        slot.MouseLeftButtonDown += CustomColorSlot_Click;
                    }
                    catch { }
                }
                else
                {
                    slot.Background = Brushes.Transparent;
                    slot.MouseLeftButtonDown += CustomColorSlot_Click; // wired up — empty slots early-return anyway
                }

                CustomColorContainer.Children.Add(slot);
            }
        }

        private void BtnAddCustom_Click(object sender, RoutedEventArgs e)
        {
            if (!_config.CustomColors.Contains(SelectedColorHex))
            {
                _config.CustomColors.Add(SelectedColorHex);
                if (_config.CustomColors.Count > 7) _config.CustomColors.RemoveAt(0); // max 7 custom slots, FIFO yeet
                _config.Save();
                PopulateCustomColors();
            }
        }

        private void BtnDeleteCustom_Click(object sender, RoutedEventArgs e)
        {
            if (_config.CustomColors.Contains(SelectedColorHex))
            {
                _config.CustomColors.Remove(SelectedColorHex);
                _config.Save();
                PopulateCustomColors();
            }
            else if (_config.CustomColors.Count > 0)
            {
                _config.CustomColors.RemoveAt(_config.CustomColors.Count - 1);
                _config.Save();
                PopulateCustomColors();
            }
        }

        #endregion

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            _config.Save();
            this.DialogResult = true;
            this.Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void ApplyLocalization(string language)
        {
            switch (language)
            {
                case "TR":
                    LblTitle.Text = "Renk Seçici";
                    LblStandardColors.Text = "Standart Renkler";
                    LblCustomColors.Text = "Özel Renkler";
                    BtnOk.Content = "Tamam";
                    BtnCancel.Content = "İptal";
                    break;
                case "DE":
                    LblTitle.Text = "Farbauswahl";
                    LblStandardColors.Text = "Standardfarben";
                    LblCustomColors.Text = "Benutzerdefinierte Farben";
                    BtnOk.Content = "OK";
                    BtnCancel.Content = "Abbrechen";
                    break;
                case "FR":
                    LblTitle.Text = "Sélecteur de Couleur";
                    LblStandardColors.Text = "Couleurs Standard";
                    LblCustomColors.Text = "Couleurs Personnalisées";
                    BtnOk.Content = "OK";
                    BtnCancel.Content = "Annuler";
                    break;
                case "ES":
                    LblTitle.Text = "Selector de Color";
                    LblStandardColors.Text = "Colores Estándar";
                    LblCustomColors.Text = "Colores Personalizados";
                    BtnOk.Content = "Aceptar";
                    BtnCancel.Content = "Cancelar";
                    break;
                case "RU":
                    LblTitle.Text = "Выбор цвета";
                    LblStandardColors.Text = "Стандартные цвета";
                    LblCustomColors.Text = "Пользовательские цвета";
                    BtnOk.Content = "ОК";
                    BtnCancel.Content = "Отмена";
                    break;
                case "CN":
                    LblTitle.Text = "颜色选择器";
                    LblStandardColors.Text = "标准颜色";
                    LblCustomColors.Text = "自定义颜色";
                    BtnOk.Content = "确定";
                    BtnCancel.Content = "取消";
                    break;
                case "BR":
                    LblTitle.Text = "Seletor de Cores";
                    LblStandardColors.Text = "Cores Padrão";
                    LblCustomColors.Text = "Cores Personalizadas";
                    BtnOk.Content = "OK";
                    BtnCancel.Content = "Cancelar";
                    break;
                default:
                    // EN or whatever fallback we got
                    LblTitle.Text = "Color Picker";
                    LblStandardColors.Text = "Standard Colors";
                    LblCustomColors.Text = "Custom Colors";
                    BtnOk.Content = "OK";
                    BtnCancel.Content = "Cancel";
                    break;
            }
        }

        #region HSV RGB Math

        private Color ColorFromHsv(double h, double s, double v)
        {
            double c = v * s;
            double hPrime = h / 60.0;
            double x = c * (1 - Math.Abs(hPrime % 2 - 1));
            
            double r1 = 0, g1 = 0, b1 = 0;
            if (hPrime >= 0 && hPrime < 1) { r1 = c; g1 = x; b1 = 0; }
            else if (hPrime >= 1 && hPrime < 2) { r1 = x; g1 = c; b1 = 0; }
            else if (hPrime >= 2 && hPrime < 3) { r1 = 0; g1 = c; b1 = x; }
            else if (hPrime >= 3 && hPrime < 4) { r1 = 0; g1 = x; b1 = c; }
            else if (hPrime >= 4 && hPrime < 5) { r1 = x; g1 = 0; b1 = c; }
            else if (hPrime >= 5 && hPrime <= 6) { r1 = c; g1 = 0; b1 = x; }

            double m = v - c;
            return Color.FromRgb((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
        }

        private void HsvFromColor(Color color, out double h, out double s, out double v)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            v = max;

            if (max > 0)
                s = delta / max;
            else
                s = 0;

            if (delta == 0)
                h = 0;
            else
            {
                if (r == max)
                    h = (g - b) / delta;
                else if (g == max)
                    h = 2 + (b - r) / delta;
                else
                    h = 4 + (r - g) / delta;

                h *= 60;
                if (h < 0) h += 360;
            }
        }

        #endregion
    }
}
