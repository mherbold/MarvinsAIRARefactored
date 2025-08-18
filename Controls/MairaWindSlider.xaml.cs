using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
namespace MarvinsAIRARefactored.Controls
{
    /// <summary>
    /// Interaction logic for MairaWindSlider.xaml
    /// </summary>
    public partial class MairaWindSlider : System.Windows.Controls.UserControl
    {
        public MairaWindSlider()
        {
            InitializeComponent();
            TrackBackground = Brushes.Black;
        }

        public static int WindPreviewItemIndex { get; set; } = -1;
        public static MairaWindSlider CurrentPreview { get; set; }


        public static readonly DependencyProperty TargetSpeedKPHProperty =
         DependencyProperty.Register(
             nameof(TargetSpeedKPH),
             typeof(double),
             typeof(MairaWindSlider),
             new PropertyMetadata(0.0));

        public double TargetSpeedKPH
        {
            get => (double)GetValue(TargetSpeedKPHProperty);
            set => SetValue(TargetSpeedKPHProperty, value);
        }


        public static readonly DependencyProperty FanPowerProperty =
       DependencyProperty.Register(
           nameof(FanPower),
           typeof(double),
           typeof(MairaWindSlider),
           new PropertyMetadata(0.0));

        public double FanPower
        {
            get => (double)GetValue(FanPowerProperty);
            set => SetValue(FanPowerProperty, value);
        }

        public static readonly DependencyProperty IdNumberProperty =
        DependencyProperty.Register(
         nameof(IdNumber),
         typeof(int),
         typeof(MairaWindSlider),
         new PropertyMetadata(0));

        public int IdNumber
        {
            get => (int)GetValue(IdNumberProperty);
            set => SetValue(IdNumberProperty, value);
        }

        public static readonly DependencyProperty TrackBackgroundProperty =
        DependencyProperty.Register(
        nameof(TrackBackground),
        typeof(Brush),
        typeof(MairaWindSlider),
        new PropertyMetadata(Brushes.Gray));

        public Brush TrackBackground
        {
            get => (Brush)GetValue(TrackBackgroundProperty);
            set => SetValue(TrackBackgroundProperty, value);
        }



        private static void TargetSpeedKPHChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (MairaKnob)d;

            //control.UpdateLabelVisual();
        }

        private void Slider_Wind_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
          
            if (MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.WindSimulatorPreviewEnabled)
            {
               
                WindPreviewItemIndex = IdNumber;

                TrackBackground = Brushes.White;

                CurrentPreview = this;
            }
        }

        private bool IsTextNumeric(string text)
        {
            return int.TryParse(text, out _);
        }

        private void TextBox_Wind_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextNumeric(e.Text);
        }

        private void Slider_Wind_01_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            //TrackBackground = Brushes.Black;
        }

        private void Slider_Wind_01_LostFocus(object sender, RoutedEventArgs e)
        {
            TrackBackground = Brushes.Black;
        }

        private void Slider_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.WindSimulatorPreviewEnabled)
            {

                WindPreviewItemIndex = IdNumber;

                TrackBackground = Brushes.White;

                CurrentPreview = this;
            }
        }
    }
}
