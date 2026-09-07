using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MyNotes.Templates;

public sealed partial class TextContentControl : Control
{

  public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(TextContentControl), new PropertyMetadata(null));
  public string Text
  {
    get => (string)GetValue(TextProperty);
    set => SetValue(TextProperty, value);
  }

  public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register("TextWrapping", typeof(TextWrapping), typeof(TextContentControl), new PropertyMetadata(TextWrapping.NoWrap));
  public TextWrapping TextWrapping
  {
    get => (TextWrapping)GetValue(TextWrappingProperty);
    set => SetValue(TextWrappingProperty, value);
  }

  public static readonly DependencyProperty TextTrimmingProperty = DependencyProperty.Register("TextTrimming", typeof(TextTrimming), typeof(TextContentControl), new PropertyMetadata(TextTrimming.CharacterEllipsis));
  public TextTrimming TextTrimming
  {
    get => (TextTrimming)GetValue(TextTrimmingProperty);
    set => SetValue(TextTrimmingProperty, value);
  }

  public static readonly DependencyProperty IsTextSelectionEnabledProperty = DependencyProperty.Register("IsTextSelectionEnabled", typeof(bool), typeof(TextContentControl), new PropertyMetadata(false));
  public bool IsTextSelectionEnabled
  {
    get => (bool)GetValue(IsTextSelectionEnabledProperty);
    set => SetValue(IsTextSelectionEnabledProperty, value);
  }

  public TextContentControl()
  {
    DefaultStyleKey = typeof(TextContentControl);
    IsEnabledChanged += TextBlockControl_IsEnabledChanged;
    this.Loaded += TextContentControl_Loaded;
  }

  protected override void OnApplyTemplate()
  {
    base.OnApplyTemplate();
    UpdateEnabledVisualState();
  }

  private void TextBlockControl_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateEnabledVisualState();

  private void TextContentControl_Loaded(object sender, RoutedEventArgs e) => UpdateEnabledVisualState();

  private void UpdateEnabledVisualState() => VisualStateManager.GoToState(this, IsEnabled ? "Enabled" : "Disabled", false);
}
