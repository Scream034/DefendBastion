namespace Game.Debug;

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

[Tool]
public partial class PromptGeneratorTool : VBoxContainer
{
    #region Экспортируемые узлы
    [Export] private Button _selectSourceButton;
    [Export] private Button _removeSourceButton;
    [Export] private Button _clearSourcesButton;
    [Export] private ItemList _sourcePathsList;
    [Export] private LineEdit _extensionsLineEdit;
    [Export] private Button _generateButton;
    [Export] private RichTextLabel _logLabel;
    [Export] private FileDialog _sourceFileDialog;
    [Export] private FileDialog _saveFileDialog;
    #endregion

    // Используем HashSet для хранения путей. Это элегантно решает проблему дубликатов:
    // попытка добавить уже существующий путь будет просто проигнорирована.
    // Это также обеспечивает быстрый доступ, хотя для нашего случая это не критично.
    private readonly HashSet<string> _selectedSourcePaths = new();
    
    // Хранилище для валидных расширений файлов
    private HashSet<string> _targetExtensions;

    public override void _Ready()
    {
        // Настройка диалога выбора папок
        _sourceFileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;

        // Подключаем сигналы к методам. Использование лямбд здесь лаконично и уместно.
        _selectSourceButton.Pressed += () => _sourceFileDialog.PopupCentered();
        _removeSourceButton.Pressed += OnRemoveSelectedSources;
        _clearSourcesButton.Pressed += OnClearSources;
        _generateButton.Pressed += OnGenerateButtonPressed;

        _sourceFileDialog.DirSelected += OnSourceDirectorySelected;
        _saveFileDialog.FileSelected += OnOutputFileSelected;
        
        // Связываем активность кнопки "Удалить" с наличием выделения в списке
        _sourcePathsList.ItemSelected += _ => _removeSourceButton.Disabled = false;
        _sourcePathsList.FocusExited += () => _removeSourceButton.Disabled = true;

        // Изначально кнопка удаления неактивна
        _removeSourceButton.Disabled = true;

        // Предзаполняем поле расширений для удобства.
        _extensionsLineEdit.Text = "cs, gd, gdshader, tscn, tres, res, json, md";
    }

    #region Обработчики UI событий

    /// <summary>
    /// Вызывается при выборе директории в FileDialog. Добавляет путь в список.
    /// </summary>
    private void OnSourceDirectorySelected(string dir)
    {
        // Метод Add у HashSet вернет false, если элемент уже существует.
        // Это позволяет нам избежать дубликатов как в данных, так и в UI.
        if (_selectedSourcePaths.Add(dir))
        {
            _sourcePathsList.AddItem(dir);
            Log($"Добавлена директория: {dir}", Colors.CornflowerBlue);
        }
        else
        {
            Log($"Директория уже в списке: {dir}", Colors.Yellow);
        }
    }

    /// <summary>
    /// Удаляет выделенные элементы из списка директорий.
    /// </summary>
    private void OnRemoveSelectedSources()
    {
        // Важно: при удалении нескольких элементов из коллекции нужно итерировать
        // в обратном порядке, чтобы не нарушить индексы оставшихся элементов.
        int[] selectedIndices = _sourcePathsList.GetSelectedItems();
        for (int i = selectedIndices.Length - 1; i >= 0; i--)
        {
            int index = selectedIndices[i];
            string pathToRemove = _sourcePathsList.GetItemText(index);

            _selectedSourcePaths.Remove(pathToRemove);
            _sourcePathsList.RemoveItem(index);
            
            Log($"Удалена директория: {pathToRemove}", Colors.Orange);
        }
    }

    /// <summary>
    /// Полностью очищает список исходных директорий.
    /// </summary>
    private void OnClearSources()
    {
        _selectedSourcePaths.Clear();
        _sourcePathsList.Clear();
        _removeSourceButton.Disabled = true;
        Log("Список исходных директорий очищен.", Colors.Yellow);
    }
    
    private void OnGenerateButtonPressed()
    {
        // --- Проверки на входе (Guard Clauses) ---
        if (_selectedSourcePaths.Count == 0)
        {
            LogError("Ошибка: Не выбрана ни одна исходная папка.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_extensionsLineEdit.Text))
        {
            LogError("Ошибка: Укажите хотя бы одно расширение файла.");
            return;
        }
        
        // Показываем диалог сохранения файла. Основная логика выполнится после выбора файла.
        _saveFileDialog.PopupCentered();
    }

    #endregion

    #region Основная логика

    private void OnOutputFileSelected(string path)
    {
        _logLabel.Clear();
        Log("Начинаем процесс сканирования...", Colors.Cyan);
        
        // Парсим расширения один раз перед началом цикла.
        _targetExtensions = _extensionsLineEdit.Text
            .Split(',')
            .Select(ext => $".{ext.Trim().ToLower()}") // Сразу добавляем точку и приводим к нижнему регистру
            .Where(ext => ext.Length > 1)
            .ToHashSet();

        var stringBuilder = new StringBuilder();
        
        // Итерируемся по всем выбранным директориям
        foreach (string sourceDir in _selectedSourcePaths)
        {
            Log($"Сканируем: {sourceDir}", Colors.DarkCyan);
            // Передаем `sourceDir` как `basePath` для корректного вычисления относительных путей
            ScanDirectory(sourceDir, sourceDir, stringBuilder);
        }
        
        SavePromptFile(path, stringBuilder.ToString());
    }

    /// <summary>
    /// Рекурсивно сканирует директорию.
    /// </summary>
    /// <param name="currentPath">Текущая директория для сканирования.</param>
    /// <param name="basePath">Корневая директория, от которой будут строиться относительные пути.</param>
    /// <param name="stringBuilder">Объект для построения итоговой строки.</param>
    private void ScanDirectory(string currentPath, string basePath, StringBuilder stringBuilder)
    {
        using var dir = DirAccess.Open(currentPath);
        if (dir == null)
        {
            LogError($"Не удалось получить доступ к директории: {currentPath}");
            return;
        }

        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != "")
        {
            if (fileName is "." or "..")
            {
                fileName = dir.GetNext();
                continue;
            }

            string fullPath = currentPath.PathJoin(fileName);

            if (dir.CurrentIsDir())
            {
                ScanDirectory(fullPath, basePath, stringBuilder);
            }
            else
            {
                string extension = System.IO.Path.GetExtension(fullPath).ToLower();
                if (_targetExtensions.Contains(extension))
                {
                    ProcessFile(fullPath, basePath, stringBuilder);
                }
            }
            fileName = dir.GetNext();
        }
    }
    
    private void ProcessFile(string filePath, string basePath, StringBuilder stringBuilder)
    {
        // Ключевое изменение: относительный путь теперь вычисляется от `basePath`,
        // который является одной из выбранных пользователем папок.
        string relativePath = filePath.Replace(basePath + "/", "");
        string extension = System.IO.Path.GetExtension(filePath); // Без точки
        string pathWithoutExtension = relativePath.Left(relativePath.Length - extension.Length);

        Log($"Обработка файла: {relativePath}");

        using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            LogError($"Не удалось прочитать файл: {filePath}");
            return;
        }

        string content = file.GetAsText();

        stringBuilder.AppendLine(pathWithoutExtension);
        stringBuilder.AppendLine($"```{extension.TrimStart('.')}");
        stringBuilder.AppendLine(content);
        stringBuilder.AppendLine("```");
        stringBuilder.AppendLine();
    }

    private void SavePromptFile(string path, string content)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            var error = FileAccess.GetOpenError();
            LogError($"Не удалось сохранить итоговый файл: {path} (Ошибка: {error})");
            return;
        }
        
        file.StoreString(content);
        Log($"Успех! Промпт сохранен в файл: {path}", Colors.LightGreen);
    }
    
    #endregion
    
    #region Вспомогательные методы логирования
    private void Log(string message, Color? color = null)
    {
        var finalColor = color ?? Colors.White;
        _logLabel.PushColor(finalColor);
        _logLabel.AppendText($"[INFO] {message}\n");
        _logLabel.Pop();
    }

    private void LogError(string message)
    {
        _logLabel.PushColor(Colors.Red);
        _logLabel.AppendText($"[ERROR] {message}\n");
        _logLabel.Pop();
    }
    #endregion
}