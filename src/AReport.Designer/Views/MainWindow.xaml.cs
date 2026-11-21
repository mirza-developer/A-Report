using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AReport.Designer.Views;

public partial class MainWindow : Window
{
    // Breakpoints from DESIGN.md section 5.
    private const double CompactBelow = 900;

    private readonly DesignerViewModel _viewModel;
    private bool _compact;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new DesignerViewModel(new WpfShell(this));
        DataContext = _viewModel;

        SizeChanged += (_, e) => ApplyLayoutMode(e.NewSize.Width);
        Loaded += (_, _) =>
        {
            _layoutApplied = false; // the toolbox buttons exist now, so labels can be set
            ApplyLayoutMode(ActualWidth);
        };
        Closing += (_, e) => e.Cancel = !_viewModel.CanClose();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void OpenFile(string path) => _viewModel.OpenFile(path);

    // ---------- responsive layout (DESIGN.md section 5) ----------

    private void ApplyLayoutMode(double width)
    {
        bool compact = width < CompactBelow;
        if (_layoutApplied && compact == _compact) return;

        _compact = compact;
        _layoutApplied = true;
        ApplyToolboxLabels(compact);
        Toolbox.Width = (double)FindResource(compact ? "Size.ToolboxCompact" : "Size.ToolboxWide");
        ZoomGroup.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        PropertiesToggle.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ToolboxHeading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ArrangeHeading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

        if (compact)
        {
            // Slide the panel over the canvas, hidden until the toggle is on.
            Grid.SetColumn(PropertiesPanel, 1);
            PropertiesPanel.HorizontalAlignment = HorizontalAlignment.Right;
            Panel.SetZIndex(PropertiesPanel, 10);
            PropertiesPanel.Effect = new DropShadowEffect
            {
                Color = (Color)FindResource("Color.TextPrimary"),
                Opacity = 0.25,
                BlurRadius = (double)FindResource("Space.3"),
                ShadowDepth = 0,
            };
            PropertiesPanel.Visibility = PropertiesToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            Grid.SetColumn(PropertiesPanel, 2);
            PropertiesPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            Panel.SetZIndex(PropertiesPanel, 0);
            PropertiesPanel.Effect = null;
            PropertiesPanel.Visibility = Visibility.Visible;
        }
    }

    private bool _layoutApplied;

    /// <summary>Full words normally; only the first letter in compact mode (the full name stays available to screen readers and tooltips).</summary>
    private void ApplyToolboxLabels(bool compact)
    {
        foreach (var button in FindButtons(Toolbox))
        {
            if (button.Tag is not string label) continue;
            button.Content = compact ? label[..1] : label;
            AutomationProperties.SetName(button, label);
        }
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }

    private void OnPropertiesToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_compact)
            PropertiesPanel.Visibility = PropertiesToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- input ----------

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        _viewModel.SetZoom(_viewModel.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
        e.Handled = true;
    }

    /// <summary>Enter commits the value typed in a single-line property box.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox { AcceptsReturn: false } box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }
}
