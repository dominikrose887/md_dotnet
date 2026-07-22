using System.Windows;
using MdViewer.Services;

namespace MdViewer;

public partial class KeyboardShortcutsWindow : Window
{
    public KeyboardShortcutsWindow()
    {
        InitializeComponent();
        GroupsList.ItemsSource = KeyboardShortcutsCatalog.Groups;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
