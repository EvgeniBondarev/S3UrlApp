using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using S3WebUrlApp.Application.S3Service.Implementation;
using S3WebUrlApp.Application.S3Service.Models;

namespace S3WebUrlApp
{
    /// <summary>
    /// Главное окно приложения для работы с S3 хранилищем
    /// </summary>
    public partial class MainWindow : Window
    {
        private S3Service _s3Service;
        private S3Settings _s3Settings;
        
        // Кэшированные данные
        private readonly Dictionary<string, List<string>> _folderCache = new Dictionary<string, List<string>>();
        private List<string> _foldersCache = new List<string>();
        private const int MaxVisibleFiles = 20; // Максимальное количество отображаемых файлов
        private List<string> _currentFiles = new List<string>();

        public ICommand OpenUrlCommand { get; }
        public ICommand CopyToClipboardCommand { get; }

        public MainWindow()
        {
            InitializeComponent();
            
            OpenUrlCommand = new RelayCommand(OpenUrl);
            CopyToClipboardCommand = new RelayCommand(CopyToClipboard);
            
            DataContext = this;
            Loaded += MainWindow_Loaded;
            
            ConfigureComboBox();
        }

        #region Initialization Methods

        /// <summary>
        /// Настройка ComboBox для поиска файлов
        /// </summary>
        private void ConfigureComboBox()
        {
            FileNameComboBox.IsTextSearchEnabled = true;
            FileNameComboBox.IsTextSearchCaseSensitive = false;
            FileNameComboBox.StaysOpenOnEdit = true;
        }

        /// <summary>
        /// Обработчик загрузки окна - инициализация сервиса и загрузка данных
        /// </summary>
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string appSettingsPath = GetAppSettingsPath();
                
                if (!File.Exists(appSettingsPath))
                {
                    ShowErrorAndClose("Файл appsettings.json не найден!");
                    return;
                }

                await InitializeS3Service(appSettingsPath);
                await LoadFoldersAsync();
            }
            catch (Exception ex)
            {
                ShowErrorAndClose($"Ошибка инициализации: {ex.Message}");
            }
        }

        /// <summary>
        /// Получает путь к файлу настроек приложения
        /// </summary>
        private string GetAppSettingsPath()
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(exeDirectory, "appsettings.json");
        }

        /// <summary>
        /// Инициализирует S3 сервис на основе настроек из файла
        /// </summary>
        private async Task InitializeS3Service(string appSettingsPath)
        {
            var json = File.ReadAllText(appSettingsPath);
            var settings = JObject.Parse(json);
            _s3Settings = settings["S3Settings"].ToObject<S3Settings>();
            _s3Service = new S3Service(_s3Settings);
        }

        #endregion

        #region Command Methods

        /// <summary>
        /// Открывает URL в браузере по умолчанию
        /// </summary>
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
                    ShowErrorMessage($"Не удалось открыть ссылку: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Копирует текст в буфер обмена
        /// </summary>
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
                    ShowErrorMessage($"Не удалось скопировать в буфер обмена: {ex.Message}");
                }
            }
        }

        #endregion

        #region Folder Operations

        /// <summary>
        /// Загружает список папок из S3 хранилища
        /// </summary>
        /// <param name="forceRefresh">Принудительное обновление кэша</param>
        private async Task LoadFoldersAsync(bool forceRefresh = false)
        {
            try
            {
                StatusText.Text = "Загрузка папок...";
        
                if (forceRefresh || _foldersCache.Count == 0)
                {
                    await RefreshFoldersCache();
                }

                FolderComboBox.ItemsSource = _foldersCache;
                UpdateStatusTextForFolders();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки папок";
                ShowErrorMessage($"Ошибка: {ex.Message}");
            }
        }

        /// <summary>
        /// Обновляет кэш папок
        /// </summary>
        private async Task RefreshFoldersCache()
        {
            var rawFolders = await _s3Service.GetAllFoldersAsync();
            _foldersCache = rawFolders.Select(f => f.TrimEnd('/')).ToList();
            _folderCache.Clear();
        }

        /// <summary>
        /// Обновляет текст статуса в зависимости от количества загруженных папок
        /// </summary>
        private void UpdateStatusTextForFolders()
        {
            StatusText.Text = _foldersCache.Count > 0 
                ? $"Готово (кэш: {_foldersCache.Count} папок)" 
                : "Папки не найдены";
        }

        #endregion

        #region File Operations

        /// <summary>
        /// Обработчик изменения выбранной папки - загружает файлы из выбранной папки
        /// </summary>
        private async void FolderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FolderComboBox.SelectedItem == null) return;

            string selectedFolder = NormalizeFolderPath(FolderComboBox.SelectedItem.ToString());

            try
            {
                StatusText.Text = "Загрузка файлов...";
                await LoadFilesFromFolder(selectedFolder);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки файлов";
                ShowErrorMessage($"Ошибка: {ex.Message}");
            }
        }

        /// <summary>
        /// Нормализует путь к папке (добавляет слеш в конце если нужно)
        /// </summary>
        private string NormalizeFolderPath(string folderPath)
        {
            return folderPath.EndsWith("/") ? folderPath : folderPath + "/";
        }

        /// <summary>
        /// Загружает файлы из указанной папки
        /// </summary>
        private async Task LoadFilesFromFolder(string folderPath)
        {
            if (!_folderCache.TryGetValue(folderPath, out var files))
            {
                files = await GetFilesFromS3(folderPath);
                _folderCache[folderPath] = files;
                _currentFiles = files;
            }

            UpdateFileComboBox(files);
            UpdateStatusTextForFiles(files.Count);
        }

        /// <summary>
        /// Получает файлы из S3 хранилища и обрезает расширения
        /// </summary>
        private async Task<List<string>> GetFilesFromS3(string folderPath)
        {
            var rawFiles = await _s3Service.GetFilesInFolderAsync(folderPath);
            return rawFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
        }

        /// <summary>
        /// Обновляет ComboBox с файлами (отображает первые N файлов + подсказку)
        /// </summary>
        private void UpdateFileComboBox(List<string> files)
        {
            var displayFiles = files.Take(MaxVisibleFiles).ToList();
            if (files.Count > MaxVisibleFiles)
            {
                displayFiles.Add($"... и ещё {files.Count - MaxVisibleFiles} файлов");
            }

            FileNameComboBox.ItemsSource = displayFiles;
            FileNameComboBox.ToolTip = files.Count > MaxVisibleFiles 
                ? $"Всего файлов: {files.Count}\n{string.Join("\n", files)}" 
                : string.Join("\n", files);
        }

        /// <summary>
        /// Обновляет текст статуса в зависимости от количества загруженных файлов
        /// </summary>
        private void UpdateStatusTextForFiles(int fileCount)
        {
            StatusText.Text = $"Готово (кэш: {fileCount} файлов)";
        }

        #endregion

        #region Search Operations

        /// <summary>
        /// Обработчик ввода текста для поиска файлов - обновляет список файлов по мере ввода
        /// </summary>
        private async void FileNameComboBox_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Back)
                return;

            await UpdateFileListAsync();
        }

        /// <summary>
        /// Обновляет список файлов на основе введенного текста
        /// </summary>
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
                ShowErrorMessage($"Ошибка: {ex.Message}");
            }
        }

        /// <summary>
        /// Возвращает список файлов, начинающихся с указанного префикса (без учета регистра)
        /// </summary>
        public List<string> GetFilesByPrefix(string prefix)
        {
            return _currentFiles
                .Where(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetFileNameWithoutExtension(file))
                .ToList();
        }

        /// <summary>
        /// Обработчик кнопки поиска - выполняет поиск файлов по имени
        /// </summary>
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateSearchInput())
                    return;

                string folderPath = NormalizeFolderPath(FolderComboBox.Text);
                string baseFileName = FileNameComboBox.Text.Trim();
                
                StatusText.Text = "Поиск файлов...";
                ResultsListView.ItemsSource = null;

                var fileUrls = await _s3Service.GetFileUrlsByBaseNameAsync(folderPath, baseFileName);
                ResultsListView.ItemsSource = fileUrls;

                UpdateStatusTextForSearchResults(fileUrls.Count);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка поиска";
                ShowErrorMessage($"Ошибка: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверяет корректность введенных данных для поиска
        /// </summary>
        private bool ValidateSearchInput()
        {
            if (string.IsNullOrWhiteSpace(FolderComboBox.Text))
            {
                ShowWarningMessage("Укажите папку для поиска");
                return false;
            }

            if (string.IsNullOrWhiteSpace(FileNameComboBox.Text))
            {
                ShowWarningMessage("Введите имя файла");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Обновляет текст статуса в зависимости от результатов поиска
        /// </summary>
        private void UpdateStatusTextForSearchResults(int foundCount)
        {
            StatusText.Text = foundCount > 0 
                ? $"Найдено: {foundCount} файлов" 
                : "Файлы не найдены";
        }

        #endregion

        #region UI Helpers

        /// <summary>
        /// Обработчик кнопки обновления - обновляет список папок
        /// </summary>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadFoldersAsync(true); // Принудительное обновление
        }

        /// <summary>
        /// Показывает сообщение об ошибке и закрывает приложение
        /// </summary>
        private void ShowErrorAndClose(string message)
        {
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }

        /// <summary>
        /// Показывает сообщение об ошибке
        /// </summary>
        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// Показывает предупреждающее сообщение
        /// </summary>
        private void ShowWarningMessage(string message)
        {
            MessageBox.Show(message, "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        #endregion
    }

    /// <summary>
    /// Реализация команды ICommand для обработки действий в UI
    /// </summary>
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