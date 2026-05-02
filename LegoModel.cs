using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace LegoConstructorApp;


/// Точка дискретной трёхмерной сетки.
/// X и Y задают положение на рабочей плоскости, Z — высоту.

public readonly record struct GridPoint(int X, int Y, int Z)
{
    public override string ToString() => $"({X}; {Y}; {Z})";
}


/// Фиксированное состояние поворота детали.
/// Для учебного варианта достаточно двух состояний: вдоль X и вдоль Y.
/// Это соответствует выбранному в задании алгоритму фиксированных состояний.

public enum RotationState
{
    AlongX = 0,
    AlongY = 1
}


/// Предустановленные ракурсы просмотра сцены.
/// Смена вида не меняет логическую модель, а только способ проекции на экран.

public enum CameraView
{
    Front = 0,
    Left = 1,
    Right = 2,
    Back = 3
}


/// Русские подписи для ракурсов.

public static class ViewNames
{
    public static string GetName(CameraView view) => view switch
    {
        CameraView.Front => "Главный вид",
        CameraView.Left => "Слева от главного",
        CameraView.Right => "Справа от главного",
        CameraView.Back => "Противоположный вид",
        _ => view.ToString()
    };
}


/// Тип детали в библиотеке.
/// Размеры указаны в клетках сетки, а не в пикселях.

public sealed record BrickDefinition(
    string Id,
    string DisplayName,
    int SizeX,
    int SizeY,
    int SizeZ)
{
    public override string ToString() => DisplayName;
}


/// Конкретная деталь, размещённая на сцене.

public sealed class BrickInstance
{
    public BrickInstance(
        Guid id,
        BrickDefinition definition,
        GridPoint origin,
        RotationState rotation,
        Color color)
    {
        Id = id;
        Definition = definition;
        Origin = origin;
        Rotation = rotation;
        Color = color;
    }

    public Guid Id { get; }

    public BrickDefinition Definition { get; }


    /// Левый ближний нижний угол детали в координатах сетки.

    public GridPoint Origin { get; private set; }

    public RotationState Rotation { get; private set; }

    public Color Color { get; private set; }


    /// Текущая ширина детали по X с учётом поворота.

    public int Width => Rotation == RotationState.AlongX
        ? Definition.SizeX
        : Definition.SizeY;


    /// Текущая глубина детали по Y с учётом поворота.

    public int Depth => Rotation == RotationState.AlongX
        ? Definition.SizeY
        : Definition.SizeX;

    public int Height => Definition.SizeZ;

    public void MoveTo(GridPoint newOrigin) => Origin = newOrigin;

    public void RotateTo(RotationState newRotation) => Rotation = newRotation;

    public void ChangeColor(Color newColor) => Color = newColor;


    /// Возвращает все клетки, которые занимает деталь.
    /// Этот метод используется для проверки столкновений и выхода за границы поля.

    public IEnumerable<GridPoint> GetOccupiedCells(GridPoint origin, RotationState rotation)
    {
        int width = rotation == RotationState.AlongX
            ? Definition.SizeX
            : Definition.SizeY;

        int depth = rotation == RotationState.AlongX
            ? Definition.SizeY
            : Definition.SizeX;

        for (int z = 0; z < Definition.SizeZ; z++)
        {
            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    yield return new GridPoint(origin.X + x, origin.Y + y, origin.Z + z);
                }
            }
        }
    }
}


/// Снимок сцены для отрисовки.
/// Холст получает уже готовое состояние и не меняет модель напрямую.

public sealed record WorkspaceSnapshot(
    int Width,
    int Depth,
    int Height,
    CameraView View,
    IReadOnlyList<BrickInstance> Bricks,
    Guid? SelectedBrickId);


/// Главный сервис рабочей области.
/// Здесь реализованы алгоритмы из раздела 2: размещение, перемещение, поворот,
/// смена вида, удаление и проверка занятости клеток.

public sealed class WorkspaceService
{
    private readonly List<BrickDefinition> _definitions;
    private readonly List<BrickInstance> _bricks = new();
    private Guid?[,,] _occupied;

    private WorkspaceService(int width, int depth, int height, IEnumerable<BrickDefinition> definitions)
    {
        Width = width;
        Depth = depth;
        Height = height;
        _definitions = definitions.ToList();
        _occupied = new Guid?[width, depth, height];
    }

    public int Width { get; }

    public int Depth { get; }

    public int Height { get; }

    public CameraView View { get; set; } = CameraView.Front;

    public IReadOnlyList<BrickDefinition> BrickDefinitions => _definitions;

    public BrickInstance? SelectedBrick => SelectedBrickId.HasValue
        ? _bricks.FirstOrDefault(b => b.Id == SelectedBrickId.Value)
        : null;

    private Guid? SelectedBrickId { get; set; }


    /// Создаёт рабочую область с несколькими типами деталей.
    /// Размер поля 20×20×8 подходит для учебного варианта и спокойно держит 100 деталей.

    public static WorkspaceService CreateDefault()
    {
        return new WorkspaceService(
            width: 20,
            depth: 20,
            height: 8,
            definitions: new[]
            {
                new BrickDefinition("brick_1x1", "Кирпич 1×1×1", 1, 1, 1),
                new BrickDefinition("brick_1x2", "Кирпич 1×2×1", 1, 2, 1),
                new BrickDefinition("brick_2x2", "Кирпич 2×2×1", 2, 2, 1),
                new BrickDefinition("brick_2x3", "Кирпич 2×3×1", 2, 3, 1),
                new BrickDefinition("brick_2x4", "Кирпич 2×4×1", 2, 4, 1),
                new BrickDefinition("plate_4x4", "Пластина 4×4×1", 4, 4, 1),
                new BrickDefinition("tower_1x1x2", "Высокий блок 1×1×2", 1, 1, 2)
            });
    }

    public WorkspaceSnapshot BuildSnapshot()
    {
        return new WorkspaceSnapshot(
            Width,
            Depth,
            Height,
            View,
            _bricks.ToList(),
            SelectedBrickId);
    }


    /// Размещение новой детали по клику.
    /// Проверяется выход за границы и пересечение с уже занятыми клетками.

    public bool TryPlaceBrick(string definitionId, GridPoint origin, Color color, out string message)
    {
        BrickDefinition? definition = _definitions.FirstOrDefault(d => d.Id == definitionId);
        if (definition is null)
        {
            message = "Не найден тип детали.";
            return false;
        }

        var brick = new BrickInstance(Guid.NewGuid(), definition, origin, RotationState.AlongX, color);
        if (!CanOccupy(brick, origin, RotationState.AlongX, ignoreBrickId: null, out message))
            return false;

        _bricks.Add(brick);
        Occupy(brick, origin, RotationState.AlongX);
        SelectedBrickId = brick.Id;
        message = "Деталь размещена.";
        return true;
    }

    public void SelectBrick(Guid brickId)
    {
        SelectedBrickId = _bricks.Any(b => b.Id == brickId)
            ? brickId
            : null;
    }


    /// Прямое изменение координат выбранной детали.
    /// Это выбранный в разделе 2 алгоритм перемещения.

    public bool TryMoveSelected(int dx, int dy, int dz, out string message)
    {
        BrickInstance? brick = SelectedBrick;
        if (brick is null)
        {
            message = "Сначала выберите деталь.";
            return false;
        }

        var newOrigin = new GridPoint(
            brick.Origin.X + dx,
            brick.Origin.Y + dy,
            brick.Origin.Z + dz);

        if (!CanOccupy(brick, newOrigin, brick.Rotation, brick.Id, out message))
            return false;

        Release(brick);
        brick.MoveTo(newOrigin);
        Occupy(brick, brick.Origin, brick.Rotation);

        message = "Деталь перемещена.";
        return true;
    }


    /// Поворот на 90 градусов через переключение фиксированного состояния.
    /// Перед применением поворота снова проверяются границы и коллизии.

    public bool TryRotateSelected(out string message)
    {
        BrickInstance? brick = SelectedBrick;
        if (brick is null)
        {
            message = "Сначала выберите деталь.";
            return false;
        }

        RotationState newRotation = brick.Rotation == RotationState.AlongX
            ? RotationState.AlongY
            : RotationState.AlongX;

        if (!CanOccupy(brick, brick.Origin, newRotation, brick.Id, out message))
            return false;

        Release(brick);
        brick.RotateTo(newRotation);
        Occupy(brick, brick.Origin, brick.Rotation);

        message = "Деталь повернута.";
        return true;
    }

    public bool ChangeSelectedColor(Color color, out string message)
    {
        BrickInstance? brick = SelectedBrick;
        if (brick is null)
        {
            message = "Сначала выберите деталь.";
            return false;
        }

        brick.ChangeColor(color);
        message = "Цвет детали изменён.";
        return true;
    }

    public bool DeleteSelected(out string message)
    {
        BrickInstance? brick = SelectedBrick;
        if (brick is null)
        {
            message = "Сначала выберите деталь.";
            return false;
        }

        Release(brick);
        _bricks.Remove(brick);
        SelectedBrickId = null;
        message = "Деталь удалена.";
        return true;
    }

    public void Clear()
    {
        _bricks.Clear();
        SelectedBrickId = null;
        View = CameraView.Front;
        _occupied = new Guid?[Width, Depth, Height];
    }


    /// Создаёт документ для сохранения в JSON.
    /// Цвет хранится как ARGB-число, потому что System.Drawing.Color содержит лишние служебные поля.

    public WorkspaceDocument CreateDocument()
    {
        return new WorkspaceDocument
        {
            GridWidth = Width,
            GridDepth = Depth,
            GridHeight = Height,
            View = View,
            SelectedBrickId = SelectedBrickId,
            Bricks = _bricks.Select(b => new BrickDto
            {
                Id = b.Id,
                DefinitionId = b.Definition.Id,
                X = b.Origin.X,
                Y = b.Origin.Y,
                Z = b.Origin.Z,
                Rotation = b.Rotation,
                ColorArgb = b.Color.ToArgb()
            }).ToList()
        };
    }

    public void Load(WorkspaceDocument document)
    {
        Clear();
        View = document.View;

        foreach (BrickDto dto in document.Bricks)
        {
            BrickDefinition? definition = _definitions.FirstOrDefault(d => d.Id == dto.DefinitionId);
            if (definition is null)
                continue;

            var brick = new BrickInstance(
                dto.Id,
                definition,
                new GridPoint(dto.X, dto.Y, dto.Z),
                dto.Rotation,
                Color.FromArgb(dto.ColorArgb));

            if (CanOccupy(brick, brick.Origin, brick.Rotation, null, out _))
            {
                _bricks.Add(brick);
                Occupy(brick, brick.Origin, brick.Rotation);
            }
        }

        SelectedBrickId = document.SelectedBrickId.HasValue && _bricks.Any(b => b.Id == document.SelectedBrickId.Value)
            ? document.SelectedBrickId
            : null;
    }

    private bool CanOccupy(
        BrickInstance brick,
        GridPoint origin,
        RotationState rotation,
        Guid? ignoreBrickId,
        out string message)
    {
        foreach (GridPoint cell in brick.GetOccupiedCells(origin, rotation))
        {
            if (!IsInside(cell))
            {
                message = $"Операция невозможна: клетка {cell} выходит за границы поля.";
                return false;
            }

            Guid? occupiedBy = _occupied[cell.X, cell.Y, cell.Z];
            if (occupiedBy.HasValue && occupiedBy.Value != ignoreBrickId)
            {
                message = $"Операция невозможна: клетка {cell} уже занята другой деталью.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private bool IsInside(GridPoint cell)
    {
        return cell.X >= 0 && cell.X < Width &&
               cell.Y >= 0 && cell.Y < Depth &&
               cell.Z >= 0 && cell.Z < Height;
    }

    private void Occupy(BrickInstance brick, GridPoint origin, RotationState rotation)
    {
        foreach (GridPoint cell in brick.GetOccupiedCells(origin, rotation))
            _occupied[cell.X, cell.Y, cell.Z] = brick.Id;
    }

    private void Release(BrickInstance brick)
    {
        foreach (GridPoint cell in brick.GetOccupiedCells(brick.Origin, brick.Rotation))
        {
            if (IsInside(cell))
                _occupied[cell.X, cell.Y, cell.Z] = null;
        }
    }
}


/// DTO-документ для сохранения всей сцены.

public sealed class WorkspaceDocument
{
    public int GridWidth { get; set; }
    public int GridDepth { get; set; }
    public int GridHeight { get; set; }
    public CameraView View { get; set; }
    public Guid? SelectedBrickId { get; set; }
    public List<BrickDto> Bricks { get; set; } = new();
}


/// DTO одной детали для JSON-сохранения.

public sealed class BrickDto
{
    public Guid Id { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public RotationState Rotation { get; set; }
    public int ColorArgb { get; set; }
}


/// Сохранение и загрузка проекта.
/// JSON используется потому, что его легко проверить и приложить к отчёту.

public static class SessionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string AutosavePath
    {
        get
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LegoConstructorApp");

            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "autosave.json");
        }
    }

    public static void Save(string path, WorkspaceDocument document)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static WorkspaceDocument Load(string path)
    {
        string json = File.ReadAllText(path);
        WorkspaceDocument? document = JsonSerializer.Deserialize<WorkspaceDocument>(json, JsonOptions);

        return document ?? throw new InvalidOperationException("Файл проекта пустой или повреждён.");
    }
}


/// Пользовательский холст для отрисовки рабочей области.
/// Используется GDI+ и двойная буферизация, чтобы сцена не мерцала при перерисовке.

public sealed class LegoCanvas : Control
{
    private const float TileWidth = 46f;
    private const float TileHeight = 24f;
    private const float BrickHeight = 18f;

    private WorkspaceSnapshot? _snapshot;
    private readonly List<HitRegion> _hitRegions = new();

    public LegoCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Cursor = Cursors.Cross;
    }

    public event EventHandler<GridPoint>? EmptyCellClicked;
    public event EventHandler<Guid>? BrickClicked;

    public void SetScene(WorkspaceSnapshot snapshot)
    {
        _snapshot = snapshot;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (_snapshot is null)
            return;

        // Сначала проверяем попадание по существующей детали.
        // Идём с конца списка, потому что последние нарисованные детали визуально находятся сверху.
        for (int i = _hitRegions.Count - 1; i >= 0; i--)
        {
            if (_hitRegions[i].Bounds.Contains(e.Location.X, e.Location.Y))
            {
                BrickClicked?.Invoke(this, _hitRegions[i].BrickId);
                return;
            }
        }

        GridPoint point = ScreenToGrid(e.Location, _snapshot);
        EmptyCellClicked?.Invoke(this, point);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Color.White);

        if (_snapshot is null)
            return;

        DrawGrid(e.Graphics, _snapshot);
        DrawBricks(e.Graphics, _snapshot);
        DrawOverlayText(e.Graphics, _snapshot);
    }


    /// Преобразует клик мышью в координаты нижней плоскости Z=0.
    /// Для размещения новых деталей этого достаточно: дальше коллизии проверяются сервисом.

    private GridPoint ScreenToGrid(Point point, WorkspaceSnapshot snapshot)
    {
        PointF origin = GetScreenOrigin(snapshot);

        float dx = (point.X - origin.X) / (TileWidth / 2f);
        float dy = (point.Y - origin.Y) / (TileHeight / 2f);

        int vx = (int)Math.Floor((dx + dy) / 2f);
        int vy = (int)Math.Floor((dy - dx) / 2f);

        GridPoint logical = FromViewCoordinates(vx, vy, snapshot);
        return new GridPoint(
            Math.Clamp(logical.X, 0, snapshot.Width - 1),
            Math.Clamp(logical.Y, 0, snapshot.Depth - 1),
            0);
    }

    private void DrawGrid(Graphics graphics, WorkspaceSnapshot snapshot)
    {
        using var gridPen = new Pen(Color.FromArgb(225, 225, 225), 1f);

        for (int x = 0; x <= snapshot.Width; x++)
        {
            PointF a = ProjectToScreen(x, 0, 0, snapshot);
            PointF b = ProjectToScreen(x, snapshot.Depth, 0, snapshot);
            graphics.DrawLine(gridPen, a, b);
        }

        for (int y = 0; y <= snapshot.Depth; y++)
        {
            PointF a = ProjectToScreen(0, y, 0, snapshot);
            PointF b = ProjectToScreen(snapshot.Width, y, 0, snapshot);
            graphics.DrawLine(gridPen, a, b);
        }
    }

    private void DrawBricks(Graphics graphics, WorkspaceSnapshot snapshot)
    {
        _hitRegions.Clear();

        IEnumerable<BrickInstance> ordered = snapshot.Bricks
            .OrderBy(b => GetViewSortKey(b, snapshot.View))
            .ThenBy(b => b.Origin.Z);

        foreach (BrickInstance brick in ordered)
        {
            RectangleF bounds = DrawBrick(graphics, brick, snapshot);
            _hitRegions.Add(new HitRegion(brick.Id, bounds));
        }
    }

    private RectangleF DrawBrick(Graphics graphics, BrickInstance brick, WorkspaceSnapshot snapshot)
    {
        ViewBox box = ToViewBox(brick, snapshot);

        PointF p000 = ProjectToScreen(box.X, box.Y, brick.Origin.Z, snapshot);
        PointF p100 = ProjectToScreen(box.X + box.Width, box.Y, brick.Origin.Z, snapshot);
        PointF p110 = ProjectToScreen(box.X + box.Width, box.Y + box.Depth, brick.Origin.Z, snapshot);
        PointF p010 = ProjectToScreen(box.X, box.Y + box.Depth, brick.Origin.Z, snapshot);

        PointF p001 = ProjectToScreen(box.X, box.Y, brick.Origin.Z + brick.Height, snapshot);
        PointF p101 = ProjectToScreen(box.X + box.Width, box.Y, brick.Origin.Z + brick.Height, snapshot);
        PointF p111 = ProjectToScreen(box.X + box.Width, box.Y + box.Depth, brick.Origin.Z + brick.Height, snapshot);
        PointF p011 = ProjectToScreen(box.X, box.Y + box.Depth, brick.Origin.Z + brick.Height, snapshot);

        Color topColor = ControlPaint.Light(brick.Color, 0.35f);
        Color leftColor = ControlPaint.Dark(brick.Color, 0.10f);
        Color rightColor = ControlPaint.Dark(brick.Color, 0.25f);

        using var topBrush = new SolidBrush(topColor);
        using var leftBrush = new SolidBrush(leftColor);
        using var rightBrush = new SolidBrush(rightColor);
        using var outlinePen = new Pen(Color.FromArgb(70, 70, 70), 1.2f);
        using var selectedPen = new Pen(Color.Black, 3f);

        PointF[] leftFace = { p010, p110, p111, p011 };
        PointF[] rightFace = { p100, p110, p111, p101 };
        PointF[] topFace = { p001, p101, p111, p011 };

        graphics.FillPolygon(leftBrush, leftFace);
        graphics.FillPolygon(rightBrush, rightFace);
        graphics.FillPolygon(topBrush, topFace);

        graphics.DrawPolygon(outlinePen, leftFace);
        graphics.DrawPolygon(outlinePen, rightFace);
        graphics.DrawPolygon(outlinePen, topFace);

        // Верхние круглые выступы делают объект похожим на деталь конструктора.
        DrawStuds(graphics, brick, box, snapshot);

        if (snapshot.SelectedBrickId == brick.Id)
        {
            RectangleF bounds = GetBounds(leftFace.Concat(rightFace).Concat(topFace));
            graphics.DrawRectangle(
                selectedPen,
                bounds.X - 3,
                bounds.Y - 3,
                bounds.Width + 6,
                bounds.Height + 6);
        }

        return GetBounds(leftFace.Concat(rightFace).Concat(topFace));
    }

    private void DrawStuds(Graphics graphics, BrickInstance brick, ViewBox box, WorkspaceSnapshot snapshot)
    {
        int countX = Math.Max(1, brick.Width);
        int countY = Math.Max(1, brick.Depth);

        using var studBrush = new SolidBrush(ControlPaint.Light(brick.Color, 0.55f));
        using var studPen = new Pen(ControlPaint.Dark(brick.Color), 1f);

        for (int y = 0; y < countY; y++)
        {
            for (int x = 0; x < countX; x++)
            {
                float vx = box.X + x + 0.5f;
                float vy = box.Y + y + 0.5f;
                PointF center = ProjectToScreen(vx, vy, brick.Origin.Z + brick.Height, snapshot);

                var rect = new RectangleF(center.X - 7, center.Y - 4, 14, 8);
                graphics.FillEllipse(studBrush, rect);
                graphics.DrawEllipse(studPen, rect);
            }
        }
    }

    private void DrawOverlayText(Graphics graphics, WorkspaceSnapshot snapshot)
    {
        string text = $"Поле: {snapshot.Width}×{snapshot.Depth}×{snapshot.Height} | " +
                      $"Деталей: {snapshot.Bricks.Count} | " +
                      $"Ракурс: {ViewNames.GetName(snapshot.View)}";

        using var brush = new SolidBrush(Color.FromArgb(90, 90, 90));
        graphics.DrawString(text, Font, brush, 10, 10);
    }

    private static float GetViewSortKey(BrickInstance brick, CameraView view)
    {
        // Сортировка нужна для более корректной псевдо-3D отрисовки.
        // Чем дальше объект в ракурсе, тем раньше он рисуется.
        return view switch
        {
            CameraView.Front => brick.Origin.X + brick.Origin.Y + brick.Origin.Z,
            CameraView.Left => brick.Origin.Y + (100 - brick.Origin.X) + brick.Origin.Z,
            CameraView.Right => (100 - brick.Origin.Y) + brick.Origin.X + brick.Origin.Z,
            CameraView.Back => (100 - brick.Origin.X) + (100 - brick.Origin.Y) + brick.Origin.Z,
            _ => brick.Origin.X + brick.Origin.Y + brick.Origin.Z
        };
    }

    private ViewBox ToViewBox(BrickInstance brick, WorkspaceSnapshot snapshot)
    {
        float x0 = brick.Origin.X;
        float y0 = brick.Origin.Y;
        float x1 = brick.Origin.X + brick.Width;
        float y1 = brick.Origin.Y + brick.Depth;

        PointF[] points =
        {
            ToViewCoordinates(x0, y0, snapshot.View, snapshot),
            ToViewCoordinates(x1, y0, snapshot.View, snapshot),
            ToViewCoordinates(x1, y1, snapshot.View, snapshot),
            ToViewCoordinates(x0, y1, snapshot.View, snapshot)
        };

        float minX = points.Min(p => p.X);
        float maxX = points.Max(p => p.X);
        float minY = points.Min(p => p.Y);
        float maxY = points.Max(p => p.Y);

        return new ViewBox(minX, minY, maxX - minX, maxY - minY);
    }

    private static PointF ToViewCoordinates(float x, float y, CameraView view, WorkspaceSnapshot snapshot)
    {
        return view switch
        {
            CameraView.Front => new PointF(x, y),
            CameraView.Left => new PointF(y, snapshot.Width - x),
            CameraView.Right => new PointF(snapshot.Depth - y, x),
            CameraView.Back => new PointF(snapshot.Width - x, snapshot.Depth - y),
            _ => new PointF(x, y)
        };
    }

    private static GridPoint FromViewCoordinates(int vx, int vy, WorkspaceSnapshot snapshot)
    {
        return snapshot.View switch
        {
            CameraView.Front => new GridPoint(vx, vy, 0),
            CameraView.Left => new GridPoint(snapshot.Width - 1 - vy, vx, 0),
            CameraView.Right => new GridPoint(vy, snapshot.Depth - 1 - vx, 0),
            CameraView.Back => new GridPoint(snapshot.Width - 1 - vx, snapshot.Depth - 1 - vy, 0),
            _ => new GridPoint(vx, vy, 0)
        };
    }

    private PointF ProjectToScreen(float viewX, float viewY, float z, WorkspaceSnapshot snapshot)
    {
        PointF origin = GetScreenOrigin(snapshot);

        float screenX = origin.X + (viewX - viewY) * TileWidth / 2f;
        float screenY = origin.Y + (viewX + viewY) * TileHeight / 2f - z * BrickHeight;

        return new PointF(screenX, screenY);
    }

    private PointF GetScreenOrigin(WorkspaceSnapshot snapshot)
    {
        // Центрируем поле на холсте.
        // Высота начинается не с самого верха, чтобы было место для подписи.
        float x = Width / 2f;
        float y = 200f;

        if (snapshot.View is CameraView.Left or CameraView.Right)
            x = Width / 2f;

        return new PointF(x, y);
    }

    private static RectangleF GetBounds(IEnumerable<PointF> points)
    {
        PointF[] array = points.ToArray();
        float minX = array.Min(p => p.X);
        float maxX = array.Max(p => p.X);
        float minY = array.Min(p => p.Y);
        float maxY = array.Max(p => p.Y);
        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    private readonly record struct ViewBox(float X, float Y, float Width, float Depth);

    private readonly record struct HitRegion(Guid BrickId, RectangleF Bounds);
}