using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace E3G.Saneamiento
{
    public enum SpinnerState { Idle, Loading, Done }

    public partial class SpinnerButton : UserControl
    {
        private SpinnerState _state = SpinnerState.Idle;
        private Storyboard _spinStoryboard;
        private Storyboard _checkStoryboard;

        public event RoutedEventHandler Clicked;

        private string _accentColor = "#4a7a96";
        public string AccentColor
        {
            get => _accentColor;
            set
            {
                _accentColor = value;
                ApplyAccentColor();
            }
        }

        public SpinnerButton()
        {
            InitializeComponent();
            _spinStoryboard = (Storyboard)Resources["SpinStoryboard"];
            _checkStoryboard = (Storyboard)Resources["CheckStoryboard"];
        }

        public void SetState(SpinnerState state)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetState(state));
                return;
            }

            _state = state;

            switch (state)
            {
                case SpinnerState.Idle:
                    _spinStoryboard.Stop();
                    SpinnerArc.Visibility = Visibility.Collapsed;
                    DoneRing.Visibility = Visibility.Collapsed;
                    CheckPath.Visibility = Visibility.Collapsed;
                    IconPlay.Visibility = Visibility.Visible;
                    IconPause.Visibility = Visibility.Collapsed;
                    SetShadowElevated();
                    break;

                case SpinnerState.Loading:
                    DoneRing.Visibility = Visibility.Collapsed;
                    CheckPath.Visibility = Visibility.Collapsed;
                    IconPlay.Visibility = Visibility.Collapsed;
                    IconPause.Visibility = Visibility.Visible;
                    SpinnerArc.Visibility = Visibility.Visible;
                    _spinStoryboard.Begin();
                    SetShadowInset();
                    break;

                case SpinnerState.Done:
                    _spinStoryboard.Stop();
                    SpinnerArc.Visibility = Visibility.Collapsed;
                    IconPlay.Visibility = Visibility.Collapsed;
                    IconPause.Visibility = Visibility.Collapsed;
                    DoneRing.Visibility = Visibility.Visible;
                    CheckPath.Visibility = Visibility.Visible;
                    _checkStoryboard.Begin();
                    SetShadowElevated();
                    break;
            }
        }

        private void ApplyAccentColor()
        {
            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(_accentColor));
            SpinnerArc.Stroke = brush;
            IconPlay.Fill = brush;

            var pauseBrush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(_accentColor));
            foreach (UIElement child in IconPause.Children)
            {
                if (child is System.Windows.Shapes.Rectangle rect)
                    rect.Fill = pauseBrush;
            }
        }

        private void SetShadowElevated()
        {
            if (BtnBorder.Effect is DropShadowEffect dse)
            {
                dse.ShadowDepth = 5;
                dse.BlurRadius = 10;
            }
        }

        private void SetShadowInset()
        {
            if (BtnBorder.Effect is DropShadowEffect dsi)
            {
                dsi.ShadowDepth = 2;
                dsi.BlurRadius = 5;
            }
        }

        private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_state == SpinnerState.Loading) return;
            SetShadowInset();
        }

        private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_state == SpinnerState.Loading) return;
            SetShadowElevated();
            Clicked?.Invoke(this, new RoutedEventArgs());
        }

        private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_state != SpinnerState.Loading)
                SetShadowElevated();
        }
    }
}