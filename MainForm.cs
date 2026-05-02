using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LegoConstructorApp;

/// Главное окно приложения.
/// В этом классе находится только интерфейс и обработка действий пользователя.
/// Предметная логика вынесена в WorkspaceService.

public sealed class MainForm : Form
{
    private readonly WorkspaceService _workspace = WorkspaceService.CreateDefault();
    private readonly LegoCanvas _canvas = new();

    private readonly ListBox _brickList = new();
    private readonly ComboBox _colorBox = new();
    private readonly ComboBox _viewBox = new();
    private readonly Label _selectedInfo = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    private string? _currentFilePath;
    private bool _interfaceReady;
    private int _customColorCounter = 1;

    public MainForm()
    {
        BuildInterface();
        BindEvents();
        FillControls();

        // При запуске пробуем восстановить прошлую работу.
        // Это закрывает нефункциональное требование о возможности перезапуска после сбоя.
        TryRestoreAutosave();

        RefreshCanvas("Приложение запущено. Выберите деталь и кликните по рабочей области.");
        _interfaceReady = true;
    }


    private void BuildInterface()
    {
        Text = "Лего-конструктор — Windows Forms";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 800);
        Size = new Size(1280, 820);
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var menu = BuildMenu();
        Controls.Add(menu);
        MainMenuStrip = menu;

        var status = new StatusStrip();
        status.Items.Add(_statusLabel);
        Controls.Add(status);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 200,
            Panel1MinSize = 140,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(split);
        split.BringToFront();

        var leftPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            AutoScroll = true
        };
        split.Panel1.Controls.Add(leftPanel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 12
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leftPanel.Controls.Add(layout);

        layout.Controls.Add(MakeTitle("Библиотека деталей"));
        _brickList.Height = 150;
        _brickList.Dock = DockStyle.Top;
        layout.Controls.Add(_brickList);

        layout.Controls.Add(MakeTitle("Цвет"));
        _colorBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _colorBox.Dock = DockStyle.Top;
        layout.Controls.Add(_colorBox);

        layout.Controls.Add(MakeButton("Применить цвет", ApplySelectedColor));
        layout.Controls.Add(MakeButton("Выбрать другой цвет", ChooseCustomColor));

        layout.Controls.Add(MakeTitle("Перемещение выбранной детали"));
        layout.Controls.Add(BuildMovePanel());

        layout.Controls.Add(MakeButton("Повернуть на 90°", RotateSelectedBrick));
        layout.Controls.Add(MakeButton("Удалить выбранную деталь", DeleteSelectedBrick));

        layout.Controls.Add(MakeTitle("Ракурс"));
        _viewBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _viewBox.Dock = DockStyle.Top;
        layout.Controls.Add(_viewBox);

        _selectedInfo.Dock = DockStyle.Top;
        _selectedInfo.AutoSize = true;
        _selectedInfo.Padding = new Padding(0, 10, 0, 0);
        _selectedInfo.Text = "Выбранная деталь: нет";
        layout.Controls.Add(_selectedInfo);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 12, 0, 0),
            Text = "Подсказки:\n" +
                   "• клик по пустому месту — поставить деталь;\n" +
                   "• клик по детали — выделить её;\n" +
                   "• стрелки двигают выделенную деталь;\n" +
                   "• Delete удаляет выделенную деталь."
        };
        layout.Controls.Add(hint);

        _canvas.Dock = DockStyle.Fill;
        _canvas.BackColor = Color.White;
        split.Panel2.Controls.Add(_canvas);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var fileMenu = new ToolStripMenuItem("Файл");
        fileMenu.DropDownItems.Add("Новый проект", null, (_, _) => NewWorkspace());
        fileMenu.DropDownItems.Add("Открыть...", null, (_, _) => LoadWorkspace());
        fileMenu.DropDownItems.Add("Сохранить", null, (_, _) => SaveWorkspace());
        fileMenu.DropDownItems.Add("Сохранить как...", null, (_, _) => SaveWorkspaceAs());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Выход", null, (_, _) => Close());

        var toolsMenu = new ToolStripMenuItem("Инструменты");
        toolsMenu.DropDownItems.Add("Заполнить тестовыми 100 деталями", null, (_, _) => FillPerformanceDemo());
        toolsMenu.DropDownItems.Add("Очистить автосохранение", null, (_, _) => ClearAutosave());

        var helpMenu = new ToolStripMenuItem("Справка");
        helpMenu.DropDownItems.Add("О программе", null, (_, _) => ShowAbout());

        menu.Items.Add(fileMenu);
        menu.Items.Add(toolsMenu);
        menu.Items.Add(helpMenu);
        menu.Dock = DockStyle.Top;

        return menu;
    }

    private static Label MakeTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Top,
        Padding = new Padding(0, 12, 0, 4),
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
    };

    private static Button MakeButton(string text, EventHandler clickHandler)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 34,
            Margin = new Padding(0, 4, 0, 4)
        };
        button.Click += clickHandler;
        return button;
    }

    private Control BuildMovePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 140,
            ColumnCount = 3,
            RowCount = 3
        };

        for (int i = 0; i < 3; i++)
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

        for (int i = 0; i < 3; i++)
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

        panel.Controls.Add(MakeSmallMoveButton("↑", 0, 0, 1), 1, 0);
        panel.Controls.Add(MakeSmallMoveButton("↙", 0, 1, 0), 0, 1);
        panel.Controls.Add(MakeSmallMoveButton("↗", 0, -1, 0), 2, 1);
        panel.Controls.Add(MakeSmallMoveButton("↓", 0, 0, -1), 1, 2);
        panel.Controls.Add(MakeSmallMoveButton("↖", -1, 0, 0), 0, 0);
        panel.Controls.Add(MakeSmallMoveButton("↘", 1, 0, 0), 2, 2);

        return panel;
    }

    private Button MakeSmallMoveButton(string text, int dx, int dy, int dz)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(2),
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        };
        button.Click += (_, _) => MoveSelectedBrick(dx, dy, dz);
        return button;
    }

    private void BindEvents()
    {
        _canvas.EmptyCellClicked += (_, point) => PlaceBrick(point);
        _canvas.BrickClicked += (_, brickId) => SelectBrick(brickId);

        _viewBox.SelectedIndexChanged += (_, _) =>
        {
            if (!_interfaceReady || _viewBox.SelectedItem is not CameraView view)
                return;

            _workspace.View = view;
            RefreshCanvas($"Ракурс изменён: {ViewNames.GetName(view)}.");
            SaveAutosaveQuietly();
        };

        KeyDown += MainForm_KeyDown;
        FormClosing += (_, _) => SaveAutosaveQuietly();
    }

    private void FillControls()
    {
        _brickList.DataSource = _workspace.BrickDefinitions.ToList();
        _brickList.DisplayMember = nameof(BrickDefinition.DisplayName);

        _colorBox.Items.AddRange(new object[]
        {
            new NamedColor("Красный", Color.Firebrick),
            new NamedColor("Синий", Color.RoyalBlue),
            new NamedColor("Жёлтый", Color.Goldenrod),
            new NamedColor("Зелёный", Color.SeaGreen),
            new NamedColor("Белый", Color.White),
            new NamedColor("Чёрный", Color.DimGray)
        });
        _colorBox.SelectedIndex = 0;

        _viewBox.Items.AddRange(Enum.GetValues<CameraView>().Cast<object>().ToArray());
        _viewBox.Format += (_, e) =>
        {
            if (e.ListItem is CameraView view)
                e.Value = ViewNames.GetName(view);
        };
        _viewBox.SelectedItem = _workspace.View;
    }

    private void PlaceBrick(GridPoint point)
    {
        if (_brickList.SelectedItem is not BrickDefinition definition)
        {
            ShowStatus("Сначала выберите тип детали в библиотеке.");
            return;
        }

        Color color = GetSelectedColor();
        var watch = Stopwatch.StartNew();
        bool ok = _workspace.TryPlaceBrick(definition.Id, point, color, out string message);
        watch.Stop();

        RefreshCanvas(ok
            ? $"Деталь размещена в точке {point}. Время операции: {watch.ElapsedMilliseconds} мс."
            : message);

        if (ok)
            SaveAutosaveQuietly();
    }

    private void SelectBrick(Guid brickId)
    {
        _workspace.SelectBrick(brickId);
        BrickInstance? selected = _workspace.SelectedBrick;

        if (selected is not null)
            ShowStatus($"Выбрана деталь: {selected.Definition.DisplayName}, координаты {selected.Origin}.");

        RefreshCanvas();
    }

    private void MoveSelectedBrick(int dx, int dy, int dz)
    {
        var watch = Stopwatch.StartNew();
        bool ok = _workspace.TryMoveSelected(dx, dy, dz, out string message);
        watch.Stop();

        RefreshCanvas(ok
            ? $"Деталь перемещена. Время операции: {watch.ElapsedMilliseconds} мс."
            : message);

        if (ok)
            SaveAutosaveQuietly();
    }

    private void RotateSelectedBrick(object? sender, EventArgs e)
    {
        var watch = Stopwatch.StartNew();
        bool ok = _workspace.TryRotateSelected(out string message);
        watch.Stop();

        RefreshCanvas(ok
            ? $"Деталь повернута на 90°. Время операции: {watch.ElapsedMilliseconds} мс."
            : message);

        if (ok)
            SaveAutosaveQuietly();
    }

    private void DeleteSelectedBrick(object? sender, EventArgs e)
    {
        bool ok = _workspace.DeleteSelected(out string message);
        RefreshCanvas(message);

        if (ok)
            SaveAutosaveQuietly();
    }

    private void ApplySelectedColor(object? sender, EventArgs e)
    {
        bool ok = _workspace.ChangeSelectedColor(GetSelectedColor(), out string message);
        RefreshCanvas(message);

        if (ok)
            SaveAutosaveQuietly();
    }

    private void ChooseCustomColor(object? sender, EventArgs e)
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = GetSelectedColor()
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var color = new NamedColor($"Пользовательский {_customColorCounter}", dialog.Color);
        _customColorCounter++;

        _colorBox.Items.Add(color);
        _colorBox.SelectedItem = color;
        ApplySelectedColor(sender, e);
    }

    private Color GetSelectedColor()
    {
        if (_colorBox.SelectedItem is NamedColor namedColor)
            return namedColor.Color;

        return Color.Firebrick;
    }

    private void RefreshCanvas(string? status = null)
    {
        _canvas.SetScene(_workspace.BuildSnapshot());
        UpdateSelectedInfo();

        if (status is not null)
            ShowStatus(status);
    }

    private void UpdateSelectedInfo()
    {
        BrickInstance? selected = _workspace.SelectedBrick;

        _selectedInfo.Text = selected is null
            ? "Выбранная деталь: нет"
            : $"Выбранная деталь:\n{selected.Definition.DisplayName}\n" +
              $"Координаты: {selected.Origin}\n" +
              $"Поворот: {selected.Rotation}";
    }

    private void ShowStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Delete:
                DeleteSelectedBrick(sender, e);
                e.Handled = true;
                break;
            case Keys.R:
                RotateSelectedBrick(sender, e);
                e.Handled = true;
                break;
            case Keys.Left:
                MoveSelectedBrick(-1, 0, 0);
                e.Handled = true;
                break;
            case Keys.Right:
                MoveSelectedBrick(1, 0, 0);
                e.Handled = true;
                break;
            case Keys.Up:
                MoveSelectedBrick(0, -1, 0);
                e.Handled = true;
                break;
            case Keys.Down:
                MoveSelectedBrick(0, 1, 0);
                e.Handled = true;
                break;
        }
    }

    private void NewWorkspace()
    {
        if (!ConfirmLossOfUnsavedChanges())
            return;

        _workspace.Clear();
        _currentFilePath = null;
        RefreshCanvas("Создан новый пустой проект.");
        SaveAutosaveQuietly();
    }

    private void SaveWorkspace()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            SaveWorkspaceAs();
            return;
        }

        SaveToPath(_currentFilePath);
    }

    private void SaveWorkspaceAs()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Lego scene (*.json)|*.json|All files (*.*)|*.*",
            FileName = "lego-scene.json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _currentFilePath = dialog.FileName;
        SaveToPath(dialog.FileName);
    }

    private void SaveToPath(string path)
    {
        try
        {
            SessionRepository.Save(path, _workspace.CreateDocument());
            ShowStatus($"Проект сохранён: {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка сохранения", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadWorkspace()
    {
        if (!ConfirmLossOfUnsavedChanges())
            return;

        using var dialog = new OpenFileDialog
        {
            Filter = "Lego scene (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            WorkspaceDocument document = SessionRepository.Load(dialog.FileName);
            _workspace.Load(document);
            _currentFilePath = dialog.FileName;
            _viewBox.SelectedItem = _workspace.View;
            RefreshCanvas($"Проект загружен: {dialog.FileName}");
            SaveAutosaveQuietly();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ConfirmLossOfUnsavedChanges()
    {
        DialogResult result = MessageBox.Show(
            this,
            "Текущая сцена будет заменена. Продолжить?",
            "Подтверждение",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        return result == DialogResult.Yes;
    }

    private void SaveAutosaveQuietly()
    {
        try
        {
            SessionRepository.Save(SessionRepository.AutosavePath, _workspace.CreateDocument());
        }
        catch
        {
            // Автосохранение не должно мешать основной работе пользователя.
            // Ошибка намеренно не показывается всплывающим окном.
        }
    }

    private void TryRestoreAutosave()
    {
        if (!File.Exists(SessionRepository.AutosavePath))
            return;

        try
        {
            WorkspaceDocument document = SessionRepository.Load(SessionRepository.AutosavePath);
            _workspace.Load(document);
            _viewBox.SelectedItem = _workspace.View;
            ShowStatus("Восстановлена последняя автосохранённая сцена.");
        }
        catch
        {
            // Если файл автосохранения повреждён, просто начинаем с пустой сцены.
            _workspace.Clear();
        }
    }

    private void ClearAutosave()
    {
        try
        {
            if (File.Exists(SessionRepository.AutosavePath))
                File.Delete(SessionRepository.AutosavePath);

            ShowStatus("Файл автосохранения очищен.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    /// Создаёт демонстрационный набор из 100 деталей.
    /// Нужен для проверки нефункционального требования по производительности.

    private void FillPerformanceDemo()
    {
        _workspace.Clear();

        BrickDefinition definition = _workspace.BrickDefinitions.First(b => b.Id == "brick_1x1");
        Color[] colors = { Color.Firebrick, Color.RoyalBlue, Color.Goldenrod, Color.SeaGreen };

        int created = 0;
        var watch = Stopwatch.StartNew();

        for (int y = 0; y < _workspace.Depth && created < 100; y += 2)
        {
            for (int x = 0; x < _workspace.Width && created < 100; x += 2)
            {
                if (_workspace.TryPlaceBrick(definition.Id, new GridPoint(x, y, 0), colors[created % colors.Length], out _))
                    created++;
            }
        }

        watch.Stop();
        RefreshCanvas($"Создано деталей: {created}. Время заполнения: {watch.ElapsedMilliseconds} мс.");
        SaveAutosaveQuietly();
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            this,
            "Учебное приложение Windows Forms для сборки простых конструкций из виртуальных деталей Лего.\n\n" +
            "Реализовано: библиотека деталей, размещение по клику, выбор, перемещение в X/Y/Z, " +
            "поворот на 90°, смена цвета, удаление, несколько ракурсов, сохранение и автосохранение.",
            "О программе",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private sealed record NamedColor(string Name, Color Color)
    {
        public override string ToString() => Name;
    }
}