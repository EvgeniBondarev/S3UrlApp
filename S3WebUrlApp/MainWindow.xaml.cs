using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using S3WebUrlApp.Application.S3Service.Implementation;
using S3WebUrlApp.Application.S3Service.Models;

namespace S3WebUrlApp
{
   public partial class MainWindow : Window
    {
        private S3Service _s3Service;
        private S3Settings _s3Settings;
        
        // Кэшированные данные
        private Dictionary<string, List<string>> _folderCache = new Dictionary<string, List<string>>();
        private List<string> _foldersCache = new List<string>();
        private const int MaxVisibleFiles = 20; // Максимальное количество отображаемых файлов
        private  List<string> _currentFiles = new List<string>();

        public ICommand OpenUrlCommand { get; }
        public ICommand CopyToClipboardCommand { get; }

        public MainWindow()
        {
            InitializeComponent();
            
            OpenUrlCommand = new RelayCommand(OpenUrl);
            CopyToClipboardCommand = new RelayCommand(CopyToClipboard);
            
            DataContext = this;
            Loaded += MainWindow_Loaded;
            
            // Настройка автодополнения для ComboBox
            FileNameComboBox.IsTextSearchEnabled = true;
            FileNameComboBox.IsTextSearchCaseSensitive = false;
            FileNameComboBox.StaysOpenOnEdit = true;
        }

        private void OpenUrl(object parameter)
        {
            if (parameter is string url)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось открыть ссылку: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyToClipboard(object parameter)
        {
            if (parameter is string text)
            {
                try
                {
                    Clipboard.SetText(text);
                    StatusText.Text = "Ссылка скопирована в буфер обмена";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось скопировать в буфер обмена: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string appSettingsPath = Path.Combine(
                    Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.FullName, 
                    "appsettings.json");

                if (!File.Exists(appSettingsPath))
                {
                    MessageBox.Show("Файл appsettings.json не найден!", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                var json = File.ReadAllText(appSettingsPath);
                var settings = JObject.Parse(json);
                _s3Settings = settings["S3Settings"].ToObject<S3Settings>();
                _s3Service = new S3Service(_s3Settings);

                await LoadFoldersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private async Task LoadFoldersAsync(bool forceRefresh = false)
        {
            try
            {
                StatusText.Text = "Загрузка папок...";
                
                if (forceRefresh || _foldersCache.Count == 0)
                {
                    _foldersCache = await _s3Service.GetAllFoldersAsync();
                    _folderCache.Clear(); // Очищаем кэш файлов при обновлении папок
                }

                FolderComboBox.ItemsSource = _foldersCache;
                StatusText.Text = _foldersCache.Count > 0 
                    ? $"Готово (кэш: {_foldersCache.Count} папок)" 
                    : "Папки не найдены";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки папок";
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void FolderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FolderComboBox.SelectedItem == null) return;

            string selectedFolder = FolderComboBox.SelectedItem.ToString();
            if (!selectedFolder.EndsWith("/")) selectedFolder += "/";

            try
            {
                StatusText.Text = "Загрузка файлов...";
                
                if (!_folderCache.TryGetValue(selectedFolder, out var files))
                {
                    files = await _s3Service.GetFilesInFolderAsync(selectedFolder);
                    _folderCache[selectedFolder] = files;
                    _currentFiles = files;
                }

                // Отображаем только первые N файлов + подсказку с полным списком
                var displayFiles = files.Take(MaxVisibleFiles).ToList();
                if (files.Count > MaxVisibleFiles)
                {
                    displayFiles.Add($"... и ещё {files.Count - MaxVisibleFiles} файлов");
                }

                FileNameComboBox.ItemsSource = displayFiles;
                FileNameComboBox.ToolTip = files.Count > MaxVisibleFiles 
                    ? $"Всего файлов: {files.Count}\n{string.Join("\n", files)}" 
                    : string.Join("\n", files);
                
                StatusText.Text = $"Готово (кэш: {files.Count} файлов)";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки файлов";
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void FileNameComboBox_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Back)
                return;

            await UpdateFileListAsync();
        }

        private async Task UpdateFileListAsync()
        {
            string inputText = FileNameComboBox.Text;
    
            if (string.IsNullOrEmpty(inputText))
            {
                FileNameComboBox.ItemsSource = null;
                return;
            }

            try
            {
                StatusText.Text = "Поиск файлов...";
                var displayFiles = GetFilesByPrefix(inputText);
                FileNameComboBox.ItemsSource = displayFiles;
                FileNameComboBox.IsDropDownOpen = displayFiles.Any();
                StatusText.Text = "Готово";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка поиска файлов";
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public List<string> GetFilesByPrefix(string prefix)
        {
            return _currentFiles
                .Where(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadFoldersAsync(true); // Принудительное обновление
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(FolderComboBox.Text))
                {
                    MessageBox.Show("Укажите папку для поиска", "Внимание", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(FileNameComboBox.Text))
                {
                    MessageBox.Show("Введите имя файла", "Внимание",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string folderPath = FolderComboBox.Text;
                if (!folderPath.EndsWith("/")) folderPath += "/";

                string baseFileName = FileNameComboBox.Text.Trim();
                
                StatusText.Text = "Поиск файлов...";
                ResultsListView.ItemsSource = null;

                var fileUrls = await _s3Service.GetFileUrlsByBaseNameAsync(folderPath, baseFileName);
                ResultsListView.ItemsSource = fileUrls;

                StatusText.Text = fileUrls.Count > 0 
                    ? $"Найдено: {fileUrls.Count} файлов" 
                    : "Файлы не найдены";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка поиска";
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}