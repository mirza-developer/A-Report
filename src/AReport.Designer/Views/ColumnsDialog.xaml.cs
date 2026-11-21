using System.Collections.ObjectModel;
using System.Windows;
using AReport.Model;

namespace AReport.Designer.Views;

public partial class ColumnsDialog : Window
{
    public static Array AlignmentValues => Enum.GetValues(typeof(HAlign));

    public ColumnsDialog(IEnumerable<TableColumn> columns)
    {
        // Edit copies so Cancel leaves the design untouched.
        Columns = new ObservableCollection<TableColumn>(columns.Select(c => new TableColumn
        {
            Header = c.Header,
            Binding = c.Binding,
            Weight = c.Weight,
            Alignment = c.Alignment,
            Format = c.Format,
        }));
        InitializeComponent();
    }

    public ObservableCollection<TableColumn> Columns { get; }

    public List<TableColumn>? Result { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        Result = Columns.ToList();
        DialogResult = true;
    }
}
