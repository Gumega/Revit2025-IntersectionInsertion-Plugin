using Autodesk.Revit.DB;
using System.Windows;
using System.Windows.Controls;

namespace ThorusRevitIntersectionPlugin
{
	public partial class Win : Window, IDisposable
	{
		public Document? structureDocument = null;
		public Document? tubesDocument = null;

		public Win(List<Document> openedDocuments)
		{
			InitializeComponent();
			LoadOpenedDocuments(openedDocuments);

			OnAwake();
		}

		private void OnAwake()
		{
			ButtonCalculate.Click += ButtonCalculate_Click;
			ButtonCancel.Click += ButtonCancel_Click;
			ComboBoxStructuralFile.SelectionChanged += ComboBoxes_SelectionChanged;
			ComboBoxTubesFile.SelectionChanged += ComboBoxes_SelectionChanged;
		}

		private void ComboBoxes_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			ButtonCalculate.IsEnabled = ComboBoxStructuralFile.SelectedItem != null && ComboBoxTubesFile.SelectedItem != null;
		}

		private void ButtonCancel_Click(object sender, RoutedEventArgs e)
		{
			OnDestroy();
			Close();
		}

		private void ButtonCalculate_Click(object sender, RoutedEventArgs e)
		{
			structureDocument = (Document)ComboBoxStructuralFile.SelectedItem;
			tubesDocument = (Document)ComboBoxTubesFile.SelectedItem;
			OnDestroy();
			Close();
		}

		private void LoadOpenedDocuments(List<Document> documents)
		{
			ComboBoxStructuralFile.ItemsSource = documents;
			ComboBoxStructuralFile.DisplayMemberPath = "Title";
			ComboBoxTubesFile.ItemsSource = documents;
			ComboBoxTubesFile.DisplayMemberPath = "Title";
		}

		private void OnDestroy()
		{
			ButtonCalculate.Click -= ButtonCalculate_Click;
			ButtonCancel.Click -= ButtonCancel_Click;
			ComboBoxStructuralFile.SelectionChanged -= ComboBoxes_SelectionChanged;
			ComboBoxTubesFile.SelectionChanged -= ComboBoxes_SelectionChanged;
		}

		public void Dispose()
		{
			OnDestroy();
		}
	}
}
