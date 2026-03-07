using System.Windows;
using System.Windows.Controls;

namespace MarvinsAIRARefactored.Controls
{
    public class MairaTabItem : TabItem
    {
        public static readonly DependencyProperty HelpTopicProperty = DependencyProperty.Register(
            "HelpTopic",
            typeof(string),
            typeof(MairaTabItem),
            new PropertyMetadata(null, OnHelpTopicChanged));

        public string? HelpTopic
        {
            get => (string?)GetValue(HelpTopicProperty);
            set => SetValue(HelpTopicProperty, value);
        }

        private static void OnHelpTopicChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MairaTabItem item)
            {
                Classes.HelpService.SetHelpTopic(item, e.NewValue as string);
            }
        }
    }
}
